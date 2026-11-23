// FR-EXP-04, BR-97, ADR 0033: Copy and Download act on exactly the text in the preview. Nothing is sent to a server.
// No inline handlers (SEC-23).
(function () {
  "use strict";
  function status(text) {
    var el = document.querySelector(".ai-status");
    if (el) { el.textContent = text; }
  }
  document.querySelectorAll("button.ai-copy").forEach(function (b) {
    b.addEventListener("click", function () {
      var box = document.getElementById(b.getAttribute("data-target"));
      if (!box) { return; }
      var done = function () { status("Copied."); };
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(box.value).then(done, function () { box.select(); status("Press Ctrl+C or Cmd+C to copy."); });
      } else {
        box.select();
        status("Press Ctrl+C or Cmd+C to copy.");
      }
    });
  });
  document.querySelectorAll("button.ai-download").forEach(function (b) {
    b.addEventListener("click", function () {
      var box = document.getElementById(b.getAttribute("data-target"));
      if (!box) { return; }
      var url = URL.createObjectURL(new Blob([box.value], { type: "text/markdown;charset=utf-8" }));
      var a = document.createElement("a");
      a.href = url;
      a.download = b.getAttribute("data-filename") || "recipe-for-ai.md";
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
      status("Downloaded.");
    });
  });
})();
