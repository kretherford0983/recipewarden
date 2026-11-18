// SEC-30: htmx requests carry the antiforgery token in a header.
(function () {
  "use strict";
  document.addEventListener("htmx:configRequest", function (event) {
    var meta = document.querySelector('meta[name="rw-csrf"]');
    if (meta) {
      event.detail.headers["RequestVerificationToken"] = meta.getAttribute("content");
    }
  });
})();
