// Menu: CGDD/Teacher/Export Toolkit Package
// Exports Assets/CGDD to dist/CGDD_StudentToolkit.unitypackage

using System.IO;
using UnityEditor;
using UnityEngine;

public static class CgddPackageExporter
{
    const string AssetFolder = "Assets/CGDD";
    const string OutRelative = "dist/CGDD_StudentToolkit.unitypackage";

    [MenuItem("CGDD/Teacher/Export Toolkit Package", false, 110)]
    public static void Export()
    {
        ExportInternal(showDialog: true);
    }

    // Unity -batchmode -executeMethod CgddPackageExporter.ExportCli
    public static void ExportCli()
    {
        ExportInternal(showDialog: false);
    }

    static void ExportInternal(bool showDialog)
    {
        if (!AssetDatabase.IsValidFolder(AssetFolder))
        {
            string err = "Missing folder: " + AssetFolder;
            Debug.LogError(err);
            if (showDialog) EditorUtility.DisplayDialog("Export failed", err, "OK");
            if (!showDialog) EditorApplication.Exit(1);
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outPath = Path.Combine(projectRoot, OutRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath));

        AssetDatabase.ExportPackage(
            new[] { AssetFolder },
            outPath,
            ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);

        Debug.Log("CGDD toolkit package → " + outPath);
        if (showDialog)
            EditorUtility.DisplayDialog("Package ready", "Wrote:\n" + outPath, "OK");
        if (!showDialog)
            EditorApplication.Exit(0);
    }
}
