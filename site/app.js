const data = window.CGDD_DATA;
const pack = window.CGDD_LANG || { ui: { zh: {}, en: {} }, docs: { en: {} }, phrases: [] };
const phrases = (pack.phrases || []).slice().sort((a, b) => b[0].length - a[0].length);

const flowEl = document.querySelector("#flow");
const tunablesEl = document.querySelector("#tunables");
const challengeEl = document.querySelector("#challenge");
const introEl = document.querySelector("#devlog-intro");
const timelineEl = document.querySelector("#timeline");

let lang = localStorage.getItem("cgdd-lang") === "zh" ? "zh" : "en";
let openScript = null;
let flowToken = 0;

setupLangSwitch();
applyChrome();

if (!data) {
  const msg = t("data-missing");
  for (const el of [flowEl, tunablesEl, challengeEl, introEl]) {
    if (el) el.textContent = msg;
  }
} else {
  renderDocs();
  renderFlow(data.flow || "");
  probeRecording();
}

markNav();
setupScriptChrome();

async function renderFlow(source) {
  const body = (source || "").trim();
  const scriptCount = data && data.scripts ? Object.keys(data.scripts).length : 0;
  if (!body || scriptCount === 0) {
    flowEl.textContent = t("flow-no-role");
    return;
  }

  flowEl.textContent = t("flow-loading");

  const mermaid = window.mermaid;
  if (!mermaid) {
    flowEl.innerHTML = `<p class="note">${escapeHtml(t("flow-fail").replace("{message}", "Mermaid failed to load (check network / CDN)"))}</p><pre>${escapeHtml(body)}</pre>`;
    return;
  }

  try {
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: "loose",
      theme: "base",
      look: "classic",
      useMaxWidth: true,
      themeVariables: {
        darkMode: false,
        background: "transparent",
        fontFamily: '"IBM Plex Mono", "Noto Sans SC", "PingFang SC", sans-serif',
        fontSize: "16px",
        textColor: "#1a140e",
        primaryColor: "#efe6cf",
        primaryTextColor: "#1a140e",
        primaryBorderColor: "#1a140e",
        secondaryColor: "#9cba6a",
        secondaryTextColor: "#1a140e",
        secondaryBorderColor: "#1a140e",
        tertiaryColor: "#f0a202",
        tertiaryTextColor: "#1a140e",
        tertiaryBorderColor: "#1a140e",
        lineColor: "#3f4f2c",
        arrowheadColor: "#3f4f2c",
        defaultLinkColor: "#3f4f2c",
        mainBkg: "#efe6cf",
        nodeBkg: "#efe6cf",
        nodeBorder: "#1a140e",
        nodeTextColor: "#1a140e",
        clusterBkg: "#e7efd4",
        clusterBorder: "#5d7a38",
        titleColor: "#1a140e",
        edgeLabelBackground: "#efe6cf",
        radius: 16,
        strokeWidth: 2.25,
        useGradient: false
      },
      flowchart: {
        htmlLabels: true,
        curve: "basis",
        padding: 18,
        nodeSpacing: 48,
        rankSpacing: 72,
        diagramPadding: 16,
        useMaxWidth: true
      }
    });
    const token = ++flowToken;
    const { svg } = await mermaid.render("cgdd-flow-" + token, localize(body));
    if (token !== flowToken) return;
    flowEl.innerHTML = svg;
    bindScriptClicks();
    if (openScript) showScript(openScript, findNode(openScript), false);
  } catch (err) {
    flowEl.innerHTML = `<p class="note">${escapeHtml(t("flow-fail").replace("{message}", err.message))}</p><pre>${escapeHtml(body)}</pre>`;
  }
}

function bindScriptClicks() {
  for (const node of flowEl.querySelectorAll(".node")) {
    const name = nodeName(node);
    if (!name) continue;
    node.classList.add("is-script");
    node.setAttribute("role", "button");
    node.setAttribute("tabindex", "0");
    node.setAttribute("aria-pressed", "false");
    node.setAttribute("aria-label", `${name}, ${t("view-script")}`);
    const open = () => showScript(name, node, true);
    node.addEventListener("click", open);
    node.addEventListener("keydown", (event) => {
      if (event.key !== "Enter" && event.key !== " ") return;
      event.preventDefault();
      open();
    });
  }

}

function showScript(name, node, scroll) {
  const view = document.querySelector("#script-view");
  const empty = document.querySelector("#script-empty");
  const body = document.querySelector("#script-body");
  const title = document.querySelector("#script-title");
  const role = document.querySelector("#script-role");
  const path = document.querySelector("#script-path");
  const code = document.querySelector("#script-code");
  const note = document.querySelector("#script-note");
  if (!view || !code || !node) return;
  openScript = name;

  for (const other of flowEl.querySelectorAll(".node.is-selected")) {
    other.classList.remove("is-selected");
    other.setAttribute("aria-pressed", "false");
    restoreStroke(other);
  }
  node.classList.add("is-selected");
  node.setAttribute("aria-pressed", "true");
  markStroke(node);

  const entry = (data.scripts || {})[name];
  empty.hidden = true;
  body.hidden = false;
  title.textContent = name;
  role.textContent = entry && entry.role ? localize(entry.role) : "";
  path.textContent = entry && entry.path ? entry.path : "";
  code.textContent = entry && entry.source
    ? localize(String(entry.source).replace(/\r\n/g, "\n"))
    : t("script-missing");
  if (note) {
    note.hidden = lang !== "en";
    note.textContent = t("script-note");
  }
  if (scroll && window.matchMedia("(max-width: 800px)").matches) {
    view.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }
}

function findNode(name) {
  return [...flowEl.querySelectorAll(".node")].find((node) => nodeName(node) === name) || null;
}

function setupScriptChrome() {
  const close = document.querySelector("#script-close");
  const empty = document.querySelector("#script-empty");
  const body = document.querySelector("#script-body");
  const code = document.querySelector("#script-code");
  const note = document.querySelector("#script-note");
  if (!close) return;
  close.addEventListener("click", () => {
    openScript = null;
    empty.hidden = false;
    body.hidden = true;
    if (note) note.hidden = true;
    code.textContent = "";
    for (const other of flowEl.querySelectorAll(".node.is-selected")) {
      other.classList.remove("is-selected");
      other.setAttribute("aria-pressed", "false");
      restoreStroke(other);
    }
  });
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape" || body.hidden) return;
    close.click();
  });
}

function setupLangSwitch() {
  const group = document.querySelector(".lang-switch");
  if (!group) return;
  group.addEventListener("click", (event) => {
    const button = event.target.closest("button[data-lang]");
    if (!button) return;
    setLang(button.dataset.lang);
  });
}

function setLang(next) {
  if (next !== "zh" && next !== "en") return;
  if (next === lang) return;
  lang = next;
  localStorage.setItem("cgdd-lang", lang);
  applyChrome();
  if (!data) {
    const msg = t("data-missing");
    for (const el of [flowEl, tunablesEl, challengeEl, introEl]) {
      if (el) el.textContent = msg;
    }
    return;
  }
  renderDocs();
  renderFlow(data.flow || "");
}

function applyChrome() {
  document.documentElement.lang = lang === "en" ? "en" : "zh-CN";
  document.documentElement.dataset.lang = lang;
  document.title = t("title");
  const description = document.querySelector('meta[name="description"]');
  if (description) description.setAttribute("content", t("description"));
  for (const el of document.querySelectorAll("[data-i18n]")) {
    el.textContent = t(el.dataset.i18n);
  }
  for (const el of document.querySelectorAll("[data-i18n-html]")) {
    el.innerHTML = t(el.dataset.i18nHtml);
  }
  for (const el of document.querySelectorAll("[data-i18n-aria]")) {
    el.setAttribute("aria-label", t(el.dataset.i18nAria));
  }
  for (const button of document.querySelectorAll(".lang-switch button")) {
    button.setAttribute("aria-pressed", button.dataset.lang === lang ? "true" : "false");
  }
}

function renderDocs() {
  tunablesEl.innerHTML = markdown(localize(data.tunables || ""));
  challengeEl.innerHTML = markdown(docText("challenge"));
  renderDevlog(docText("devlog"));
}

function docText(name) {
  const raw = data[name] || "";
  if (lang === "en") return raw;
  const ready = pack.docs && pack.docs.zh && pack.docs.zh[name];
  return ready || localize(raw);
}

function localize(text) {
  if (lang !== "zh" || text == null) return text || "";
  let out = String(text);
  for (const pair of phrases) out = out.split(pair[0]).join(pair[1]);
  return out;
}

function t(key) {
  const table = (pack.ui && pack.ui[lang]) || {};
  return table[key] != null ? table[key] : key;
}

function nodeName(node) {
  const bold = node.querySelector("b");
  const fromLabel = bold && bold.textContent.trim();
  if (fromLabel) return fromLabel;
  const match = (node.id || "").match(/(?:^|-)([A-Za-z_][A-Za-z0-9_]*)-\d+$/);
  return match ? match[1] : "";
}

function markStroke(node) {
  const rect = node.querySelector("rect.basic");
  if (!rect || rect.dataset.prevStyle != null) return;
  rect.dataset.prevStyle = rect.getAttribute("style") || "";
  const next = rect.dataset.prevStyle
    .replace(/stroke:[^;]*/i, "stroke:#12160f !important")
    .replace(/stroke-width:[^;]*/i, "stroke-width:4px !important");
  rect.setAttribute("style", next);
}

function restoreStroke(node) {
  const rect = node.querySelector("rect.basic");
  if (!rect || rect.dataset.prevStyle == null) return;
  rect.setAttribute("style", rect.dataset.prevStyle);
  delete rect.dataset.prevStyle;
}

function renderDevlog(source) {
  const lines = source.replace(/\r\n/g, "\n").split("\n");
  const intro = [];
  const events = [];
  let current = null;
  for (const line of lines) {
    if (line.startsWith("## ")) {
      if (current) events.push(current);
      current = { title: line.slice(3).trim(), lines: [] };
    } else if (current) {
      current.lines.push(line);
    } else {
      intro.push(line);
    }
  }
  if (current) events.push(current);

  introEl.innerHTML = markdown(intro.join("\n"));
  timelineEl.innerHTML = "";
  for (const event of events) {
    const item = document.createElement("li");
    item.className = "event";
    if (event.title.includes("EXAMPLE")) item.classList.add("is-example");
    if (event.title.includes("Stage Reflection")) item.classList.add("is-reflection");
    const card = document.createElement("article");
    card.className = "card";
    const stamp = event.title.includes("EXAMPLE") ? `<p class="stamp">${escapeHtml(t("example-stamp"))}</p>` : "";
    card.innerHTML = `${stamp}<h2>${inline(event.title)}</h2>${markdown(event.lines.join("\n"))}`;
    item.appendChild(card);
    timelineEl.appendChild(item);
  }
}

function probeRecording() {
  const video = document.querySelector("#rec");
  const fallback = document.querySelector("#screen-fallback");
  if (!video || !fallback) return;
  // file:// often blocks fetch/HEAD; leave the poster and skip quietly
  if (location.protocol === "file:") return;
  fetch("media/play.mp4", { method: "HEAD" })
    .then((res) => {
      if (!res.ok) return;
      video.poster = "media/poster.svg";
      video.src = "media/play.mp4";
      video.hidden = false;
      fallback.hidden = true;
    })
    .catch(() => {});
}

function markNav() {
  const links = [...document.querySelectorAll("nav a")];
  const sections = links
    .map((link) => document.querySelector(link.getAttribute("href")))
    .filter(Boolean);
  if (!("IntersectionObserver" in window)) return;
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      for (const link of links) {
        link.setAttribute("aria-current", link.getAttribute("href") === `#${entry.target.id}` ? "true" : "false");
      }
    }
  }, { rootMargin: "-40% 0px -50% 0px" });
  for (const section of sections) observer.observe(section);
}

function markdown(source) {
  const lines = source.replace(/\r\n/g, "\n").split("\n");
  const out = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) {
      i += 1;
      continue;
    }
    const heading = /^(#{1,3})\s+(.*)$/.exec(line);
    if (heading) {
      const level = heading[1].length;
      out.push(`<h${level}>${inline(heading[2])}</h${level}>`);
      i += 1;
      continue;
    }
    if (line.includes("|") && i + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1])) {
      const headers = splitRow(line);
      i += 2;
      const rows = [];
      while (i < lines.length && lines[i].includes("|") && lines[i].trim()) {
        rows.push(splitRow(lines[i]));
        i += 1;
      }
      out.push(table(headers, rows));
      continue;
    }
    if (line.startsWith("- ")) {
      const items = [];
      while (i < lines.length && lines[i].startsWith("- ")) {
        items.push(`<li>${inline(lines[i].slice(2))}</li>`);
        i += 1;
      }
      out.push(`<ul>${items.join("")}</ul>`);
      continue;
    }
    const para = [];
    while (i < lines.length && lines[i].trim() && !lines[i].startsWith("#") && !lines[i].startsWith("- ") && !isTableStart(lines, i)) {
      para.push(lines[i]);
      i += 1;
    }
    out.push(`<p>${inline(para.join(" "))}</p>`);
  }
  return out.join("");
}

function isTableStart(lines, index) {
  return lines[index].includes("|") && index + 1 < lines.length && /^\s*\|?\s*:?-{3,}/.test(lines[index + 1]);
}

function splitRow(line) {
  const trimmed = line.trim().replace(/^\|/, "").replace(/\|$/, "");
  return trimmed.split("|").map((cell) => cell.trim());
}

function table(headers, rows) {
  const head = headers.map((cell) => `<th>${inline(cell)}</th>`).join("");
  const body = rows.map((row) => `<tr>${row.map((cell) => `<td>${inline(cell)}</td>`).join("")}</tr>`).join("");
  return `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
}

function inline(text) {
  return escapeHtml(text)
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
}

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}
