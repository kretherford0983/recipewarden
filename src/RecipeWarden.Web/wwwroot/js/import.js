// UX-10: show progress while an import runs in the request (ADR 0024), and catch files that are too large before
// uploading them. No inline script (SEC-23); the server checks everything again.
(function () {
    'use strict';
    document.querySelectorAll('form[data-progress]').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            var input = form.querySelector('input[type=file][data-max-bytes]');
            if (input) {
                var max = Number(input.getAttribute('data-max-bytes'));
                var big = Array.prototype.filter.call(input.files, function (f) { return f.size > max; });
                if (big.length > 0) {
                    e.preventDefault();
                    var warn = form.querySelector('.progress');
                    if (warn) {
                        warn.textContent = big.map(function (f) { return f.name; }).join(', ') + ': documents must be 20 MB or smaller';
                        warn.hidden = false;
                    }
                    return;
                }
            }
            var status = form.querySelector('.progress');
            if (status) {
                status.textContent = form.getAttribute('data-progress');
                status.hidden = false;
            }
            var button = form.querySelector('button[type=submit]');
            if (button) {
                button.disabled = true;
            }
        });
    });
}());
