// FR-VIEW-02, AC-23, ADR 0031: cook mode. Ticks live in this browser's localStorage only (nothing is sent to the
// server); the screen stays awake with the Wake Lock API where supported; timers count down from the step text.
// Text is set with textContent only (SEC-22); no inline handlers (SEC-23).
(function () {
  "use strict";
  var root = document.querySelector(".cook-mode");
  if (!root) { return; }
  var key = "rw-cook:" + (root.getAttribute("data-cook-key") || "");

  function load() {
    try { return JSON.parse(window.localStorage.getItem(key) || "{}") || {}; } catch (e) { return {}; }
  }
  function store(state) {
    try { window.localStorage.setItem(key, JSON.stringify(state)); } catch (e) { /* private mode: ticks last for this page */ }
  }

  var state = load();
  root.querySelectorAll("input.cook-tick").forEach(function (box) {
    var id = box.getAttribute("data-tick");
    box.checked = state[id] === true;
    box.closest("li").classList.toggle("done", box.checked);
    box.addEventListener("change", function () {
      state[id] = box.checked;
      if (!box.checked) { delete state[id]; }
      box.closest("li").classList.toggle("done", box.checked);
      store(state);
    });
  });

  var clear = root.querySelector("button.cook-clear");
  if (clear) {
    clear.addEventListener("click", function () {
      state = {};
      try { window.localStorage.removeItem(key); } catch (e) { /* ignore */ }
      root.querySelectorAll("input.cook-tick").forEach(function (box) {
        box.checked = false;
        box.closest("li").classList.remove("done");
      });
    });
  }

  // Wake Lock: re-acquired when the page becomes visible again (the browser releases it when hidden).
  var status = root.querySelector(".wake-status");
  var lock = null;
  function wake() {
    if (!("wakeLock" in navigator)) {
      if (status) { status.textContent = "This browser cannot keep the screen on."; }
      return;
    }
    navigator.wakeLock.request("screen").then(function (l) {
      lock = l;
      if (status) { status.textContent = "Screen stays on while cooking."; }
      l.addEventListener("release", function () { lock = null; });
    }, function () {
      if (status) { status.textContent = "The screen may turn off."; }
    });
  }
  document.addEventListener("visibilitychange", function () {
    if (document.visibilityState === "visible" && lock === null) { wake(); }
  });
  wake();

  // Timers.
  var area = root.querySelector(".cook-timers");
  function format(s) {
    var h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
    var mm = h > 0 && m < 10 ? "0" + m : String(m);
    return (h > 0 ? h + ":" : "") + mm + ":" + (sec < 10 ? "0" : "") + sec;
  }
  function start(label, seconds) {
    var box = document.createElement("div");
    box.className = "timer";
    var name = document.createElement("span");
    name.textContent = label + ": ";
    var left = document.createElement("strong");
    var stop = document.createElement("button");
    stop.type = "button";
    stop.textContent = "Stop";
    box.appendChild(name);
    box.appendChild(left);
    box.appendChild(document.createTextNode(" "));
    box.appendChild(stop);
    area.appendChild(box);
    var end = Date.now() + seconds * 1000;
    function tick() {
      var remaining = Math.max(0, Math.round((end - Date.now()) / 1000));
      left.textContent = format(remaining);
      if (remaining === 0) {
        window.clearInterval(handle);
        box.classList.add("timer-done");
        left.textContent = "done";
        if (navigator.vibrate) { navigator.vibrate([300, 150, 300]); }
      }
    }
    var handle = window.setInterval(tick, 1000);
    tick();
    stop.addEventListener("click", function () { window.clearInterval(handle); box.remove(); });
  }
  root.querySelectorAll("button.cook-timer").forEach(function (b) {
    b.addEventListener("click", function () {
      start(b.textContent.replace("⏱", "").trim(), parseInt(b.getAttribute("data-seconds"), 10) || 0);
    });
  });
})();
