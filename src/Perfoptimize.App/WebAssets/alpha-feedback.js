"use strict";

// Alpha support helper: keep the export tied to the exact analysis currently
// displayed in this browser tab. The downloaded JSON is produced server-side
// from the saved session, so the UI never reconstructs or embellishes results.
(function () {
  const host = document.querySelector(".topbar-right");
  if (!host) return;

  const button = document.createElement("button");
  button.id = "export-alpha-report";
  button.type = "button";
  button.textContent = "Exporter le rapport";
  button.title = "Télécharger un rapport technique anonymisé de la dernière analyse";
  button.style.cssText = [
    "border:1px solid rgba(255,255,255,.18)",
    "background:transparent",
    "color:inherit",
    "border-radius:8px",
    "padding:7px 10px",
    "font:inherit",
    "cursor:pointer",
  ].join(";");

  const quit = document.getElementById("quit");
  host.insertBefore(button, quit || null);

  function currentSessionId() {
    return state && state.lastReport && state.lastReport.sessionId != null
      ? state.lastReport.sessionId
      : null;
  }

  function sync() {
    const ready = currentSessionId() != null;
    button.disabled = !ready;
    button.style.opacity = ready ? "1" : ".45";
    button.style.cursor = ready ? "pointer" : "not-allowed";
  }

  button.addEventListener("click", () => {
    const sessionId = currentSessionId();
    if (sessionId == null) return;
    window.location.href = `/api/export-report?sessionId=${encodeURIComponent(sessionId)}`;
  });

  const baseRender = render;
  render = function () {
    baseRender();
    sync();
  };

  sync();
})();
