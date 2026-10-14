// DLDS shared client-side behavior: theme toggle, sidebar, toasts, live polling helper.
(function () {
  "use strict";

  // ---------- Theme ----------
  const THEME_KEY = "dlds-theme";

  function applyTheme(theme) {
    document.documentElement.setAttribute("data-theme", theme);
    try { localStorage.setItem(THEME_KEY, theme); } catch (e) { /* storage unavailable */ }
  }

  function initTheme() {
    let saved = null;
    try { saved = localStorage.getItem(THEME_KEY); } catch (e) { /* ignore */ }
    applyTheme(saved || document.documentElement.getAttribute("data-theme") || "dark");

    document.querySelectorAll("[data-theme-toggle]").forEach(btn => {
      btn.addEventListener("click", () => {
        const current = document.documentElement.getAttribute("data-theme");
        applyTheme(current === "dark" ? "light" : "dark");
      });
    });
  }

  // ---------- Sidebar ----------
  function initSidebar() {
    const sidebar = document.querySelector(".sidebar");
    if (!sidebar) return;

    document.querySelectorAll("[data-sidebar-collapse]").forEach(btn => {
      btn.addEventListener("click", () => sidebar.classList.toggle("collapsed"));
    });

    document.querySelectorAll("[data-sidebar-toggle-mobile]").forEach(btn => {
      btn.addEventListener("click", () => sidebar.classList.toggle("mobile-open"));
    });
  }

  // ---------- Toasts ----------
  function ensureToastContainer() {
    let el = document.getElementById("toast-container");
    if (!el) {
      el = document.createElement("div");
      el.id = "toast-container";
      document.body.appendChild(el);
    }
    return el;
  }

  window.dldsToast = function (message, type) {
    type = type || "info";
    const container = ensureToastContainer();
    const toast = document.createElement("div");
    toast.className = "dlds-toast " + type;
    toast.textContent = message;
    container.appendChild(toast);
    setTimeout(() => {
      toast.style.transition = "opacity 250ms ease";
      toast.style.opacity = "0";
      setTimeout(() => toast.remove(), 260);
    }, 4200);
  };

  function initServerToasts() {
    document.querySelectorAll("[data-server-toast]").forEach(el => {
      const message = el.getAttribute("data-server-toast");
      const type = el.getAttribute("data-toast-type") || "success";
      if (message) window.dldsToast(message, type);
    });
  }

  // ---------- Simple client-side table filter (File Events search box, no server round-trip needed for quick filter) ----------
  function initQuickFilter() {
    document.querySelectorAll("[data-quick-filter]").forEach(input => {
      const targetSelector = input.getAttribute("data-quick-filter");
      input.addEventListener("input", () => {
        const term = input.value.trim().toLowerCase();
        document.querySelectorAll(targetSelector).forEach(row => {
          const text = row.textContent.toLowerCase();
          row.style.display = text.includes(term) ? "" : "none";
        });
      });
    });
  }

  // ---------- Live monitor polling ----------
  // Used by Views/Dashboard/Index.cshtml (mini live feed) — polls the JSON endpoint
  // rather than reloading the page. Falls back gracefully if the endpoint fails.
  window.initDldsLivePoll = function (url, renderFn, intervalMs) {
    intervalMs = intervalMs || 5000;
    let lastSeenId = null;

    async function tick() {
      try {
        const res = await fetch(url, { headers: { "X-Requested-With": "XMLHttpRequest" } });
        if (!res.ok) return;
        const data = await res.json();
        renderFn(data, lastSeenId);
        if (data.length) lastSeenId = data[0].id;
      } catch (e) {
        // Network hiccup — silently retry on the next interval rather than spamming the user.
      }
    }

    tick();
    return setInterval(tick, intervalMs);
  };

  document.addEventListener("DOMContentLoaded", function () {
    initTheme();
    initSidebar();
    initServerToasts();
    initQuickFilter();
  });
})();
