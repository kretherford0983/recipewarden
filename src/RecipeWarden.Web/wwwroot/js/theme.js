// FR-UI-01, UX-03: theme toggle. The server already rendered <html data-theme> from the cookie or
// profile, so the first paint is correct; this script only handles changes. No inline handlers (SEC-23).
(function () {
  "use strict";
  var order = ["system", "light", "dark"];
  var cookieName = "rw-theme";

  function apply(value) {
    document.documentElement.setAttribute("data-theme", value);
    var button = document.getElementById("theme-toggle");
    if (!button) { return; }
    button.setAttribute("aria-label", "Theme: " + value + ". Change theme");
    var label = button.querySelector(".theme-toggle__label");
    if (label) { label.textContent = value; }
  }

  function save(value, signedIn) {
    // SEC-31: anonymous preference is a client-side cookie; GET never changes server state.
    document.cookie = cookieName + "=" + value + "; Path=/; Max-Age=31536000; SameSite=Lax; Secure";
    if (!signedIn) { return; }
    var meta = document.querySelector('meta[name="rw-csrf"]');
    var body = new URLSearchParams();
    body.set("theme", value);
    fetch("/account/theme", {
      method: "POST",
      credentials: "same-origin",
      headers: { "RequestVerificationToken": meta ? meta.getAttribute("content") : "" },
      body: body
    }).catch(function () { /* the cookie still holds the choice */ });
  }

  function init() {
    var button = document.getElementById("theme-toggle");
    if (!button) { return; }
    button.addEventListener("click", function () {
      var current = document.documentElement.getAttribute("data-theme") || "system";
      var next = order[(order.indexOf(current) + 1) % order.length];
      apply(next);
      save(next, button.getAttribute("data-signed-in") === "true");
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
