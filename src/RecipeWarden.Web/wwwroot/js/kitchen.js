// BR-120, /manage/kitchen: show only the fields of the chosen tool type. The server renders the right fields for the
// posted type and ignores the others (KitchenRules.OnlyItsFields), so the form still works without this script.
(function () {
  "use strict";
  document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("[data-kitchen-form]").forEach(function (form) {
      var select = form.querySelector("[data-kitchen-type]");
      if (!select) { return; }
      var update = function () {
        var o = select.options[select.selectedIndex].dataset;
        var show = { size: o.size === "True", units: o.units === "True", fill: o.fill === "True" };
        form.querySelectorAll("[data-kitchen-part]").forEach(function (el) {
          el.hidden = !show[el.getAttribute("data-kitchen-part")];
        });
        var label = form.querySelector("[data-kitchen-size-label]");
        if (label) { label.textContent = o.sizeLabel; }
        var hint = form.querySelector("[data-kitchen-size-hint]");
        if (hint) { hint.textContent = o.sizeHint; }
        var input = form.querySelector("[name='Input.Size']");
        if (input) { input.placeholder = o.sizePlaceholder; }
      };
      select.addEventListener("change", update);
      update();
    });
  });
})();
