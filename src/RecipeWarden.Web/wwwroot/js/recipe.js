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

  function init(root) {
    root.querySelectorAll(".no-js-only").forEach(function (el) { el.hidden = true; });
    root.querySelectorAll("button.save-default").forEach(function (button) {
      button.hidden = false;
      button.addEventListener("click", function () { save(button); });
    });
  }

  document.addEventListener("htmx:load", function (event) { init(event.detail.elt); });
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", function () { init(document); });
  } else {
    init(document);
  }
})();
