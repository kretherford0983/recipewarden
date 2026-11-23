// AC-96, NFR-03, ADR 0045: an in-page accessibility check for the rules axe rates "serious" or "critical".
// axe-core is MPL-2.0 (weak copyleft), so it is not used (permissive licences only). Returns a list of
// { rule, target, detail } problems; an empty list passes.
(() => {
  "use strict";
  const problems = [];
  const report = (rule, el, detail) => problems.push({
    rule,
    target: el ? describe(el) : "document",
    detail: detail || "",
  });

  function describe(el) {
    let s = el.tagName.toLowerCase();
    if (el.id) { s += "#" + el.id; }
    if (el.className && typeof el.className === "string") { s += "." + el.className.trim().split(/\s+/).join("."); }
    const text = (el.textContent || "").trim().replace(/\s+/g, " ").slice(0, 40);
    return text ? s + " \"" + text + "\"" : s;
  }

  function visible(el) {
    if (!el.isConnected) { return false; }
    for (let e = el; e; e = e.parentElement) {
      const cs = getComputedStyle(e);
      if (cs.display === "none" || cs.visibility === "hidden") { return false; }
      if (e.tagName === "DETAILS" && !e.open && e !== el && !el.closest("summary")) {
        // Content of a closed <details> is not rendered.
        if (e.contains(el)) { return false; }
      }
    }
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  function hiddenFromAT(el) {
    for (let e = el; e; e = e.parentElement) {
      if (e.getAttribute && e.getAttribute("aria-hidden") === "true") { return true; }
    }
    return false;
  }

  function byIds(list) {
    return (list || "").split(/\s+/).filter(Boolean).map((id) => document.getElementById(id));
  }

  function name(el) {
    const labelledby = el.getAttribute("aria-labelledby");
    if (labelledby) {
      const t = byIds(labelledby).filter(Boolean).map((e) => e.textContent).join(" ").trim();
      if (t) { return t; }
    }
    const aria = (el.getAttribute("aria-label") || "").trim();
    if (aria) { return aria; }
    if (el.labels && el.labels.length) {
      const t = Array.from(el.labels).map((l) => l.textContent).join(" ").trim();
      if (t) { return t; }
    }
    if (el.tagName === "INPUT" && ["submit", "button", "reset"].includes(el.type) && (el.value || "").trim()) {
      return el.value.trim();
    }
    if (el.tagName === "INPUT" && el.type === "image" && (el.alt || "").trim()) { return el.alt.trim(); }
    if (["A", "BUTTON", "SUMMARY"].includes(el.tagName)) {
      let t = "";
      el.childNodes.forEach(function walk(n) {
        if (n.nodeType === 3) { t += n.textContent; }
        else if (n.nodeType === 1 && n.getAttribute("aria-hidden") !== "true") {
          if (n.tagName === "IMG" || n.tagName === "svg" || n.tagName === "SVG") {
            t += n.getAttribute("alt") || n.getAttribute("aria-label") || "";
          } else {
            n.childNodes.forEach(walk);
          }
        }
      });
      if (t.trim()) { return t.trim(); }
    }
    return (el.getAttribute("title") || "").trim();
  }

  // html-has-lang, html-lang-valid (serious)
  const lang = document.documentElement.getAttribute("lang");
  if (!lang || !/^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$/.test(lang)) { report("html-has-lang", null, String(lang)); }

  // document-title (serious)
  if (!document.title.trim()) { report("document-title", null); }

  // meta-viewport (critical): zoom must not be disabled.
  const viewport = document.querySelector('meta[name="viewport"]');
  if (viewport && /user-scalable\s*=\s*(no|0)|maximum-scale\s*=\s*(0?\.\d+|1(\.0+)?)\b/i.test(viewport.content)) {
    report("meta-viewport", viewport, viewport.content);
  }

  // image-alt (critical)
  document.querySelectorAll("img").forEach((img) => {
    if (!img.hasAttribute("alt") && !hiddenFromAT(img) && img.getAttribute("role") !== "presentation") {
      report("image-alt", img, img.getAttribute("src"));
    }
  });

  // label (critical), select-name (critical)
  document.querySelectorAll("input, select, textarea").forEach((el) => {
    if (el.type === "hidden" || hiddenFromAT(el)) { return; }
    if (["submit", "button", "reset"].includes(el.type)) {
      if (!name(el)) { report("input-button-name", el); }
      return;
    }
    if (!name(el) && !(el.getAttribute("placeholder") || "").trim()) { report("label", el, el.getAttribute("name") || ""); }
    else if (!name(el)) { report("label", el, "placeholder only: " + el.getAttribute("name")); }
  });

  // button-name (critical), link-name (serious)
  document.querySelectorAll("button, [role=button]").forEach((el) => {
    if (!hiddenFromAT(el) && !name(el)) { report("button-name", el); }
  });
  document.querySelectorAll("a[href]").forEach((el) => {
    if (!hiddenFromAT(el) && !name(el)) { report("link-name", el, el.getAttribute("href")); }
  });

  // duplicate-id-aria (critical) and broken references
  const counts = {};
  document.querySelectorAll("[id]").forEach((el) => { counts[el.id] = (counts[el.id] || 0) + 1; });
  document.querySelectorAll("[aria-labelledby], [aria-describedby], [aria-controls], label[for]").forEach((el) => {
    const refs = [el.getAttribute("aria-labelledby"), el.getAttribute("aria-describedby"), el.getAttribute("aria-controls"), el.getAttribute("for")]
      .filter(Boolean).join(" ").split(/\s+/).filter(Boolean);
    refs.forEach((id) => {
      if (!counts[id]) { report("aria-valid-reference", el, id); }
      else if (counts[id] > 1) { report("duplicate-id-aria", el, id); }
    });
  });

  // aria-roles (critical): only known roles.
  const roles = new Set(["alert", "alertdialog", "application", "article", "banner", "button", "cell", "checkbox", "columnheader",
    "combobox", "complementary", "contentinfo", "dialog", "document", "feed", "figure", "form", "grid", "gridcell", "group",
    "heading", "img", "link", "list", "listbox", "listitem", "log", "main", "marquee", "math", "menu", "menubar", "menuitem",
    "menuitemcheckbox", "menuitemradio", "navigation", "none", "note", "option", "presentation", "progressbar", "radio",
    "radiogroup", "region", "row", "rowgroup", "rowheader", "scrollbar", "search", "searchbox", "separator", "slider",
    "spinbutton", "status", "switch", "tab", "table", "tablist", "tabpanel", "term", "textbox", "timer", "toolbar", "tooltip",
    "tree", "treegrid", "treeitem"]);
  document.querySelectorAll("[role]").forEach((el) => {
    el.getAttribute("role").split(/\s+/).filter(Boolean).forEach((r) => { if (!roles.has(r)) { report("aria-roles", el, r); } });
  });

  // landmark-one-main (moderate in axe, kept: every page has exactly one) and page-has-heading-one
  if (document.querySelectorAll("main, [role=main]").length !== 1) { report("landmark-one-main", null); }
  if (document.querySelectorAll("h1").length < 1) { report("page-has-heading-one", null); }

  // list (serious): ul/ol hold only li (and script/template).
  document.querySelectorAll("ul, ol").forEach((list) => {
    Array.from(list.children).forEach((c) => {
      if (!["LI", "SCRIPT", "TEMPLATE"].includes(c.tagName)) { report("list", list, c.tagName); }
    });
  });

  // nested-interactive (serious)
  document.querySelectorAll("a[href], button").forEach((el) => {
    const inner = el.querySelector("a[href], button, input:not([type=hidden]), select, textarea");
    if (inner) { report("nested-interactive", el, describe(inner)); }
  });

  // color-contrast (serious): 4.5:1, or 3:1 for large text, against the first solid background behind the text.
  function parse(c) {
    const m = c.match(/rgba?\(([^)]+)\)/);
    if (!m) { return null; }
    const p = m[1].split(/[\s,/]+/).filter(Boolean).map(Number);
    return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 };
  }

  function blend(top, bottom) {
    const a = top.a;
    return { r: top.r * a + bottom.r * (1 - a), g: top.g * a + bottom.g * (1 - a), b: top.b * a + bottom.b * (1 - a), a: 1 };
  }

  function background(el) {
    const layers = [];
    for (let e = el; e; e = e.parentElement) {
      const cs = getComputedStyle(e);
      if (cs.backgroundImage && cs.backgroundImage !== "none") { return null; } // cannot tell; skip
      const bg = parse(cs.backgroundColor);
      if (bg && bg.a > 0) {
        layers.push(bg);
        if (bg.a >= 1) { break; }
      }
    }

    let result = { r: 255, g: 255, b: 255, a: 1 };
    for (let i = layers.length - 1; i >= 0; i--) { result = blend(layers[i], result); }
    return result;
  }

  function luminance(c) {
    const f = (v) => {
      v /= 255;
      return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
  }

  function ratio(a, b) {
    const l1 = luminance(a);
    const l2 = luminance(b);
    return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
  }

  const seen = new Set();
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const el = n.parentElement;
    if (!el || seen.has(el) || !n.textContent.trim()) { continue; }
    seen.add(el);
    if (["SCRIPT", "STYLE", "NOSCRIPT", "OPTION"].includes(el.tagName) || !visible(el) || hiddenFromAT(el)) { continue; }
    if (el.closest(":disabled, [aria-disabled=true]")) { continue; } // disabled controls are exempt (WCAG 1.4.3)
    const cs = getComputedStyle(el);
    if (cs.clip === "rect(0px, 0px, 0px, 0px)" || cs.clipPath === "inset(50%)") { continue; } // visually hidden
    let opacity = 1;
    for (let e = el; e; e = e.parentElement) { opacity *= Number(getComputedStyle(e).opacity); }
    const bg = background(el);
    const fg = parse(cs.color);
    if (!bg || !fg) { continue; }
    const shown = blend({ r: fg.r, g: fg.g, b: fg.b, a: fg.a * opacity }, bg);
    const size = parseFloat(cs.fontSize);
    const bold = Number(cs.fontWeight) >= 700;
    const large = size >= 24 || (bold && size >= 18.66);
    const need = large ? 3 : 4.5;
    const got = ratio(shown, bg);
    if (got + 0.005 < need) {
      report("color-contrast", el, got.toFixed(2) + ":1, needs " + need + ":1 (" + cs.color + " on rgb(" +
        [bg.r, bg.g, bg.b].map(Math.round).join(", ") + "))");
    }
  }

  return problems;
})()
