// FR-EXP-01: the Print button on the print layout. No inline handlers (SEC-23).
(function () {
  "use strict";
  document.querySelectorAll("button.print-now").forEach(function (b) {
    b.addEventListener("click", function () { window.print(); });
  });
})();
