"use strict";

/* ============================================================================
   Perfoptimize cockpit — a vanilla single-page UI over the local JSON API.
   One view is mounted at a time into #content; data comes from the same
   endpoints the previous UI used. Where the backend cannot supply a value
   (no PresentMon, no analysis run yet), the view shows an honest empty state
   rather than a fabricated number.
   ========================================================================== */

const C = {
  accent: "oklch(0.82 0.13 205)",
  accentSoft: "oklch(0.86 0.1 205)",
  good: "oklch(0.82 0.18 150)",
  warn: "oklch(0.80 0.15 72)",
  bad: "oklch(0.64 0.21 27)",
  faint: "#5e6a7b",
};

const NAV = [
  ["overview", "Vue d'ensemble"],
  ["analyse", "Analyse"],
  ["moniteur", "Moniteur"],
  ["library", "Jeux installés"],
  ["hardware", "Matériel"],
  ["system", "Système"],
  ["incidents", "Incidents"],
  ["reseau", "Réseau"],
  ["tools", "Outils"],
];

const state = {
  view: "overview",
  monitorPhase: "idle", // idle | running | done
  seconds: 30, goal: "balanced", targetFps: 60,
  applyPhase: "idle", // idle | preview | applied | restored
  applyResult: null, applyMsg: "",
  openConfig: null,
  // data caches (null = not loaded yet)
  games: null, hardware: null, tools: null, system: null,
  library: null, libConfigs: {}, lastReport: null, incidents: null, live: null, network: null,
  alerts: [], settingsGuide: null,
};

let countdownTimer = null;
let chartResizeBound = false;
let liveTimer = null;
let liveBusy = false;
let liveFirstPoll = true;
const seenAlertIds = new Set();

// ---- helpers ---------------------------------------------------------------
const $ = (id) => document.getElementById(id);
const esc = (s) => String(s == null ? "" : s).replace(/[&<>"']/g, (c) =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const num = (v) => (v == null || Number.isNaN(v)) ? null : v;
const pct = (v) => num(v) == null ? "n/a" : `${Math.round(v)}`;
const gib = (b) => (b == null) ? null : `${(b / 1073741824).toFixed(1)} Gio`;
const mib = (b) => (b == null) ? "—" : (b >= 1073741824 ? `${(b / 1073741824).toFixed(1)} Gio` : `${Math.round(b / 1048576)} Mio`);

async function getJson(url) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${url} → ${r.status}`);
  return r.json();
}
async function postJson(url, body) {
  const r = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: body ? JSON.stringify(body) : null,
  });
  if (!r.ok) throw new Error(`${url} → ${r.status}`);
  return r.json();
}

function usageColor(v) { if (v == null) return C.faint; if (v >= 90) return C.bad; if (v >= 75) return C.warn; return C.good; }
function vramColor(v) { if (v == null) return C.faint; if (v >= 88) return C.bad; if (v >= 70) return C.warn; return C.good; }
function tempColor(v) { if (v == null) return C.faint; if (v >= 85) return C.bad; if (v >= 75) return C.warn; return C.good; }
function cpuTempColor(v) { if (v == null) return C.faint; if (v >= 90) return C.bad; if (v >= 80) return C.warn; return C.good; }

// ---- small view builders ---------------------------------------------------
function ring(v, c) {
  const f = (Math.max(0, Math.min(100, v || 0)) / 100 * 270).toFixed(1);
  return `position:absolute;inset:0;border-radius:50%;background:conic-gradient(from 225deg, ${c} ${f}deg, #0d1017 ${f}deg, #0d1017 270deg, transparent 270deg)`;
}
function gauge(g, big) {
  const size = big ? 132 : 108, discInset = big ? 11 : 9, valSize = big ? 30 : 22, unitSize = big ? 15 : 12;
  const pad = big ? 20 : 16, flex = big ? "1 1 168px;max-width:360px" : "1 1 150px;max-width:300px";
  const value = g.value == null ? "n/a" : g.value;
  const valColor = g.value == null ? C.faint : g.color;
  return `<div class="card gauge" style="flex:${flex};padding:${pad}px">
    <div class="gauge-ring" style="width:${size}px;height:${size}px">
      <div style="${ring(g.ringValue, g.color)}"></div>
      <div class="disc" style="inset:${discInset}px"></div>
      <div class="val" style="font-size:${valSize}px;color:${valColor}">${value}<span style="font-size:${unitSize}px;color:#8b97a8">${g.unit || ""}</span></div>
    </div>
    <div class="gauge-label"><div class="l">${esc(g.label)}</div><div class="s">${esc(g.sub || "")}</div></div>
  </div>`;
}
function badge(label, color) {
  return `<span style="display:inline-flex;align-items:center;padding:6px 13px;border-radius:9px;font-size:14px;font-weight:700;letter-spacing:.04em;text-transform:uppercase;background:color-mix(in srgb, ${color} 16%, transparent);color:${color}">${esc(label)}</span>`;
}
function tag(label, color) {
  return `<span style="display:inline-flex;align-items:center;padding:3px 9px;border-radius:6px;font-size:10px;font-weight:700;letter-spacing:.08em;white-space:nowrap;background:color-mix(in srgb, ${color} 18%, transparent);color:${color}">${esc(label)}</span>`;
}
function cardHead(label, color) {
  const c = color || C.accent;
  return `<div class="card-head"><span class="card-head-square" style="background:${c};box-shadow:0 0 8px ${c}"></span><span class="card-head-title">${esc(label)}</span></div>`;
}
function head(kicker, title, right) {
  return `<div class="view-head"><div><div class="kicker">${esc(kicker)}</div><h1 class="title">${esc(title)}</h1></div>${right || ""}</div>`;
}

const BOTTLENECK = {
  Gpu: ["Limite GPU", C.accent], Cpu: ["Limite CPU", C.warn],
  Vram: ["Limite VRAM", C.warn], Ram: ["Limite RAM", C.warn],
  Thermal: ["Throttling thermique", C.bad], External: ["Limite externe", C.bad],
  Hardware: ["Limite matérielle", C.bad], None: ["Aucun goulot", C.good],
  Unknown: ["Indéterminé", C.faint],
};
function bottleneckLabel(type) { return BOTTLENECK[type] || [type, C.accent]; }

// ---- hardware summaries ----------------------------------------------------
function primaryGpu(h) { return h ? (h.gpus.find((g) => g.name === h.primaryGpu) || h.gpus[0] || null) : null; }
function primaryDisplay(h) { return h ? (h.displays.find((d) => d.isPrimary) || h.displays[0] || null) : null; }
function resolutionLabel(h) {
  const d = primaryDisplay(h);
  if (!d) return null;
  return `${d.heightPixels}p${d.refreshRateHz ? " · " + d.refreshRateHz + " Hz" : ""}`;
}

// ============================================================================
//  VIEW: 01 — Vue d'ensemble
// ============================================================================
function viewOverview() {
  const g = state.games && state.games.running;
  const h = state.hardware;
  const r = state.lastReport;
  const gpu = primaryGpu(h);

  const detected = !!g;
  const heroTag = detected
    ? `<span class="dot dot-good pulse"></span><span style="font-family:var(--mono);font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:${C.good}">jeu détecté · en cours</span>`
    : `<span class="dot" style="background:${C.faint}"></span><span style="font-family:var(--mono);font-size:11px;letter-spacing:.14em;text-transform:uppercase;color:${C.faint}">aucun jeu détecté</span>`;
  const heroName = detected ? esc(g.name) : "Aucun jeu en cours";
  const chip = (t) => `<span style="display:inline-flex;align-items:center;padding:5px 11px;border-radius:999px;background:#0e131b;border:1px solid #232b38;font-size:12px;color:#8b97a8;font-family:var(--mono)">${t}</span>`;
  const chips = [];
  if (detected) chips.push(`<span style="display:inline-flex;align-items:center;padding:5px 11px;border-radius:999px;background:oklch(0.82 0.13 205 / .14);border:1px solid oklch(0.82 0.13 205 / .35);font-size:12px;color:${C.accentSoft};font-family:var(--mono)">${g.isKnown ? "profil détaillé" : "détecté auto"}</span>`);
  if (detected) chips.push(chip(`pid ${g.processId}`));
  if (gpu) chips.push(chip(`${esc(gpu.name)}${resolutionLabel(h) ? " · " + esc(resolutionLabel(h)) : ""}`));

  const hero = `<div style="position:relative;overflow:hidden;background:linear-gradient(135deg,#141a24,#10141d);border:1px solid #232b38;border-radius:18px;padding:28px 30px">
    <div style="position:absolute;top:-50px;right:-40px;width:280px;height:280px;border-radius:50%;background:radial-gradient(circle, oklch(0.82 0.13 205 / .14), transparent 70%);pointer-events:none"></div>
    <div style="position:relative">
      <div style="display:flex;align-items:center;gap:10px;margin-bottom:12px">${heroTag}</div>
      <div style="font-size:40px;font-weight:700;letter-spacing:-.02em;line-height:1.02">${heroName}</div>
      <div style="display:flex;align-items:center;gap:8px;margin-top:14px;flex-wrap:wrap">${chips.join("")}</div>
    </div>
    <div style="position:relative;display:flex;align-items:center;justify-content:space-between;gap:18px 32px;flex-wrap:wrap;margin-top:24px;padding-top:22px;border-top:1px solid #1b222d">
      <div style="display:flex;gap:40px">
        <div><div style="font-family:var(--mono);font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:#5e6a7b">Objectif</div><div style="font-size:18px;font-weight:600;margin-top:4px">${goalLabel(state.goal)}</div></div>
        <div><div style="font-family:var(--mono);font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:#5e6a7b">FPS visés</div><div style="font-family:var(--mono);font-size:18px;font-weight:600;margin-top:4px">${state.targetFps}</div></div>
      </div>
      <button class="btn-primary" data-action="goto-analyse">Lancer une analyse →</button>
    </div>
  </div>`;

  // gauges from the last analysis, or an honest empty state
  let gauges;
  if (r) {
    gauges = `<div style="display:flex;flex-wrap:wrap;gap:16px">${[
      { label: "GPU", value: pct(r.gpu.avg), ringValue: r.gpu.avg, unit: "%", sub: gpuSub(r), color: usageColor(r.gpu.avg) },
      { label: "CPU", value: pct(r.cpu.avg), ringValue: r.cpu.avg, unit: "%", sub: r.cpu.busiestCore != null ? `cœur max ${pct(r.cpu.busiestCore)} %` : "", color: usageColor(r.cpu.avg) },
      { label: "VRAM", value: pct(r.memory.vram), ringValue: r.memory.vram, unit: "%", sub: r.memory.ram != null ? `RAM ${pct(r.memory.ram)} %` : "", color: vramColor(r.memory.vram) },
      { label: "Temp GPU", value: r.gpu.tempC != null ? Math.round(r.gpu.tempC) : null, ringValue: r.gpu.tempC, unit: "°", sub: r.gpu.tempC != null ? "pic" : "capteur absent", color: tempColor(r.gpu.tempC) },
    ].map((x) => gauge(x, true)).join("")}</div>`;
  } else {
    gauges = `<div class="empty">Aucune mesure encore. Lance une analyse pour instrumenter GPU, CPU, VRAM et température en temps réel.
      <div style="margin-top:12px"><button class="btn-primary" data-action="goto-analyse">Lancer une analyse →</button></div></div>`;
  }

  // hardware summary
  const hwRows = hwSummaryRows(h);
  const hwCard = `<div class="card">
    <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:18px">
      ${cardHead("Matériel")}<a href="#" data-action="goto-hardware" style="font-family:var(--mono);font-size:11px">détails →</a>
    </div>
    ${hwRows}
  </div>`;

  // recent diagnostic
  let diagCard;
  if (r) {
    const [lbl, col] = bottleneckLabel(r.bottleneck.type);
    const confirmed = r.bottleneck.confidence !== "Suspected";
    const tline = r.target && r.target.measuredAverageFps != null
      ? `<div style="background:#0e131b;border:1px solid #1b222d;border-radius:12px;padding:14px 16px;margin-top:4px;font-family:var(--mono);font-size:12px;color:#8b97a8">${r.target.measuredAverageFps.toFixed(0)} FPS mesurés · objectif ${r.target.targetFps}${r.target.requiredGainPercent > 0 ? ` · +${Math.round(r.target.requiredGainPercent)} % nécessaires` : " · atteint"}</div>`
      : `<div style="font-family:var(--mono);font-size:11px;color:#5e6a7b;margin-top:4px">Installe PresentMon pour mesurer l'écart FPS réel.</div>`;
    diagCard = `<div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:18px">
        ${cardHead("Diagnostic récent")}<a href="#" data-action="goto-analyse" style="font-family:var(--mono);font-size:11px">analyse →</a>
      </div>
      <div style="display:flex;align-items:center;gap:12px;margin-bottom:16px">
        <span style="display:inline-flex;align-items:center;padding:5px 12px;border-radius:8px;font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;background:color-mix(in srgb, ${col} 16%, transparent);color:${col}">${esc(lbl)}</span>
        <span style="display:inline-flex;align-items:center;gap:6px;font-family:var(--mono);font-size:11px;color:${confirmed ? C.good : C.warn}"><span style="width:6px;height:6px;border-radius:50%;background:${confirmed ? C.good : C.warn}"></span>${confirmed ? "confirmé" : "suspecté"}</span>
      </div>
      <p style="margin:0 0 16px;font-size:13.5px;line-height:1.6;color:#a9b3c1">${esc(r.bottleneck.explanation)}</p>
      ${tline}
    </div>`;
  } else {
    diagCard = `<div class="card">${cardHead("Diagnostic récent")}<div class="empty" style="margin-top:2px">Aucune analyse récente. Lance une mesure pour obtenir un diagnostic CPU/GPU.</div></div>`;
  }

  const right = `<div style="font-family:var(--mono);font-size:11px;color:#5e6a7b;text-align:right;line-height:1.6">état de session<br><span style="color:#8b97a8">${detected ? "jeu actif" : "en veille"} · ${goalLabel(state.goal)} · ${state.targetFps} FPS</span></div>`;
  return `<div class="view">
    ${head("01 — Vue d'ensemble", "Poste de pilotage", right)}
    ${hero}
    ${gauges}
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:20px">${hwCard}${diagCard}</div>
  </div>`;
}

function goalLabel(goal) { return { quality: "Qualité", balanced: "Équilibré", performance: "Performance" }[goal] || goal; }
function gpuSub(r) {
  if (r.gpu.gameShare != null) return `part jeu ${pct(r.gpu.gameShare)} %`;
  return "";
}
function hwSummaryRows(h) {
  if (!h) return `<div class="empty">Analyse du matériel en cours…</div>`;
  const gpu = primaryGpu(h);
  const rows = [
    ["CPU", h.cpu.name || "capteur absent"],
    ["GPU", gpu ? `${gpu.name}${gpu.vramBytes ? " · " + gib(gpu.vramBytes) : ""}` : "capteur absent"],
    ["RAM", gib(h.memory.totalBytes) ? `${gib(h.memory.totalBytes)}${h.memory.speedMtPerSecond ? " · " + h.memory.speedMtPerSecond + " MT/s" : ""}` : "capteur absent"],
    ["Écran", resolutionLabel(h) || "capteur absent"],
  ];
  return `<div style="display:flex;flex-direction:column;gap:12px">${rows.map(([k, v]) =>
    `<div style="display:flex;align-items:baseline;justify-content:space-between;gap:14px;border-bottom:1px solid #1b222d;padding-bottom:11px">
      <span style="font-family:var(--mono);font-size:11px;letter-spacing:.08em;text-transform:uppercase;color:#5e6a7b">${esc(k)}</span>
      <span style="font-size:13.5px;text-align:right;color:#e7ecf3">${esc(v)}</span></div>`).join("")}</div>`;
}

// ============================================================================
//  VIEW: 02 — Analyse
// ============================================================================
function viewAnalyse() {
  const opt = (v, l, sel) => `<option value="${v}"${sel ? " selected" : ""}>${l}</option>`;
  const running = state.monitorPhase === "running";
  const controls = `<div class="card" style="display:flex;align-items:flex-end;gap:18px;flex-wrap:wrap;padding:22px 24px">
    <label style="display:flex;flex-direction:column;gap:7px;font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:#8b97a8;font-weight:600;font-family:var(--ui)">Durée
      <select data-field="seconds" style="background:#0e131b;color:#e7ecf3;border:1px solid #232b38;border-radius:9px;padding:10px 12px;font-size:13px;min-width:110px">${opt(10, "10 s", state.seconds === 10)}${opt(30, "30 s", state.seconds === 30)}${opt(60, "60 s", state.seconds === 60)}</select>
    </label>
    <label style="display:flex;flex-direction:column;gap:7px;font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:#8b97a8;font-weight:600;font-family:var(--ui)">Objectif
      <select data-field="goal" style="background:#0e131b;color:#e7ecf3;border:1px solid #232b38;border-radius:9px;padding:10px 12px;font-size:13px;min-width:140px">${opt("quality", "Qualité", state.goal === "quality")}${opt("balanced", "Équilibré", state.goal === "balanced")}${opt("performance", "Performance", state.goal === "performance")}</select>
    </label>
    <label style="display:flex;flex-direction:column;gap:7px;font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:#8b97a8;font-weight:600;font-family:var(--ui)">FPS visés
      <input type="number" min="30" max="360" step="1" value="${state.targetFps}" data-field="targetFps" style="background:#0e131b;color:#e7ecf3;border:1px solid #232b38;border-radius:9px;padding:10px 12px;font-size:13px;width:100px">
    </label>
    <button class="btn-primary" data-action="run"${running ? " disabled" : ""}>${running ? "Mesure…" : "Lancer l'analyse"}</button>
    <div style="flex:1;min-width:180px;font-size:12.5px;color:#5e6a7b;line-height:1.5">Lance ton jeu, reste dans une scène représentative, puis démarre la mesure.</div>
  </div>`;

  let body = "";
  if (running) {
    body = `<div class="card" style="padding:40px;display:flex;flex-direction:column;align-items:center;gap:16px">
      <div class="spinner"></div>
      <div style="font-size:15px">Mesure en cours — <span id="countdown" style="font-family:var(--mono);color:${C.accent}">${state.seconds}</span> s restantes</div>
      <div style="font-size:12.5px;color:#5e6a7b">Joue normalement, l'attribution suit le processus du jeu.</div>
    </div>`;
  } else if (state.monitorPhase === "done" && state.lastReport) {
    body = renderResults(state.lastReport);
  } else {
    body = `<div class="empty">Prêt à mesurer. Choisis une durée et un objectif, puis lance l'analyse — Perfoptimize capture les performances réelles et diagnostique le goulot d'étranglement.</div>`;
  }
  return `<div class="view">${head("02 — Analyse des performances", "Mesurer, pas deviner")}${controls}${body}${renderSettingsGuide()}</div>`;
}

// Educational block: what each setting does + the DX11/DX12/Vulkan choice.
function renderSettingsGuide() {
  const g = state.settingsGuide;
  if (!g) return `<div class="card">${cardHead("Comprendre les réglages")}<div class="empty" style="margin-top:2px"><span class="spinner-sm"></span> Chargement du guide des réglages…</div></div>`;
  const COST = { Gpu: ["GPU", C.accent], Cpu: ["CPU", C.warn], Vram: ["VRAM", C.warn], Ram: ["RAM", C.warn], Mixed: ["CPU+GPU", "#8b97a8"] };
  const rec = g.renderer;
  const options = rec.options.map((o) => {
    const active = o.id === rec.recommendedId;
    return `<div style="flex:1 1 210px;background:#0e131b;border:1px solid ${active ? C.accent : "#1b222d"};border-radius:12px;padding:15px 16px">
      <div style="display:flex;align-items:center;gap:8px;margin-bottom:6px"><span style="font-weight:600;font-size:14px">${esc(o.name)}</span>${active ? `<span style="font-family:var(--mono);font-size:9px;letter-spacing:.08em;text-transform:uppercase;color:${C.accent};border:1px solid ${C.accent};border-radius:4px;padding:1px 5px">conseillé</span>` : ""}</div>
      <div style="font-size:12.5px;color:#a9b3c1;line-height:1.5;margin-bottom:6px">${esc(o.summary)}</div>
      <div style="font-size:11.5px;color:#5e6a7b;line-height:1.45"><span style="color:#8b97a8">Idéal :</span> ${esc(o.bestWhen)}</div>
    </div>`;
  }).join("");
  const rendererCard = `<div class="card">
    ${cardHead("Quelle API de rendu : DirectX 11, 12 ou Vulkan ?")}
    <div style="background:linear-gradient(120deg,#141a24,#10141d);border:1px solid #232b38;border-left:3px solid ${C.accent};border-radius:12px;padding:14px 16px;font-size:13.5px;color:#c3ccd8;line-height:1.55;margin-bottom:14px">${esc(rec.recommendation)}</div>
    <div style="display:flex;flex-wrap:wrap;gap:12px">${options}</div>
  </div>`;
  const items = g.settings.map((s) => {
    const [cl, cc] = COST[s.cost] || ["—", "#8b97a8"];
    return `<div style="background:#0e131b;border:1px solid #1b222d;border-radius:12px;padding:15px 17px">
      <div style="display:flex;align-items:center;gap:9px;margin-bottom:7px"><span style="font-weight:600;font-size:14px">${esc(s.title)}</span><span style="display:inline-flex;padding:2px 8px;border-radius:5px;font-family:var(--mono);font-size:10px;font-weight:600;color:${cc};background:color-mix(in srgb, ${cc} 16%, transparent)">${cl}</span></div>
      <div style="font-size:13px;color:#c3ccd8;line-height:1.5;margin-bottom:6px">${esc(s.whatItChanges)}</div>
      <div style="font-size:12.5px;color:#8b97a8;line-height:1.5">${esc(s.guidance)}</div>
    </div>`;
  }).join("");
  const glossaryCard = `<div class="card">${cardHead("Ce que change chaque réglage")}
    <p style="margin:0 0 14px;font-size:12.5px;color:#5e6a7b">Les réglages les plus impactants, expliqués simplement — pour ne plus remplir au hasard.</p>
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">${items}</div>
  </div>`;
  return rendererCard + glossaryCard;
}

function renderResults(r) {
  const parts = [];
  if (r.sampleCount < 3) {
    parts.push(`<div class="card" style="border-color:${C.warn}"><p style="margin:0;color:${C.warn};font-size:13.5px">Trop peu d'échantillons collectés (${r.sampleCount}).${r.primeSeconds >= 2 ? ` La première sonde a pris ${r.primeSeconds}s — la télémétrie est lente à démarrer, réessaie.` : ""}</p></div>`);
  }

  // verdict
  const [lbl, col] = bottleneckLabel(r.bottleneck.type);
  const confirmed = r.bottleneck.confidence !== "Suspected";
  const t = r.target || {};
  let targetLine = "";
  if (t.measuredAverageFps != null) {
    const gap = t.requiredGainPercent > 0
      ? `<span style="color:${C.warn}">+${Math.round(t.requiredGainPercent)} % nécessaires</span>`
      : `<span style="color:${C.good}">objectif atteint</span>`;
    let est = "";
    if (t.gainHigh != null) {
      est = Math.abs(t.gainHigh - t.gainLow) < 0.5
        ? ` · gain estimé <span style="font-family:var(--mono)">~+${Math.round(t.gainHigh)} %</span>`
        : ` · gain estimé <span style="font-family:var(--mono)">+${Math.round(t.gainLow)} % à +${Math.round(t.gainHigh)} %</span>`;
    }
    targetLine = `<div style="margin-top:16px;padding-top:16px;border-top:1px solid #1b222d;font-size:13.5px;color:#a9b3c1"><span style="font-family:var(--mono);color:#e7ecf3">${t.measuredAverageFps.toFixed(0)} FPS</span> mesurés · objectif ${t.targetFps} · ${gap}${est}</div>`;
  } else {
    targetLine = `<div style="margin-top:16px;padding-top:16px;border-top:1px solid #1b222d;font-size:13px;color:#5e6a7b">Installe PresentMon (Outils) pour mesurer les vrais FPS et dimensionner le plan.</div>`;
  }
  parts.push(`<div style="background:linear-gradient(120deg,#141a24,#10141d);border:1px solid #232b38;border-left:3px solid ${col};border-radius:16px;padding:24px 26px">
    <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:12px">
      ${badge(lbl, col)}
      <span style="display:inline-flex;align-items:center;gap:7px;font-family:var(--mono);font-size:12px;color:${confirmed ? C.good : C.warn}"><span style="width:7px;height:7px;border-radius:50%;background:${confirmed ? C.good : C.warn};box-shadow:0 0 8px ${confirmed ? C.good : C.warn}"></span>diagnostic ${confirmed ? "confirmé" : "suspecté"}</span>
    </div>
    <p style="margin:0;font-size:15px;line-height:1.6;color:#c3ccd8;max-width:820px">${esc(r.bottleneck.explanation)}</p>
    ${(r.cpu.gameShare != null || r.gpu.gameShare != null) ? `<p style="margin:10px 0 0;font-size:12.5px;color:#8b97a8">Part du jeu : CPU ${pct(r.cpu.gameShare)} % · GPU ${pct(r.gpu.gameShare)} %</p>` : ""}
    ${targetLine}
  </div>`);

  // gauges (compact)
  parts.push(`<div style="display:flex;flex-wrap:wrap;gap:14px">${[
    { label: "CPU moy.", value: pct(r.cpu.avg), ringValue: r.cpu.avg, unit: "%", sub: r.cpu.gameShare != null ? `part jeu ${pct(r.cpu.gameShare)} %` : "", color: usageColor(r.cpu.avg) },
    { label: "Cœur max", value: pct(r.cpu.busiestCore), ringValue: r.cpu.busiestCore, unit: "%", sub: "", color: usageColor(r.cpu.busiestCore) },
    { label: "GPU moy.", value: pct(r.gpu.avg), ringValue: r.gpu.avg, unit: "%", sub: r.gpu.gameShare != null ? `part jeu ${pct(r.gpu.gameShare)} %` : "", color: usageColor(r.gpu.avg) },
    { label: "VRAM pic", value: pct(r.memory.vram), ringValue: r.memory.vram, unit: "%", sub: "", color: vramColor(r.memory.vram) },
    { label: "Temp GPU", value: r.gpu.tempC != null ? Math.round(r.gpu.tempC) : null, ringValue: r.gpu.tempC, unit: "°", sub: r.gpu.tempC != null ? "" : "capteur absent", color: tempColor(r.gpu.tempC) },
  ].map((x) => gauge(x, false)).join("")}</div>`);

  // FPS chart + stats
  const series = r.fps && r.fps.series && r.fps.series.length ? r.fps.series : null;
  if (series) {
    const stat = (n, l, c) => `<div style="flex:1 1 130px;max-width:260px;background:#0e131b;border:1px solid #1b222d;border-radius:12px;padding:15px 17px">
      <div style="font-family:var(--mono);font-size:26px;font-weight:600;color:${c || "#e7ecf3"}">${n}</div>
      <div style="font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:#8b97a8;margin-top:5px">${l}</div></div>`;
    parts.push(`<div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:18px;flex-wrap:wrap;gap:8px">
        ${cardHead("FPS dans le temps")}
        <div style="display:flex;align-items:center;gap:16px;font-family:var(--mono);font-size:11px;color:#8b97a8">
          <span style="display:flex;align-items:center;gap:6px"><span style="width:14px;height:2px;background:${C.accent}"></span>FPS</span>
          <span style="display:flex;align-items:center;gap:6px"><span style="width:14px;height:0;border-top:2px dashed ${C.warn}"></span>cible</span>
          <span style="display:flex;align-items:center;gap:6px"><span style="width:7px;height:7px;border-radius:50%;background:${C.bad}"></span>à-coup</span>
        </div>
      </div>
      <canvas id="fps-chart" style="width:100%;height:190px;display:block"></canvas>
      <div style="display:flex;flex-wrap:wrap;gap:12px;margin-top:20px">
        ${stat(r.fps.averageFps.toFixed(0), "FPS moyens")}
        ${stat(r.fps.onePercentLowFps.toFixed(0), "1 % low", C.warn)}
        ${stat(r.fps.worstFrametimeMs != null ? r.fps.worstFrametimeMs.toFixed(1) : "n/a", "Pire frametime (ms)")}
        ${stat(r.fps.stutterPercent != null ? r.fps.stutterPercent.toFixed(1) + " %" : "n/a", "À-coups")}
      </div>
    </div>`);
  } else {
    parts.push(`<div class="card">${cardHead("FPS dans le temps")}<div class="empty" style="margin-top:2px">Pas de courbe FPS : PresentMon n'est pas disponible sur cette machine (ou aucun jeu ne présentait d'images). Installe-le depuis l'onglet Outils pour mesurer les vrais FPS.</div></div>`);
  }

  // advisories — community "settings to avoid"
  if (r.advisories && r.advisories.length) {
    const kinds = { Crash: ["CRASH", C.bad], Stutter: ["SACCADES", C.bad], PoorValue: ["PEU RENTABLE", C.warn] };
    parts.push(`<div class="card">
      ${cardHead("Réglages à éviter", C.bad)}
      <p style="margin:0 0 16px;font-size:12.5px;color:#5e6a7b">Connus de la communauté — signalés quel que soit le goulot.</p>
      <div style="display:flex;flex-direction:column;gap:12px">${r.advisories.map((a) => {
        const [kl, kc] = kinds[a.kind] || [a.kind, C.warn];
        return `<div style="border-left:3px solid ${kc};padding:2px 0 2px 14px">
          <div style="display:flex;align-items:center;gap:10px;margin-bottom:5px;flex-wrap:wrap"><span style="font-weight:600;font-size:14.5px">${esc(a.setting)}</span>${tag(kl, kc)}</div>
          <div style="font-size:13.5px;color:#a9b3c1;line-height:1.55"><strong style="color:#e7ecf3">${esc(a.recommendation)}.</strong> ${esc(a.detail)}</div></div>`;
      }).join("")}</div>
    </div>`);
  }

  // recommendations
  if (r.recommendations && r.recommendations.length) {
    parts.push(`<div class="card">
      ${cardHead("Recommandations")}
      <p style="margin:0 0 18px;font-size:12.5px;color:#5e6a7b">${r.gainsAreMeasured ? "Gains vérifiés sur ta machine." : "Gains <strong style=\"color:#8b97a8\">théoriques</strong> issus du profil, non encore vérifiés sur ta machine."}</p>
      <div style="display:flex;flex-direction:column;gap:14px">${r.recommendations.map((rec) => {
        const gain = rec.expectedGainPercent || 0;
        const measured = rec.evidence === "LocallyMeasured";
        const barW = Math.min(100, Math.round(gain / 40 * 100));
        return `<div style="display:flex;align-items:center;gap:18px;flex-wrap:wrap">
          <div style="flex:1;min-width:260px">
            <div style="font-weight:600;font-size:14.5px;margin-bottom:3px">${esc(rec.title)}</div>
            <div style="font-size:13px;color:#8b97a8;line-height:1.5">${esc(rec.reason)}</div>
          </div>
          <div style="display:flex;align-items:center;gap:12px;min-width:220px">
            <div style="flex:1;height:7px;background:#0e131b;border-radius:999px;overflow:hidden;border:1px solid #1b222d"><div style="height:100%;width:${barW}%;border-radius:999px;background:${C.accent}"></div></div>
            <span style="font-family:var(--mono);font-size:13px;font-weight:600;color:${C.good};white-space:nowrap">${gain ? (measured ? `~+${Math.round(gain)} %` : `jusqu'à ~+${Math.round(gain)} %`) : "—"}</span>
          </div>
        </div>`;
      }).join("")}</div>
    </div>`);
  }

  for (const note of (r.notes || [])) {
    parts.push(`<div style="font-size:13px;color:#8b97a8;padding:2px 0">${esc(note)}</div>`);
  }

  // apply-to-config — only for a known, tunable game
  const tunable = ["Gpu", "Cpu", "Vram", "Ram", "Thermal"].includes(r.bottleneck.type);
  if (r.game && r.canApply && tunable) {
    parts.push(renderApplyCard());
  }

  return parts.join("");
}

function renderApplyCard() {
  const inner = [];
  inner.push(cardHead("Appliquer aux fichiers de config"));
  inner.push(`<p style="margin:0 0 16px;font-size:12.5px;color:#5e6a7b;max-width:720px">Modifie les réglages liés à un fichier. Prévisualise d'abord — rien n'est écrit sans ta confirmation, et une sauvegarde est faite avant toute modification.</p>`);

  if (state.applyPhase === "idle" || !state.applyResult) {
    inner.push(`<button class="btn" data-action="preview">Prévisualiser les modifications</button>`);
    if (state.applyMsg) inner.push(`<p style="margin:12px 0 0;font-size:13px;color:${C.warn}">${esc(state.applyMsg)}</p>`);
  } else {
    const res = state.applyResult;
    const applied = state.applyPhase === "applied";
    const rows = res.changes.map((c) => {
      const ready = c.status === "Ready";
      const dotColor = ready ? (applied ? C.good : C.accent) : C.faint;
      const change = ready ? `${esc(c.current)} → ${esc(c.proposed)}` : esc(c.note || "menu du jeu");
      return `<div style="display:flex;align-items:center;justify-content:space-between;gap:14px;padding:13px 16px;border-bottom:1px solid #151b24">
        <div style="display:flex;align-items:center;gap:11px"><span style="width:7px;height:7px;border-radius:50%;flex:none;background:${dotColor}"></span><span style="font-family:var(--mono);font-size:13px;color:${ready ? "#e7ecf3" : "#5e6a7b"}">${esc(c.setting)}</span></div>
        <div style="font-family:var(--mono);font-size:13px;color:${ready ? "#c3ccd8" : "#5e6a7b"}">${change}</div></div>`;
    }).join("");
    inner.push(`<div style="background:#0e131b;border:1px solid #1b222d;border-radius:12px;overflow:hidden;margin-bottom:16px">${rows || `<div style="padding:16px;color:#5e6a7b;font-size:13px">Aucune modification applicable.</div>`}</div>`);

    if (applied) inner.push(`<div style="display:flex;align-items:center;gap:10px;color:${C.good};font-size:13.5px;margin-bottom:14px"><span style="width:8px;height:8px;border-radius:50%;background:${C.good}"></span>Appliqué — sauvegarde créée, tu peux restaurer à tout moment.</div>`);
    if (state.applyPhase === "restored") inner.push(`<div style="display:flex;align-items:center;gap:10px;color:#8b97a8;font-size:13.5px;margin-bottom:14px"><span style="width:8px;height:8px;border-radius:50%;background:#8b97a8"></span>${esc(state.applyMsg || "Réglages d'origine restaurés depuis la sauvegarde.")}</div>`);
    if (state.applyMsg && state.applyPhase !== "restored") inner.push(`<div style="color:${C.warn};font-size:13px;margin-bottom:14px">${esc(state.applyMsg)}</div>`);

    const canApply = (state.applyPhase === "preview" || state.applyPhase === "restored") && res.changes.some((c) => c.status === "Ready");
    inner.push(`<div style="display:flex;gap:10px;flex-wrap:wrap">
      ${canApply ? `<button class="btn-primary" data-action="apply">Appliquer (jeu fermé)</button>` : ""}
      <button class="btn" data-action="restore">Restaurer</button>
    </div>
    <p style="margin:12px 0 0;font-size:11.5px;color:#5e6a7b">Ferme le jeu avant d'appliquer : il réécrirait sa config en quittant.</p>`);
  }
  return `<div class="card">${inner.join("")}</div>`;
}

// ============================================================================
//  VIEW: 03 — Jeux installés
// ============================================================================
function viewLibrary() {
  const right = `<button class="btn" data-action="rescan-library">Rescanner</button>`;
  if (state.library === null) {
    return `<div class="view">${head("04 — Jeux installés", "Analyse du disque", right)}<div class="empty"><span class="spinner-sm"></span> Analyse des bibliothèques Steam et Epic…</div></div>`;
  }
  if (!state.library.length) {
    return `<div class="view">${head("04 — Jeux installés", "Analyse du disque", right)}<div class="empty">Aucun jeu installé détecté (Steam ou Epic introuvable sur cette machine).</div></div>`;
  }
  const lead = `<p class="lead">Toutes les bibliothèques Steam de tous les disques + Epic Games. <span style="font-family:var(--mono);color:${C.accentSoft}">${state.library.length}</span> jeux découverts avec leur dossier et leurs fichiers de configuration.</p>`;
  const src = (name) => {
    const epic = /epic/i.test(name);
    return `<span style="display:inline-flex;align-items:center;padding:3px 9px;border-radius:6px;font-family:var(--mono);font-size:10.5px;font-weight:600;background:${epic ? "color-mix(in srgb, oklch(0.80 0.15 72) 16%, transparent)" : "#1b222d"};color:${epic ? "oklch(0.82 0.15 72)" : "#8b97a8"}">${esc(name)}</span>`;
  };
  const cards = state.library.map((g, i) => {
    const open = state.openConfig === i;
    let expanded = "";
    if (open) {
      const cfgs = state.libConfigs[g.installDir];
      if (cfgs === undefined) {
        expanded = `<div style="padding:4px 20px 20px"><div class="empty"><span class="spinner-sm"></span> Lecture des fichiers de config…</div></div>`;
      } else if (cfgs.error) {
        expanded = `<div style="padding:4px 20px 20px"><div class="empty" style="color:${C.warn}">${esc(cfgs.error)}</div></div>`;
      } else if (!cfgs.length) {
        expanded = `<div style="padding:4px 20px 20px"><div style="font-size:12.5px;color:#5e6a7b">Aucun fichier de configuration trouvé pour ce jeu.</div></div>`;
      } else {
        expanded = `<div style="padding:4px 20px 20px">${cfgs.map((c) => {
          const has = c.settings && c.settings.length;
          const grid = has
            ? `<div style="font-family:var(--mono);font-size:10.5px;color:#5e6a7b;margin:8px 0 6px">${c.settings.length} réglage(s) graphique(s)${c.totalKeys ? " sur " + c.totalKeys + " clés" : ""}</div>
               <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(180px,1fr));gap:5px 18px">${c.settings.slice(0, 60).map((s) => `<div style="font-family:var(--mono);font-size:12px;color:#8b97a8">${esc(s.key)} = <span style="color:#e7ecf3">${esc(s.value)}</span></div>`).join("")}</div>`
            : (c.format && c.format !== "Ini"
              ? `<div style="font-size:12px;color:#5e6a7b;margin-top:6px">Format ${esc(c.format)} — fichier localisé, lecture des réglages à venir.</div>`
              : `<div style="font-size:12px;color:#5e6a7b;margin-top:6px">Aucun réglage graphique reconnu${c.totalKeys ? " (" + c.totalKeys + " clés)" : ""}.</div>`);
          return `<div style="background:#0e131b;border:1px solid #1b222d;border-radius:11px;padding:15px 17px;margin-top:8px">
            <div style="display:flex;align-items:center;gap:9px"><span style="display:inline-flex;padding:2px 8px;border-radius:5px;font-family:var(--mono);font-size:10px;font-weight:600;background:#1b222d;color:#8b97a8">${esc(c.format || "?")}</span><span style="font-family:var(--mono);font-size:11.5px;color:#8b97a8;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${esc(c.path)}</span></div>
            ${grid}</div>`;
        }).join("")}</div>`;
      }
    }
    return `<div style="background:#12161f;border:1px solid #232b38;border-radius:14px;overflow:hidden">
      <div style="display:flex;align-items:center;justify-content:space-between;gap:14px;padding:18px 20px">
        <div style="min-width:0">
          <div style="display:flex;align-items:center;gap:10px;margin-bottom:5px"><span style="font-weight:600;font-size:16px">${esc(g.name)}</span>${src(g.source)}</div>
          <div style="font-family:var(--mono);font-size:11.5px;color:#5e6a7b;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${esc(g.installDir)}</div>
        </div>
        <button class="btn" style="flex:none;padding:9px 16px;font-size:12.5px" data-action="toggle-config" data-idx="${i}">${open ? "Masquer" : "Voir la config"}</button>
      </div>
      ${expanded}
    </div>`;
  }).join("");
  return `<div class="view">${head("04 — Jeux installés", "Analyse du disque", right)}${lead}<div style="display:flex;flex-direction:column;gap:12px">${cards}</div></div>`;
}

// ============================================================================
//  VIEW: 04 — Matériel
// ============================================================================
function viewHardware() {
  if (!state.hardware) {
    return `<div class="view">${head("05 — Matériel détecté", "Configuration")}<div class="empty"><span class="spinner-sm"></span> Analyse du matériel…</div></div>`;
  }
  const h = state.hardware;
  const gpu = primaryGpu(h);
  const disp = primaryDisplay(h);
  const store = h.storage && h.storage[0];
  const spec = (k, v, sub) => `<div class="card" style="padding:20px 22px">
    <div style="display:flex;align-items:center;gap:9px;margin-bottom:11px"><span style="width:6px;height:6px;border-radius:2px;background:${C.accent}"></span><span style="font-family:var(--mono);font-size:10.5px;letter-spacing:.14em;text-transform:uppercase;color:#5e6a7b">${esc(k)}</span></div>
    <div style="font-size:17px;font-weight:600;letter-spacing:-.01em">${esc(v)}</div>
    <div style="font-family:var(--mono);font-size:12px;color:#8b97a8;margin-top:6px">${esc(sub)}</div>
  </div>`;
  const specs = [
    spec("Processeur", h.cpu.name || "capteur absent", `${h.cpu.physicalCores || "?"} cœurs · ${h.cpu.logicalCores || "?"} threads${h.cpu.maxClockMhz ? " · jusqu'à " + (h.cpu.maxClockMhz / 1000).toFixed(1) + " GHz" : ""}`),
    spec("Carte graphique", gpu ? gpu.name : "capteur absent", gpu ? `${gpu.isDiscrete ? "dédiée" : "intégrée"}${gpu.vramBytes ? " · " + gib(gpu.vramBytes) + " VRAM" : ""}${gpu.driverVersion ? " · pilote " + gpu.driverVersion : ""}` : "—"),
    spec("Mémoire", gib(h.memory.totalBytes) || "capteur absent", `${gib(h.memory.availableBytes) ? gib(h.memory.availableBytes) + " dispo" : ""}${h.memory.speedMtPerSecond ? " · " + h.memory.speedMtPerSecond + " MT/s" : ""}`.replace(/^ · /, "") || "—"),
    spec("Écran", disp ? disp.name : "capteur absent", disp ? `${disp.widthPixels} × ${disp.heightPixels}${disp.refreshRateHz ? " · " + disp.refreshRateHz + " Hz" : ""}${disp.isPrimary ? " · principal" : ""}` : "—"),
    spec("Stockage", store ? store.model : "capteur absent", store ? `${store.mediaType || ""}${gib(store.totalBytes) ? " · " + gib(store.totalBytes) : ""}`.replace(/^ · /, "") : "—"),
    spec("Système", h.os.name || "capteur absent", `${h.os.version ? "build " + h.os.version : ""}${h.os.architecture ? " · " + h.os.architecture : ""}`.replace(/^ · /, "") || "—"),
  ].join("");
  const note = `<div class="empty">Chaque capteur est sondé indépendamment et toutes les mesures sont nullables — « capteur absent » n'est jamais confondu avec « valeur zéro ».</div>`;
  return `<div class="view">${head("05 — Matériel détecté", "Configuration")}<div style="display:grid;grid-template-columns:1fr 1fr;gap:14px">${specs}</div>${note}</div>`;
}

// ============================================================================
//  VIEW: 05 — Système
// ============================================================================
function viewSystem() {
  const lead = `<p class="lead">Au-delà d'un jeu précis — optimisations globales classées par gravité.</p>`;
  if (state.system === null) {
    return `<div class="view">${head("06 — Optimisation du PC", "Conseiller matériel")}${lead}<div class="empty"><span class="spinner-sm"></span> Analyse du système…</div></div>`;
  }
  if (!state.system.length) {
    return `<div class="view">${head("06 — Optimisation du PC", "Conseiller matériel")}${lead}<div class="empty" style="color:${C.good}">✓ Rien à signaler — ta configuration est bien équilibrée.</div></div>`;
  }
  const sev = { Warning: ["À corriger", C.bad], Suggestion: ["Conseillé", C.warn], Info: ["Info", C.accent] };
  const cards = state.system.map((a) => {
    const [sl, sc] = sev[a.severity] || [a.severity, C.faint];
    return `<div class="card" style="padding:20px 22px;border-left:3px solid ${sc}">
      <div style="display:flex;align-items:center;gap:11px;margin-bottom:7px;flex-wrap:wrap">
        <span style="font-weight:600;font-size:15.5px">${esc(a.title)}</span>
        <span style="display:inline-flex;padding:3px 9px;border-radius:6px;font-family:var(--mono);font-size:10px;background:#1b222d;color:#8b97a8">${esc(a.category)}</span>
        <span style="display:inline-flex;padding:3px 9px;border-radius:6px;font-size:10px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:${sc};background:color-mix(in srgb, ${sc} 16%, transparent)">${esc(sl)}</span>
      </div>
      <div style="font-size:13.5px;color:#a9b3c1;line-height:1.6">${esc(a.detail)}</div>
    </div>`;
  }).join("");
  return `<div class="view">${head("06 — Optimisation du PC", "Conseiller matériel")}${lead}<div style="display:flex;flex-direction:column;gap:12px">${cards}</div></div>`;
}

// ============================================================================
//  VIEW: 06 — Outils
// ============================================================================
// Catalogue d'outils : décrits d'abord, installés/intégrés ensuite. Seul
// PresentMon est câblé aujourd'hui ; les autres sont documentés pour que tu
// saches à quoi ils servent avant de décider.
const TOOLS_CATALOG = [
  {
    id: "presentmon", name: "PresentMon", category: "Mesure FPS", core: true,
    purpose: "Mesure les vrais FPS et frametimes sans rien injecter dans le jeu (source officielle Intel, via ETW).",
    why: "C'est la base de l'analyse d'écart FPS, du 1 % low et de la courbe de frametime du cockpit.",
  },
  {
    id: "capframex", name: "CapFrameX", category: "Benchmark", core: false,
    purpose: "Capture et compare des sessions FPS/frametimes détaillées, avec 1 % / 0.1 % low et export.",
    why: "Idéal pour valider un réglage sur plusieurs runs — un cran au-dessus de la mesure ponctuelle.",
  },
  {
    id: "latencymon", name: "LatencyMon", category: "Latence", core: false,
    purpose: "Mesure la latence DPC/ISR des pilotes — le coupable typique des micro-freezes et coupures audio.",
    why: "Quand le jeu saccade sans que CPU/GPU ne saturent, c'est souvent un pilote : cet outil le pointe.",
  },
  {
    id: "hwinfo", name: "HWiNFO", category: "Capteurs", core: false,
    purpose: "Lit finement tous les capteurs : températures, tensions, horloges, états de throttling.",
    why: "Complète l'analyse thermique du Moniteur avec des capteurs par cœur et par rail d'alimentation.",
  },
  {
    id: "gpuz", name: "GPU-Z", category: "Infos GPU", core: false,
    purpose: "Détaille le GPU : modèle exact, VRAM, BIOS, état du lien PCIe, capteurs.",
    why: "Utile pour vérifier que le GPU tourne au bon régime PCIe et n'est pas bridé.",
  },
  {
    id: "ddu", name: "DDU (Display Driver Uninstaller)", category: "Pilotes", core: false,
    purpose: "Désinstalle proprement et complètement un pilote GPU avant réinstallation.",
    why: "La solution de référence contre les crashes et TDR qui apparaissent après une mise à jour de pilote.",
  },
];

function viewTools() {
  const t = state.tools;
  const lead = `<p class="lead">Chaque outil est décrit ici — à quoi il sert et pourquoi il peut aider — <strong style="color:#c3ccd8">avant</strong> toute installation. Seul PresentMon est intégré pour l'instant ; les autres arrivent à ta demande, avec ton feu vert sur l'installation.</p>`;
  const badge2 = (color, txt) => `<span style="display:inline-flex;align-items:center;gap:6px;padding:5px 11px;border-radius:8px;font-family:var(--mono);font-size:11px;background:color-mix(in srgb, ${color} 14%, transparent);color:${color}">${txt}</span>`;

  const cards = TOOLS_CATALOG.map((tool) => {
    let status, action = "";
    if (tool.id === "presentmon") {
      if (!t) { status = badge2(C.faint, "vérification…"); }
      else if (t.presentMon.installed) {
        status = badge2(C.good, "installé");
        action = `<div style="display:flex;align-items:center;gap:10px;color:${C.good};font-size:12.5px;font-family:var(--mono);margin-top:14px"><span style="width:8px;height:8px;border-radius:50%;background:${C.good}"></span>${esc(t.presentMon.path || "")}</div>`;
      } else if (!t.platform.isWindows) {
        status = badge2(C.warn, "Windows uniquement");
      } else {
        status = badge2(C.warn, "non installé");
        action = `<div style="margin-top:14px"><button class="btn-primary" data-action="install-pm" id="install-pm">Télécharger et installer</button></div>`;
      }
    } else {
      status = badge2(C.faint, "intégration à venir");
      action = `<div style="margin-top:14px;font-family:var(--mono);font-size:11.5px;color:#5e6a7b">Décris ton besoin — je peux l'intégrer aux analyses.</div>`;
    }
    return `<div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;gap:10px;margin-bottom:10px">
        <span style="font-size:16px;font-weight:600">${esc(tool.name)}</span>${status}
      </div>
      <span style="display:inline-flex;padding:3px 9px;border-radius:6px;font-family:var(--mono);font-size:10px;background:#1b222d;color:#8b97a8;margin-bottom:12px">${esc(tool.category)}</span>
      <p style="margin:0 0 8px;font-size:13.5px;color:#c3ccd8;line-height:1.55">${esc(tool.purpose)}</p>
      <p style="margin:0;font-size:12.5px;color:#8b97a8;line-height:1.5"><span style="color:${C.accentSoft}">Pourquoi :</span> ${esc(tool.why)}</p>
      ${action}
    </div>`;
  }).join("");

  const backupsCard = `<div class="card">
    <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:14px"><span style="font-size:16px;font-weight:600">Sauvegardes de config</span>${badge2(C.good, "actif")}</div>
    <p style="margin:0;font-size:13.5px;color:#8b97a8;line-height:1.6">Chaque modification de config est précédée d'une sauvegarde horodatée. La restauration est chirurgicale : seule la clé testée revient à sa valeur d'origine. Restaure depuis le panneau « Appliquer » de l'analyse.</p>
  </div>`;

  return `<div class="view">${head("09 — Outils", "Boîte à outils & sauvegardes")}${lead}
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">${cards}</div>
    ${backupsCard}
  </div>`;
}

// ============================================================================
//  VIEW: Moniteur temps réel (analyse continue)
// ============================================================================
function procRow(u, mode) {
  const isCpu = mode === "cpu";
  const width = isCpu ? Math.min(100, u.cpuPercent) : (u.memoryPercent != null ? Math.min(100, u.memoryPercent) : 0);
  const value = isCpu ? `${Math.round(u.cpuPercent)} %` : mib(u.memoryBytes);
  const barColor = u.isGame ? C.accent : (isCpu ? usageColor(u.cpuPercent) : C.warn);
  const nameColor = u.isGame ? C.accentSoft : "#c3ccd8";
  return `<div style="display:flex;align-items:center;gap:12px;padding:9px 0;border-bottom:1px solid #1b222d">
    <div style="flex:1;min-width:0">
      <div style="display:flex;align-items:center;gap:7px">
        <span style="font-size:13px;color:${nameColor};white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${esc(u.name)}</span>
        ${u.isGame ? `<span style="font-family:var(--mono);font-size:9px;letter-spacing:.08em;text-transform:uppercase;color:${C.accent};border:1px solid ${C.accent};border-radius:4px;padding:1px 5px">jeu</span>` : ""}
      </div>
      <div style="height:5px;background:#0e131b;border-radius:999px;overflow:hidden;margin-top:5px"><div style="height:100%;width:${width}%;background:${barColor};border-radius:999px"></div></div>
    </div>
    <span style="font-family:var(--mono);font-size:12.5px;color:#e7ecf3;width:64px;text-align:right;flex:none">${value}</span>
    <span style="font-family:var(--mono);font-size:10px;color:#5e6a7b;width:52px;text-align:right;flex:none">pid ${u.pid}</span>
  </div>`;
}
function viewMoniteur() {
  const live = state.live;
  const kicker = "03 — Analyse continue";
  const updated = live && live.timestamp ? new Date(live.timestamp).toLocaleTimeString("fr-FR") : null;
  const right = `<div style="display:flex;align-items:center;gap:8px;font-family:var(--mono);font-size:11px;color:${live ? C.good : "#5e6a7b"}"><span class="dot ${live ? "dot-good pulse" : ""}" style="width:8px;height:8px;${live ? "" : "background:#5e6a7b"}"></span>${live ? `en direct${updated ? " · maj " + updated : ""}` : "démarrage…"}</div>`;
  const lead = `<p class="lead">Perfoptimize échantillonne le PC en continu (toutes les 2 s) et attribue les ressources — pour voir en direct la charge CPU/GPU/RAM, les températures, et ce qui se dispute la machine avec ton jeu avant que ça ne provoque un freeze.</p>`;

  if (!live) {
    return `<div class="view">${head(kicker, "Moniteur temps réel", right)}${lead}<div class="empty"><span class="spinner-sm"></span> Démarrage du moniteur…</div></div>`;
  }

  const gauges = [];
  gauges.push(gauge({ label: "CPU système", value: pct(live.systemCpuPercent), ringValue: live.systemCpuPercent, unit: "%", sub: `${live.processCount} processus`, color: usageColor(live.systemCpuPercent) }, true));
  if (live.gpuPercent != null)
    gauges.push(gauge({ label: "GPU", value: pct(live.gpuPercent), ringValue: live.gpuPercent, unit: "%", sub: live.vramPercent != null ? `VRAM ${pct(live.vramPercent)} %` : "", color: usageColor(live.gpuPercent) }, true));
  gauges.push(live.ramUsedPercent != null
    ? gauge({ label: "RAM", value: pct(live.ramUsedPercent), ringValue: live.ramUsedPercent, unit: "%", sub: `${gib(live.ramUsedBytes) || "?"} / ${gib(live.ramTotalBytes) || "?"}`, color: vramColor(live.ramUsedPercent) }, true)
    : `<div class="card gauge" style="flex:1 1 168px;max-width:360px;padding:20px;justify-content:center"><div class="empty" style="border:none;background:none;padding:0;text-align:center">RAM système<br>non disponible</div></div>`);
  if (live.cpuTemperatureC != null)
    gauges.push(gauge({ label: "Temp CPU", value: Math.round(live.cpuTemperatureC), ringValue: live.cpuTemperatureC, unit: "°", sub: "", color: cpuTempColor(live.cpuTemperatureC) }, true));
  if (live.gpuTemperatureC != null)
    gauges.push(gauge({ label: "Temp GPU", value: Math.round(live.gpuTemperatureC), ringValue: live.gpuTemperatureC, unit: "°", sub: live.gpuThrottling ? "throttling" : "", color: live.gpuThrottling ? C.bad : tempColor(live.gpuTemperatureC) }, true));
  if (live.dpcPercent != null)
    gauges.push(gauge({ label: "Latence DPC", value: live.dpcPercent.toFixed(1), ringValue: Math.min(100, live.dpcPercent * 5), unit: "%", sub: live.interruptPercent != null ? `ISR ${live.interruptPercent.toFixed(1)} %` : "", color: live.dpcPercent >= 8 ? C.bad : live.dpcPercent >= 4 ? C.warn : C.good }, true));

  const noTemp = live.cpuTemperatureC == null && live.gpuTemperatureC == null;
  const tempNote = noTemp
    ? `<div class="empty">Températures indisponibles : aucun capteur lisible sans droits administrateur ou pilote adéquat (fréquent hors Windows/sans GPU NVIDIA). Ce n'est pas « 0 °C », c'est « capteur absent ».</div>`
    : "";
  const sessionCard = `<div class="card" style="display:flex;flex-direction:column;justify-content:center;gap:10px">
    ${cardHead(live.game ? "Jeu en session" : "Aucun jeu détecté", live.game ? C.good : C.faint)}
    <div style="font-size:20px;font-weight:700">${live.game ? esc(live.game) : "En veille"}</div>
    <div style="font-family:var(--mono);font-size:11px;color:#5e6a7b">${live.game ? `pid ${live.gamePid} · ressources surveillées en direct` : "Lance un jeu : le moniteur attribuera automatiquement les ressources."}</div>
  </div>`;

  const alerts = (live.alerts || []).map((a) => {
    const col = a.level === "bad" ? C.bad : a.level === "info" ? C.accent : C.warn;
    return `<div class="card" style="padding:16px 20px;border-left:3px solid ${col}">
      <div style="font-weight:600;font-size:14.5px;margin-bottom:4px;color:${col}">${esc(a.title)}</div>
      <div style="font-size:13px;color:#a9b3c1;line-height:1.5">${esc(a.detail)}</div>
    </div>`;
  }).join("");
  const alertsBlock = alerts
    ? `<div style="display:flex;flex-direction:column;gap:10px">${alerts}</div>`
    : `<div class="empty" style="color:${C.good}">✓ Rien ne se dispute les ressources en ce moment.</div>`;

  const table = (title, rows, mode) => `<div class="card">
    ${cardHead(title, mode === "cpu" ? C.accent : C.warn)}
    ${rows.length ? rows.map((u) => procRow(u, mode)).join("") : `<div style="font-size:13px;color:#5e6a7b;padding:6px 0">Aucun processus actif mesuré.</div>`}
  </div>`;

  return `<div class="view">${head(kicker, "Moniteur temps réel", right)}${lead}
    <div style="display:flex;flex-wrap:wrap;gap:16px">${gauges.join("")}</div>
    ${tempNote}
    ${sessionCard}
    ${alertsBlock}
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:16px">
      ${table("Top CPU", live.topByCpu || [], "cpu")}
      ${table("Top mémoire", live.topByMemory || [], "mem")}
    </div>
  </div>`;
}

// ============================================================================
//  VIEW: Réseau
// ============================================================================
const WELL_KNOWN_PORTS = { 80: "HTTP", 443: "HTTPS", 53: "DNS", 3389: "RDP", 445: "SMB", 22: "SSH", 3074: "Xbox", 27015: "Steam", 5222: "chat" };
function viewReseau() {
  const n = state.network;
  const right = `<button class="btn" data-action="rescan-network">Rescanner</button>`;
  const lead = `<p class="lead">Qui, sur ton PC, parle à Internet — et à quoi. Chaque connexion sortante est rattachée au programme qui l'ouvre et la destination est résolue en nom quand c'est possible, pour distinguer le normal (navigateur, Discord, Steam, Windows Update) de l'inhabituel. En lecture seule.</p>`;
  if (n === null) {
    return `<div class="view">${head("08 — Réseau", "Qui parle à Internet ?", right)}${lead}<div class="empty"><span class="spinner-sm"></span> Lecture des connexions réseau…</div></div>`;
  }
  const statCard = (val, label, color) => `<div class="card" style="flex:1 1 200px">
    <div style="font-family:var(--mono);font-size:34px;font-weight:700;color:${color}">${val}</div>
    <div style="font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:#8b97a8;margin-top:6px">${label}</div>
  </div>`;

  // Connexions groupées par programme, avec niveau de confiance.
  const TRUST = { Normal: ["identifié", C.good], Unknown: ["inconnu", C.warn], Suspicious: ["à vérifier", C.bad] };
  const trustRank = { Suspicious: 0, Unknown: 1, Normal: 2 };
  const conns = (n.connections || []);
  const groups = {};
  for (const c of conns) {
    const key = c.process || "Programme inconnu";
    (groups[key] = groups[key] || []).push(c);
  }
  const groupKeys = Object.keys(groups).sort((a, b) => {
    const wa = Math.min(...groups[a].map((c) => trustRank[c.trust] ?? 2));
    const wb = Math.min(...groups[b].map((c) => trustRank[c.trust] ?? 2));
    return wa - wb || a.localeCompare(b);
  });
  let connBlock;
  if (!conns.length) {
    connBlock = `<div class="card">${cardHead("Connexions par programme")}<div style="font-size:13px;color:#5e6a7b;padding:6px 0">Aucune connexion sortante établie (ou attribution indisponible sans droits suffisants).</div></div>`;
  } else {
    connBlock = groupKeys.map((k) => {
      const rows = groups[k];
      const worst = rows.reduce((w, c) => (trustRank[c.trust] < trustRank[w] ? c.trust : w), "Normal");
      const [lbl, col] = TRUST[worst] || TRUST.Unknown;
      const note = rows.find((c) => c.note)?.note;
      const dests = rows.map((c) => {
        const lblPort = WELL_KNOWN_PORTS[c.remotePort];
        return `<div style="display:flex;align-items:center;justify-content:space-between;gap:12px;padding:9px 0;border-bottom:1px solid #1b222d">
          <span style="font-family:var(--mono);font-size:12.5px;color:#c3ccd8;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${esc(c.remoteHost || c.remoteAddress)}</span>
          <span style="font-family:var(--mono);font-size:11.5px;color:#8b97a8;flex:none">:${c.remotePort}${lblPort ? " " + lblPort : ""}</span>
        </div>`;
      }).join("");
      return `<div class="card" style="border-left:3px solid ${col}">
        <div style="display:flex;align-items:center;gap:10px;margin-bottom:10px;flex-wrap:wrap">
          <span style="font-weight:600;font-size:15px">${esc(k)}</span>
          <span style="display:inline-flex;padding:3px 9px;border-radius:6px;font-size:10px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:${col};background:color-mix(in srgb, ${col} 16%, transparent)">${lbl}</span>
          <span style="font-family:var(--mono);font-size:11px;color:#5e6a7b;margin-left:auto">${rows.length} conn.</span>
        </div>
        ${note ? `<div style="font-size:12.5px;color:${col};margin-bottom:8px">${esc(note)}</div>` : ""}
        <div>${dests}</div>
      </div>`;
    }).join("");
  }

  const ports = (n.listeningPorts || []);
  const portChips = ports.length
    ? ports.map((p) => {
        const lbl = WELL_KNOWN_PORTS[p];
        return `<span style="display:inline-flex;align-items:center;gap:6px;padding:5px 11px;border-radius:6px;background:#0e131b;border:1px solid #1b222d;font-family:var(--mono);font-size:12px;color:#c3ccd8;margin:0 7px 7px 0">${p}${lbl ? `<span style="color:#5e6a7b">${lbl}</span>` : ""}</span>`;
      }).join("")
    : `<div style="font-size:13px;color:#5e6a7b">Aucun port en écoute détecté.</div>`;

  const guide = `<div class="card">${cardHead("Comment savoir si c'est normal ?", C.accent)}
    <div style="font-size:13px;color:#a9b3c1;line-height:1.6">
      <p style="margin:0 0 10px"><span style="color:${C.good}">Normal :</span> ton navigateur, Discord, Steam/Epic, un jeu, et <span style="font-family:var(--mono)">svchost</span> (Windows Update/télémétrie) qui parlent à des noms connus (google, cloudflare, akamai, steamcontent, discord…) sur les ports 443/80.</p>
      <p style="margin:0 0 10px"><span style="color:${C.warn}">À regarder :</span> un programme que tu ne reconnais pas, ou une destination sans nom sur un port inhabituel. Ce n'est pas forcément malveillant, mais mérite un coup d'œil.</p>
      <p style="margin:0"><span style="color:${C.bad}">Suspect :</span> un exécutable lancé depuis un dossier temporaire/téléchargements. Clic droit sur le process (Gestionnaire des tâches) → « Ouvrir l'emplacement du fichier », vérifie l'éditeur/la signature.</p>
      <p style="margin:12px 0 0;font-size:12px;color:#5e6a7b">Ce panneau t'aide à <strong style="color:#8b97a8">voir et comprendre</strong> — pour un vrai verdict, lance un scan Windows Defender ou Malwarebytes.</p>
    </div>
  </div>`;

  return `<div class="view">${head("08 — Réseau", "Qui parle à Internet ?", right)}${lead}
    <div style="display:flex;flex-wrap:wrap;gap:16px">
      ${statCard(n.establishedCount, "Connexions établies", C.accentSoft)}
      ${statCard(n.listeningCount, "Ports en écoute", C.warn)}
    </div>
    ${connBlock}
    ${guide}
    <div class="card">${cardHead("Ports en écoute", C.warn)}<div style="margin-top:2px">${portChips}</div>
      <div style="font-size:12px;color:#5e6a7b;margin-top:10px">Un port en écoute n'est pas un problème en soi (jeux, services Windows, launchers en ouvrent).</div>
    </div>
  </div>`;
}

// ============================================================================
//  VIEW: Incidents & diagnostic
// ============================================================================
const INCIDENT_KINDS = {
  AppCrash: ["Crash appli", C.bad],
  AppHang: ["Appli figée", C.warn],
  GpuReset: ["Plantage GPU", C.bad],
  BugCheck: ["Écran bleu", C.bad],
  UnexpectedShutdown: ["Arrêt inattendu", C.warn],
  OutOfMemory: ["Mémoire saturée", C.warn],
  DiskError: ["Erreur disque", C.bad],
};
function incidentWhen(iso) {
  if (!iso) return "";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "";
  return d.toLocaleString("fr-FR", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });
}
function viewIncidents() {
  const lead = `<p class="lead">Post-mortem en lecture seule : Perfoptimize lit l'historique d'événements de Windows (14 derniers jours) et explique chaque incident — crash, écran bleu, plantage GPU, arrêt inattendu — avec sa cause probable et une solution. Rien n'est modifié.</p>`;
  if (state.incidents === null) {
    return `<div class="view">${head("07 — Incidents & diagnostic", "D'où viennent les plantages")}${lead}<div class="empty"><span class="spinner-sm"></span> Lecture de l'historique d'incidents…</div></div>`;
  }
  if (!state.incidents.supported) {
    return `<div class="view">${head("07 — Incidents & diagnostic", "D'où viennent les plantages")}${lead}<div class="empty">Le diagnostic d'incidents s'appuie sur le journal d'événements Windows — disponible uniquement sur Windows.</div></div>`;
  }
  const list = state.incidents.incidents || [];
  if (!list.length) {
    return `<div class="view">${head("07 — Incidents & diagnostic", "D'où viennent les plantages")}${lead}<div class="empty" style="color:${C.good}">✓ Aucun incident détecté sur les 14 derniers jours. Système stable.</div></div>`;
  }

  // summary chips by kind
  const counts = {};
  for (const i of list) counts[i.kind] = (counts[i.kind] || 0) + 1;
  const summary = Object.keys(counts).map((k) => {
    const [lbl, col] = INCIDENT_KINDS[k] || [k, C.faint];
    return `<span style="display:inline-flex;align-items:center;gap:7px;padding:6px 12px;border-radius:999px;background:color-mix(in srgb, ${col} 14%, transparent);color:${col};font-family:var(--mono);font-size:12px"><span style="width:7px;height:7px;border-radius:50%;background:${col}"></span>${counts[k]} × ${esc(lbl)}</span>`;
  }).join("");

  const cards = list.map((i) => {
    const [lbl, col] = INCIDENT_KINDS[i.kind] || [i.kind, C.faint];
    const when = incidentWhen(i.when);
    return `<div class="card" style="padding:20px 22px;border-left:3px solid ${col}">
      <div style="display:flex;align-items:center;gap:11px;margin-bottom:8px;flex-wrap:wrap">
        <span style="display:inline-flex;padding:4px 10px;border-radius:7px;font-size:11px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:${col};background:color-mix(in srgb, ${col} 16%, transparent)">${esc(lbl)}</span>
        <span style="font-weight:600;font-size:15px">${esc(i.title)}</span>
        ${i.process ? `<span style="font-family:var(--mono);font-size:11.5px;color:#8b97a8">${esc(i.process)}</span>` : ""}
        ${when ? `<span style="font-family:var(--mono);font-size:11px;color:#5e6a7b;margin-left:auto">${esc(when)}</span>` : ""}
      </div>
      ${i.detail ? `<div style="font-size:13px;color:#a9b3c1;line-height:1.55;margin-bottom:12px">${esc(i.detail)}</div>` : ""}
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:12px">
        <div style="background:#0e131b;border:1px solid #1b222d;border-radius:11px;padding:13px 15px">
          <div style="font-family:var(--mono);font-size:10px;letter-spacing:.12em;text-transform:uppercase;color:#5e6a7b;margin-bottom:6px">Cause probable</div>
          <div style="font-size:13px;color:#c3ccd8;line-height:1.5">${esc(i.likelyCause)}</div>
        </div>
        <div style="background:#0e131b;border:1px solid #1b222d;border-radius:11px;padding:13px 15px">
          <div style="font-family:var(--mono);font-size:10px;letter-spacing:.12em;text-transform:uppercase;color:${C.good};margin-bottom:6px">Solution</div>
          <div style="font-size:13px;color:#c3ccd8;line-height:1.5">${esc(i.suggestedFix)}</div>
        </div>
      </div>
    </div>`;
  }).join("");

  return `<div class="view">${head("07 — Incidents & diagnostic", "D'où viennent les plantages")}${lead}
    <div style="display:flex;flex-wrap:wrap;gap:10px">${summary}</div>
    <div style="display:flex;flex-direction:column;gap:12px">${cards}</div>
  </div>`;
}

// ============================================================================
//  Render + navigation
// ============================================================================
const VIEWS = { overview: viewOverview, analyse: viewAnalyse, moniteur: viewMoniteur, library: viewLibrary, hardware: viewHardware, system: viewSystem, incidents: viewIncidents, reseau: viewReseau, tools: viewTools };

function render() {
  // nav
  $("nav").innerHTML = NAV.map(([id, label], i) =>
    `<button class="nav-item${state.view === id ? " active" : ""}" data-nav="${id}">
      <span class="nav-bar"></span><span class="nav-num">${String(i + 1).padStart(2, "0")}</span><span>${esc(label)}</span>
    </button>`).join("");

  // topbar session
  const g = state.games && state.games.running;
  $("topbar-game").textContent = g ? g.name : "Aucun jeu";
  $("topbar-pid").textContent = g ? `pid ${g.processId}` : "";

  // pm chip
  const pm = state.tools && state.tools.presentMon;
  const chip = $("pm-chip");
  if (pm) {
    chip.style.color = pm.installed ? C.good : C.warn;
    chip.textContent = pm.installed ? "PresentMon ✓" : "PresentMon · non installé";
  }

  $("content").innerHTML = (VIEWS[state.view] || viewOverview)();

  // chart is drawn after markup is in the DOM
  if (state.view === "analyse" && state.monitorPhase === "done" && state.lastReport
      && state.lastReport.fps && state.lastReport.fps.series && state.lastReport.fps.series.length) {
    drawChart(state.lastReport.fps.series);
    if (!chartResizeBound) {
      window.addEventListener("resize", () => {
        if (state.view === "analyse" && state.lastReport && state.lastReport.fps && state.lastReport.fps.series)
          drawChart(state.lastReport.fps.series);
      });
      chartResizeBound = true;
    }
  }
}

function setView(v) {
  state.view = v;
  // lazy loads
  if (v === "library" && state.library === null) loadLibrary();
  if (v === "system" && state.system === null) loadSystem();
  if (v === "hardware" && !state.hardware) loadHardware();
  if (v === "analyse" && state.settingsGuide === null) loadSettingsGuide();
  if (v === "incidents" && state.incidents === null) loadIncidents();
  if (v === "reseau" && state.network === null) loadNetwork();
  if (v === "tools" && !state.tools) loadTools();
  render();
  window.scrollTo(0, 0); // a fresh view starts at the top, under the sticky topbar
}

// ---- FPS canvas chart ------------------------------------------------------
function drawChart(series) {
  const cv = $("fps-chart");
  if (!cv) return;
  const rect = cv.getBoundingClientRect();
  const w = Math.max(220, rect.width || 680), h = rect.height || 190;
  const dpr = window.devicePixelRatio || 1;
  cv.width = w * dpr; cv.height = h * dpr;
  const x = cv.getContext("2d");
  x.setTransform(dpr, 0, 0, dpr, 0, 0);
  x.clearRect(0, 0, w, h);

  const d = series, n = d.length;
  const dataMin = Math.min.apply(null, d), dataMax = Math.max.apply(null, d);
  const minV = Math.min(25, Math.floor(dataMin - 3));
  const maxV = Math.max(78, Math.ceil(dataMax + 3), state.targetFps + 6);
  const padT = 12, padB = 6;
  const px = (i) => (n <= 1 ? 0 : (i / (n - 1)) * w);
  const py = (v) => padT + (1 - (v - minV) / (maxV - minV)) * (h - padT - padB);

  [30, 45, 60, 75].forEach((gv) => {
    if (gv < minV || gv > maxV) return;
    const yy = py(gv);
    x.strokeStyle = "rgba(255,255,255,0.045)"; x.lineWidth = 1;
    x.beginPath(); x.moveTo(0, yy); x.lineTo(w, yy); x.stroke();
    x.fillStyle = "rgba(140,151,168,0.5)"; x.font = "10px 'JetBrains Mono', monospace";
    x.fillText(String(gv), 3, yy - 3);
  });

  const ty = py(state.targetFps);
  x.strokeStyle = "rgba(224,160,56,0.55)"; x.setLineDash([5, 4]);
  x.beginPath(); x.moveTo(0, ty); x.lineTo(w, ty); x.stroke(); x.setLineDash([]);

  const grad = x.createLinearGradient(0, padT, 0, h);
  grad.addColorStop(0, "rgba(56,207,230,0.28)"); grad.addColorStop(1, "rgba(56,207,230,0.01)");
  x.beginPath(); x.moveTo(px(0), py(d[0]));
  for (let i = 1; i < n; i++) x.lineTo(px(i), py(d[i]));
  x.lineTo(px(n - 1), h); x.lineTo(px(0), h); x.closePath();
  x.fillStyle = grad; x.fill();

  x.beginPath(); x.moveTo(px(0), py(d[0]));
  for (let i = 1; i < n; i++) x.lineTo(px(i), py(d[i]));
  x.strokeStyle = "rgba(56,207,230,1)"; x.lineWidth = 1.9; x.lineJoin = "round"; x.stroke();

  const hitch = Math.min(46, state.targetFps * 0.8);
  for (let i = 0; i < n; i++) {
    if (d[i] < hitch) { x.beginPath(); x.arc(px(i), py(d[i]), 2.7, 0, 7); x.fillStyle = "rgba(230,80,72,1)"; x.fill(); }
  }
}

// ============================================================================
//  Data loaders
// ============================================================================
async function loadGames() {
  try { state.games = await getJson("/api/games"); } catch { state.games = { running: null, known: [] }; }
  if (state.view === "overview") render(); else updateTopbar();
}
async function loadHardware() {
  try { state.hardware = await getJson("/api/hardware"); } catch (e) { state.hardware = null; }
  if (state.view === "overview" || state.view === "hardware") render();
}
async function loadTools() {
  try { state.tools = await getJson("/api/tools"); } catch { state.tools = { presentMon: { installed: false }, platform: { isWindows: false } }; }
  render();
}
async function loadSystem() {
  try { const s = await getJson("/api/system"); state.system = s.advice || []; } catch { state.system = []; }
  if (state.view === "system") render();
}
async function loadLibrary() {
  try { const r = await getJson("/api/library"); state.library = r.games || []; } catch { state.library = []; }
  if (state.view === "library") render();
}
async function loadIncidents() {
  try { state.incidents = await getJson("/api/incidents"); }
  catch { state.incidents = { supported: false, incidents: [] }; }
  if (state.view === "incidents") render();
}

async function loadNetwork() {
  try { state.network = await getJson("/api/network"); }
  catch { state.network = { establishedCount: 0, listeningCount: 0, topRemotes: [], listeningPorts: [] }; }
  if (state.view === "reseau") render();
}
async function loadSettingsGuide() {
  const bt = state.lastReport && state.lastReport.bottleneck ? state.lastReport.bottleneck.type : "";
  try { state.settingsGuide = await getJson("/api/settings-guide?bottleneck=" + encodeURIComponent(bt)); }
  catch { state.settingsGuide = null; }
  if (state.view === "analyse") render();
}

// ---- Live monitor (global continuous polling) ------------------------------
// The backend samples continuously on its own; the UI polls it from every view
// so alerts (overheating, a resource hog, driver latency) surface even when the
// user is not on the Moniteur.
async function pollLive() {
  if (liveBusy) return;
  liveBusy = true;
  try {
    const r = await getJson("/api/live");
    state.live = r;
    state.alerts = r.alerts || [];
    if (liveFirstPoll) {
      // Don't fire a burst of notifications for conditions already active at startup.
      for (const a of state.alerts) seenAlertIds.add(a.id);
      liveFirstPoll = false;
    } else {
      for (const a of state.alerts) {
        if (seenAlertIds.has(a.id)) continue;
        seenAlertIds.add(a.id);
        notifyAlert(a);
      }
    }
    updateAlertChip();
    if (state.view === "moniteur") render();
  } catch { /* transient; keep the last snapshot on screen */ }
  finally { liveBusy = false; }
}
function startGlobalLive() {
  if (liveTimer) return;
  pollLive();
  liveTimer = setInterval(pollLive, 3000);
}
function stopLive() {
  if (liveTimer) { clearInterval(liveTimer); liveTimer = null; }
}
function notifyAlert(a) {
  try {
    if (typeof Notification !== "undefined" && Notification.permission === "granted")
      new Notification("Perfoptimize — " + a.title, { body: a.detail, tag: "perfoptimize-" + a.id });
  } catch { /* notifications are a bonus, never required */ }
}
function ensureNotifyPermission() {
  try {
    if (typeof Notification !== "undefined" && Notification.permission === "default")
      Notification.requestPermission();
  } catch { /* ignore */ }
}
function updateAlertChip() {
  const chip = $("alert-chip");
  if (!chip) return;
  const active = state.alerts || [];
  if (!active.length) { chip.classList.add("hidden"); return; }
  const color = active.some((a) => a.level === "bad") ? C.bad : C.warn;
  chip.classList.remove("hidden");
  chip.style.color = color;
  chip.style.borderColor = color;
  const count = $("alert-count");
  if (count) count.textContent = String(active.length);
}
async function loadVersion() {
  try { const v = await getJson("/api/version"); $("version").textContent = `· v${v.version}`; } catch { /* non-essential */ }
}
function updateTopbar() {
  const g = state.games && state.games.running;
  $("topbar-game").textContent = g ? g.name : "Aucun jeu";
  $("topbar-pid").textContent = g ? `pid ${g.processId}` : "";
}

async function loadGameConfig(dir) {
  try {
    const r = await getJson("/api/library/config?dir=" + encodeURIComponent(dir));
    state.libConfigs[dir] = r.ok ? r.configs : { error: r.message };
  } catch (e) {
    state.libConfigs[dir] = { error: e.message };
  }
  if (state.view === "library") render();
}

// ---- Monitor ---------------------------------------------------------------
async function runMonitor() {
  if (state.monitorPhase === "running") return;
  state.monitorPhase = "running";
  state.applyPhase = "idle"; state.applyResult = null; state.applyMsg = "";
  render();

  let left = state.seconds;
  if (countdownTimer) clearInterval(countdownTimer);
  countdownTimer = setInterval(() => {
    left -= 1;
    const c = $("countdown");
    if (c && left >= 0) c.textContent = left;
  }, 1000);

  try {
    const r = await postJson("/api/monitor", { seconds: state.seconds, goal: state.goal, targetFps: state.targetFps });
    state.lastReport = r;
    state.monitorPhase = "done";
    loadSettingsGuide(); // re-tailor the API recommendation to the measured bottleneck
    // keep the detected game fresh for the topbar/overview
    if (r.game && (!state.games || !state.games.running)) loadGames();
  } catch (e) {
    state.lastReport = null;
    state.monitorPhase = "idle";
    alert("Erreur pendant la mesure : " + e.message);
  } finally {
    if (countdownTimer) { clearInterval(countdownTimer); countdownTimer = null; }
    render();
  }
}

// ---- Apply / restore -------------------------------------------------------
function applyContext() {
  const r = state.lastReport;
  return { game: r.game, bottleneck: r.bottleneck.type, goal: state.goal };
}
async function previewApply() {
  state.applyMsg = "";
  try {
    const r = await postJson("/api/apply", { ...applyContext(), write: false });
    if (!r.ok) { state.applyMsg = r.message; state.applyResult = null; state.applyPhase = "idle"; }
    else { state.applyResult = r; state.applyPhase = "preview"; }
  } catch (e) { state.applyMsg = e.message; }
  render();
}
async function doApply() {
  state.applyMsg = "";
  try {
    const r = await postJson("/api/apply", { ...applyContext(), write: true });
    if (!r.ok) { state.applyMsg = r.message; }
    else { state.applyResult = r; state.applyPhase = "applied"; }
  } catch (e) { state.applyMsg = e.message; }
  render();
}
async function doRestore() {
  state.applyMsg = "";
  try {
    const r = await postJson("/api/restore", { game: state.lastReport.game });
    state.applyPhase = "restored";
    state.applyMsg = r.message;
  } catch (e) { state.applyMsg = e.message; }
  render();
}

// ---- Tools -----------------------------------------------------------------
async function installPresentMon() {
  const btn = $("install-pm");
  if (btn) { btn.disabled = true; btn.innerHTML = `<span class="spinner-sm"></span> Téléchargement…`; }
  try {
    const r = await postJson("/api/tools/presentmon");
    await loadTools(); // refresh installed state
    if (!r.success) state.tools.presentMon.message = r.message;
    render();
  } catch (e) {
    render();
  }
}

// ============================================================================
//  Wiring
// ============================================================================
$("nav").addEventListener("click", (e) => {
  const btn = e.target.closest("[data-nav]");
  if (btn) setView(btn.dataset.nav);
});

$("content").addEventListener("click", (e) => {
  const a = e.target.closest("[data-action]");
  if (!a) return;
  e.preventDefault();
  switch (a.dataset.action) {
    case "goto-analyse": setView("analyse"); break;
    case "goto-hardware": setView("hardware"); break;
    case "run": runMonitor(); break;
    case "preview": previewApply(); break;
    case "apply": doApply(); break;
    case "restore": doRestore(); break;
    case "install-pm": installPresentMon(); break;
    case "rescan-library": state.library = null; state.libConfigs = {}; state.openConfig = null; render(); loadLibrary(); break;
    case "rescan-network": state.network = null; render(); loadNetwork(); break;
    case "toggle-config": {
      const idx = parseInt(a.dataset.idx, 10);
      if (state.openConfig === idx) { state.openConfig = null; render(); }
      else {
        state.openConfig = idx;
        const dir = state.library[idx].installDir;
        render();
        if (state.libConfigs[dir] === undefined) loadGameConfig(dir);
      }
      break;
    }
  }
});

$("content").addEventListener("change", (e) => {
  const f = e.target.closest("[data-field]");
  if (!f) return;
  const v = f.value;
  if (f.dataset.field === "seconds") state.seconds = parseInt(v, 10) || 30;
  else if (f.dataset.field === "goal") state.goal = v;
  else if (f.dataset.field === "targetFps") state.targetFps = parseInt(v, 10) || 60;
});

$("quit").addEventListener("click", async () => {
  stopLive();
  try { await postJson("/api/shutdown"); } catch { /* server stops */ }
  $("quit-overlay").classList.remove("hidden");
});

// ---- boot ------------------------------------------------------------------
document.addEventListener("click", ensureNotifyPermission, { once: true });
const alertChip = $("alert-chip");
if (alertChip) alertChip.addEventListener("click", () => setView("moniteur"));

render();
loadGames();
loadHardware();
loadTools();
loadVersion();
startGlobalLive(); // continuous, from every view

