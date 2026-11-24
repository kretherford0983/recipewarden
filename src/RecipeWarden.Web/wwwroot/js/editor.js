// FR-REC-02: reorder ingredient and step lines by dragging, or with the Up/Down buttons (keyboard, NFR-03).
// The textarea stays the source of truth: every move rewrites it, so the form works without this script.
(function () {
  "use strict";

  function lines(textarea) {
    return textarea.value.split("\n").filter(function (l) { return l.trim().length > 0; });
  }

  function write(textarea, items) {
    textarea.value = items.join("\n");
    textarea.dispatchEvent(new Event("input", { bubbles: true })); // refreshes the live parse (htmx)
  }

  function render(textarea, list) {
    var items = lines(textarea);
    list.replaceChildren();
    items.forEach(function (text, index) {
      var li = document.createElement("li");
      li.draggable = true;
      li.dataset.index = String(index);

      var label = document.createElement("span");
      label.className = "reorder-text";
      label.textContent = text; // text only, never HTML

      var up = document.createElement("button");
      up.type = "button";
      up.textContent = "↑";
      up.disabled = index === 0;
      up.setAttribute("aria-label", "Move up: " + text);
      up.addEventListener("click", function () { move(textarea, list, index, index - 1); });

      var down = document.createElement("button");
      down.type = "button";
      down.textContent = "↓";
      down.disabled = index === items.length - 1;
      down.setAttribute("aria-label", "Move down: " + text);
      down.addEventListener("click", function () { move(textarea, list, index, index + 1); });

      li.addEventListener("dragstart", function (e) { e.dataTransfer.setData("text/plain", String(index)); });
      li.addEventListener("dragover", function (e) { e.preventDefault(); });
      li.addEventListener("drop", function (e) {
        e.preventDefault();
        var from = parseInt(e.dataTransfer.getData("text/plain"), 10);
        if (!isNaN(from)) { move(textarea, list, from, index); }
      });

      li.append(label, up, down);
      list.append(li);
    });
  }

  function move(textarea, list, from, to) {
    var items = lines(textarea);
    if (to < 0 || to >= items.length || from === to) { return; }
    var moved = items.splice(from, 1)[0];
    items.splice(to, 0, moved);
    write(textarea, items);
    render(textarea, list);
    var buttons = list.querySelectorAll("li")[to].querySelectorAll("button");
    if (buttons.length) { (to < from ? buttons[0] : buttons[1]).focus(); }
  }

  document.querySelectorAll("textarea[data-reorder]").forEach(function (textarea) {
    var toggle = document.createElement("button");
    toggle.type = "button";
    toggle.className = "reorder-toggle";
    toggle.textContent = "Reorder lines";
    toggle.setAttribute("aria-expanded", "false");

    var list = document.createElement("ol");
    list.className = "reorder-list";
    list.hidden = true;

    toggle.addEventListener("click", function () {
      var open = list.hidden;
      list.hidden = !open;
      toggle.setAttribute("aria-expanded", String(open));
      if (open) { render(textarea, list); }
    });
    textarea.addEventListener("input", function () { if (!list.hidden) { render(textarea, list); } });
    textarea.after(toggle, list);
  });
})();

// ADR 0016, UX-13: tag picker as a dropdown multi-select. The filter hides tags that do not match; the chips in
// the closed box follow the ticked checkboxes; Escape or a click outside closes it. The checkboxes themselves (and
// opening and closing) work without this script.
(function () {
  "use strict";

  function chips(picker) {
    var value = picker.querySelector("[data-multiselect-value]");
    if (!value) { return; }
    var names = [];
    picker.querySelectorAll(".tag-options input[type=checkbox]:checked").forEach(function (box) {
      names.push(box.parentElement.textContent.trim());
    });
    value.textContent = "";
    if (names.length === 0) {
      var empty = document.createElement("span");
      empty.className = "placeholder";
      empty.textContent = "Choose tags";
      value.appendChild(empty);
      return;
    }
    names.forEach(function (name) {
      var chip = document.createElement("span");
      chip.className = "chip";
      chip.textContent = name; // text, never HTML (SEC-22)
      value.appendChild(chip);
    });
  }

  function init() {
    var filter = document.querySelector("[data-tag-filter]");
    if (!filter) { return; }
    var picker = filter.closest(".tag-picker");
    var menu = picker.querySelector("[data-multiselect]");
    filter.hidden = false;
    filter.addEventListener("input", function () {
      var term = filter.value.trim().toLowerCase();
      picker.querySelectorAll("[data-tag-group]").forEach(function (group) {
        var any = false;
        group.querySelectorAll(".tag-options label").forEach(function (label) {
          var match = term.length === 0 || label.textContent.toLowerCase().indexOf(term) >= 0;
          label.hidden = !match;
          any = any || match;
        });
        group.hidden = !any;
      });
    });
    filter.addEventListener("keydown", function (event) {
      if (event.key === "Enter") { event.preventDefault(); } // never submit the recipe from the filter
    });
    picker.addEventListener("change", function () { chips(picker); });
    if (menu) {
      menu.addEventListener("toggle", function () { if (menu.open) { filter.focus(); } });
      document.addEventListener("click", function (event) {
        if (menu.open && !menu.contains(event.target)) { menu.open = false; }
      });
      menu.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && menu.open) {
          menu.open = false;
          menu.querySelector("summary").focus();
        }
      });
    }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
