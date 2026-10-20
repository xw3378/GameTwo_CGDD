// Menu: CGDD/Export Trace Docs
// Scans gameplay script header comments under Assets, writes the flowchart and tunables table, and syncs site/data.

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class TraceDocsExporter
{
    [MenuItem("CGDD/Export Trace Docs", false, 20)]
    public static void Export()
    {
        try
        {
            ExportInternal();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Export failed", ex.Message, "OK");
        }
    }

    // Unity -batchmode -executeMethod TraceDocsExporter.ExportCli
    public static void ExportCli()
    {
        try
        {
            ExportInternal();
            EditorApplication.Exit(0);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    static void ExportInternal()
    {
        var scripts = Scan();
        string flow = BuildFlow(scripts);
        string tunables = BuildTunables(scripts);

        string root = Directory.GetParent(Application.dataPath).FullName;
        var utf8 = new UTF8Encoding(false);
        string docs = Path.Combine(root, "docs");
        string site = Path.Combine(root, "site", "data");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(site);

        File.WriteAllText(Path.Combine(docs, "script-flow.mmd"), flow, utf8);
        File.WriteAllText(Path.Combine(docs, "tunables.md"), tunables, utf8);
        File.WriteAllText(Path.Combine(site, "script-flow.mmd"), flow, utf8);
        File.WriteAllText(Path.Combine(site, "tunables.md"), tunables, utf8);

        string challenge = ReadOr(Path.Combine(docs, "challenge-map.md"), "(missing docs/challenge-map.md)\n");
        string devlog = ReadOr(Path.Combine(root, "DEVLOG.md"), "(missing DEVLOG.md)\n");
        File.WriteAllText(Path.Combine(site, "challenge-map.md"), challenge, utf8);
        File.WriteAllText(Path.Combine(site, "DEVLOG.md"), devlog, utf8);

        string json = "{\"flow\":" + Quote(flow)
            + ",\"tunables\":" + Quote(tunables)
            + ",\"challenge\":" + Quote(challenge)
            + ",\"devlog\":" + Quote(devlog)
            + ",\"scripts\":" + BuildScriptsJson(scripts) + "}";
        File.WriteAllText(Path.Combine(site, "bundle.js"), "window.CGDD_DATA = " + json + ";\n", utf8);

        CgddBootstrap.ApplySiteIdentity();
        AssetDatabase.Refresh();

        int tunes = 0;
        foreach (var script in scripts) tunes += script.Tunes.Count;
        Debug.Log($"CGDD/Export Trace Docs: {scripts.Count} script(s), {tunes} tunable(s). Wrote docs/ and site/data/.");

        if (scripts.Count == 0)
        {
            CgddBootstrap.OfferOpenSite(
                "Export complete — no @role scripts",
                "Wrote docs/ and site/data/, but found 0 gameplay scripts with // @role.\n\n"
                + "Ask your Agent to annotate gameplay scripts with @role / @calls / @tune, then Export again.\n"
                + "The site header already uses the Unity product name; DEVLOG and challenge cards still load.");
        }
        else
        {
            CgddBootstrap.OfferOpenSite(
                "Export complete",
                $"Wrote docs/ and site/data/.\n{scripts.Count} script(s), {tunes} tunable(s).\n\nOpen via CGDD → Open Student Site anytime (Mermaid needs internet once for the CDN).");
        }
    }

    sealed class ScriptDoc
    {
        public string Name;
        public string Role;
        public string Path;
        public string Source;
        public readonly List<string> Calls = new List<string>();
        public readonly List<TuneDoc> Tunes = new List<TuneDoc>();
    }

    sealed class TuneDoc
    {
        public string Name;
        public string Type;
        public string Range;
        public string Note;
        public string DefaultValue;
    }

    static List<ScriptDoc> Scan()
    {
        var roleRx = new Regex(@"^@role\s+(.+)$");
        var callsRx = new Regex(@"^@calls\s+(.+)$");
        var tuneRx = new Regex(@"^@tune\s+([^:\s]+)\s*:\s*([^:\s]+)\s*:\s*([^:]+)\s*:\s*(.+)$");
        var found = new List<ScriptDoc>();
        string[] files = Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories);

        foreach (string file in files)
        {
            string norm = file.Replace('\\', '/');
            if (norm.Contains("/CGDD/Editor/") || norm.Contains("/Editor/")) continue;

            string source = File.ReadAllText(file);
            string role = null;
            var calls = new List<string>();
            var tunes = new List<TuneDoc>();

            foreach (string raw in source.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("//")) continue;
                string body = line.Substring(2).Trim();

                var roleMatch = roleRx.Match(body);
                if (roleMatch.Success)
                {
                    if (role == null) role = roleMatch.Groups[1].Value.Trim();
                    continue;
                }

                var callsMatch = callsRx.Match(body);
                if (callsMatch.Success)
                {
                    foreach (string part in Regex.Split(callsMatch.Groups[1].Value.Trim(), @"[,，\s]+"))
                    {
                        if (!string.IsNullOrEmpty(part)) calls.Add(part);
                    }
                    continue;
                }

                var tuneMatch = tuneRx.Match(body);
                if (!tuneMatch.Success) continue;
                tunes.Add(new TuneDoc
                {
                    Name = tuneMatch.Groups[1].Value.Trim(),
                    Type = tuneMatch.Groups[2].Value.Trim(),
                    Range = tuneMatch.Groups[3].Value.Trim(),
                    Note = tuneMatch.Groups[4].Value.Trim()
                });
            }

            if (string.IsNullOrEmpty(role)) continue;

            string name = ClassName(source, Path.GetFileNameWithoutExtension(file));
            var doc = new ScriptDoc
            {
                Name = name,
                Role = role,
                Path = ToProjectPath(file),
                Source = source
            };
            foreach (string call in calls)
            {
                if (call == name || doc.Calls.Contains(call)) continue;
                doc.Calls.Add(call);
            }

            foreach (var tune in tunes)
            {
                tune.DefaultValue = FindDefault(source, tune.Name, tune.Type);
                doc.Tunes.Add(tune);
            }

            found.Add(doc);
        }

        found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return found;
    }

    static string ClassName(string source, string fileName)
    {
        var matches = Regex.Matches(source, @"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)");
        string first = null;
        foreach (Match match in matches)
        {
            string name = match.Groups[1].Value;
            if (first == null) first = name;
            if (name == fileName) return fileName;
        }

        return first ?? fileName;
    }

    static string FindDefault(string source, string name, string type)
    {
        var rx = new Regex(@"\b" + Regex.Escape(type) + @"\s+" + Regex.Escape(name) + @"\s*=\s*(-?\d+(?:\.\d+)?)f?\b");
        var match = rx.Match(source);
        if (!match.Success) return "—";
        return TrimNum(match.Groups[1].Value);
    }

    static string TrimNum(string value)
    {
        if (value.IndexOf('.') < 0) return value;
        value = value.TrimEnd('0').TrimEnd('.');
        return value.Length == 0 ? "0" : value;
    }

    static string ProjectLabel()
    {
        string name = Application.productName;
        if (string.IsNullOrWhiteSpace(name) || name == "ProductName")
            name = new DirectoryInfo(Directory.GetParent(Application.dataPath).FullName).Name;
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    static string BuildFlow(List<ScriptDoc> scripts)
    {
        // Empty flow: leave body blank so the site shows "No @role scripts yet" instead of drawing forever.
        if (scripts.Count == 0)
            return "";

        var known = new HashSet<string>();
        foreach (var script in scripts) known.Add(script.Name);

        var extras = new List<string>();
        foreach (var script in scripts)
        {
            foreach (string call in script.Calls)
            {
                if (known.Contains(call) || extras.Contains(call)) continue;
                extras.Add(call);
            }
        }
        extras.Sort(string.CompareOrdinal);

        var sb = new StringBuilder();
        sb.Append("%% ").Append(ProjectLabel()).Append(" script flow\n");
        sb.Append("%% Generated by CGDD/Export Trace Docs. Do not edit by hand.\n");
        sb.Append("flowchart TD\n");
        foreach (var script in scripts)
            sb.Append("  ").Append(Node(script.Name, script.Role)).Append('\n');
        foreach (string extra in extras)
            sb.Append("  ").Append(Node(extra, "script missing")).Append('\n');

        sb.Append('\n');
        foreach (var script in scripts)
        {
            foreach (string call in script.Calls)
                sb.Append("  ").Append(script.Name).Append(" --> ").Append(call).Append('\n');
        }

        sb.Append('\n');
        AppendFlowClasses(sb, scripts, extras);
        return sb.ToString();
    }

    static string Node(string name, string role)
    {
        string safe = role.Replace("\"", "'").Replace("[", "(").Replace("]", ")").Replace("<", "‹").Replace(">", "›");
        return name + "(\"<b>" + name + "</b><br/>" + safe + "\")";
    }

    static void AppendFlowClasses(StringBuilder sb, List<ScriptDoc> scripts, List<string> extras)
    {
        sb.Append("  classDef manager fill:#f0a202,stroke:#1a140e,color:#1a140e,stroke-width:2.5px\n");
        sb.Append("  classDef actor fill:#fff8ea,stroke:#1a140e,color:#1a140e,stroke-width:2px\n");
        sb.Append("  classDef world fill:#9cba6a,stroke:#1a140e,color:#1a140e,stroke-width:2px\n");
        sb.Append("  classDef ui fill:#efe6cf,stroke:#c4841a,color:#1a140e,stroke-width:2px\n");
        sb.Append("  classDef other fill:#efe6cf,stroke:#1a140e,color:#1a140e,stroke-width:2px\n");

        var groups = new Dictionary<string, List<string>>();
        foreach (var script in scripts) AddFlowClass(groups, script.Name);
        foreach (string extra in extras) AddFlowClass(groups, extra);

        foreach (string key in new[] { "manager", "actor", "world", "ui", "other" })
        {
            if (!groups.TryGetValue(key, out var names) || names.Count == 0) continue;
            sb.Append("  class ").Append(string.Join(",", names)).Append(' ').Append(key).Append('\n');
        }
    }

    static void AddFlowClass(Dictionary<string, List<string>> groups, string name)
    {
        string key = FlowClass(name);
        if (!groups.TryGetValue(key, out var names))
        {
            names = new List<string>();
            groups[key] = names;
        }

        if (!names.Contains(name)) names.Add(name);
    }

    static string FlowClass(string name)
    {
        if (NameHas(name, "Manager") || NameHas(name, "Controller") || NameHas(name, "System")) return "manager";
        if (NameHas(name, "Player") || NameHas(name, "Projectile") || NameHas(name, "Actor") || NameHas(name, "Character")) return "actor";
        if (NameHas(name, "Spawner") || NameHas(name, "Enemy") || NameHas(name, "Zombie") || NameHas(name, "Base") || NameHas(name, "World") || NameHas(name, "Environment")) return "world";
        if (NameHas(name, "HUD") || NameHas(name, "UI") || NameHas(name, "Result") || NameHas(name, "Menu")) return "ui";
        return "other";
    }

    static bool NameHas(string name, string token)
    {
        return name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static string ToProjectPath(string file)
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string rel = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return rel.Replace('\\', '/');
    }

    static string BuildScriptsJson(List<ScriptDoc> scripts)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        for (int i = 0; i < scripts.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var script = scripts[i];
            sb.Append(Quote(script.Name));
            sb.Append(":{");
            sb.Append("\"path\":").Append(Quote(script.Path));
            sb.Append(",\"role\":").Append(Quote(script.Role));
            sb.Append(",\"source\":").Append(Quote(script.Source));
            sb.Append('}');
        }

        sb.Append('}');
        return sb.ToString();
    }

    static string BuildTunables(List<ScriptDoc> scripts)
    {
        var sb = new StringBuilder();
        sb.Append("# Tunables\n\n");
        sb.Append("Generated from `@tune` headers by the menu `CGDD/Export Trace Docs`. Hand edits are overwritten on the next export.\n\n");
        sb.Append("| Script | Field | Type | Range | Default | Note |\n");
        sb.Append("| --- | --- | --- | --- | --- | --- |\n");
        foreach (var script in scripts)
        {
            foreach (var tune in script.Tunes)
            {
                sb.Append("| ").Append(script.Name)
                    .Append(" | ").Append(tune.Name)
                    .Append(" | ").Append(tune.Type)
                    .Append(" | ").Append(tune.Range)
                    .Append(" | ").Append(tune.DefaultValue)
                    .Append(" | ").Append(tune.Note.Replace("|", "/"))
                    .Append(" |\n");
            }
        }

        sb.Append('\n');
        return sb.ToString();
    }

    static string ReadOr(string path, string fallback)
    {
        return File.Exists(path) ? File.ReadAllText(path) : fallback;
    }

    static string Quote(string value)
    {
        if (value == null) return "\"\"";
        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u");
                        sb.Append(((int)c).ToString("x4"));
                    }
                    else sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
