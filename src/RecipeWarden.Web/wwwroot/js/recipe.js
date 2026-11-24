// FR-UI-02, ADR 0014: recipe toolbar enhancements. The toolbar works without this script (plain GET forms);
// with it, the units list submits on change and "Make this my default" saves the current units and scale.
// No inline handlers (SEC-23). Runs again for content htmx swaps in.
(function () {
  "use strict";

  function save(button) {
    var units = button.getAttribute("data-units") || "as-written";
    var scale = button.getAttribute("data-scale") || "1";
    // SEC-31: an anonymous reader's preference is a client-side cookie; GET never changes server state.
    document.cookie = "rw-units=" + encodeURIComponent(units) + "; Path=/; Max-Age=31536000; SameSite=Lax; Secure";
    document.cookie = "rw-scale=" + encodeURIComponent(scale) + "; Path=/; Max-Age=31536000; SameSite=Lax; Secure";
    var done = function () { button.textContent = "Saved as your default"; };
    if (button.getAttribute("data-signed-in") !== "true") { done(); return; }
    var meta = document.querySelector('meta[name="rw-csrf"]');
    var body = new URLSearchParams();
    body.set("units", units);
    body.set("scale", scale);
    fetch("/account/preferences", {
      method: "POST",
      credentials: "same-origin",
      headers: { "RequestVerificationToken": meta ? meta.getAttribute("content") : "" },
      body: body
    }).then(done, done); // the cookie still holds the choice
  }

  // UX-13: the server always sends "More scaling options" closed; keep it open across htmx's in-place updates.
  var keepMoreOpen = false;
  document.addEventListener("htmx:beforeSwap", function () {
    var more = document.querySelector("#recipe-body .scale-more");
    keepMoreOpen = !!(more && more.open);
  });

  function init(root) {
    if (keepMoreOpen) {
      var more = root.querySelector && root.querySelector(".scale-more");
      if (more) { more.open = true; }
      if (root.classList && root.classList.contains("scale-more")) { root.open = true; }
    }
    root.querySelectorAll(".no-js-only").forEach(function (el) { el.hidden = true; });
    root.querySelectorAll("button.save-default").forEach(function (button) {
      button.hidden = false;
      button.addEventListener("click", function () { save(button); });
    });
  }

  // The units list submits its form on change. One listener on the document (not on the list) still works for a
  // toolbar htmx has just swapped in but not yet processed; htmx then turns the submit into an in-place update, or
  // the browser loads the page normally. Either way the URL carries the choice (ADR 0014).
  document.addEventListener("change", function (event) {
    var form = event.target && event.target.form;
    if (!form || !form.hasAttribute("data-submit-on-change")) { return; }
    if (typeof form.requestSubmit === "function") { form.requestSubmit(); } else { form.submit(); }
  });

  // BR-122: an editor's "Show me" weighs only the ticked lines (the ticks belong to the Save form).
  document.addEventListener("submit", function (event) {
    var form = event.target;
    if (!form || !form.hasAttribute || !form.hasAttribute("data-show-me")) { return; }
    var field = form.querySelector("[data-show-lines]");
    if (!field) { return; }
    var picked = [];
    document.querySelectorAll('[data-suggestion-line="' + form.getAttribute("data-show-me") + '"]').forEach(function (box) {
      if (box.checked) { picked.push(box.value); }
    });
    field.value = picked.join(",");
  });

  document.addEventListener("htmx:load", function (event) { init(event.detail.elt); });
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { init(document); });
  } else {
    init(document);
  }
})();
