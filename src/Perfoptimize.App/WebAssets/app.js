"use strict";

/* Perfoptimize cockpit — vanilla SPA over the local JSON API. */

const $ = (id) => document.getElementById(id);
const esc = (s) => String(s == null ? "" : s).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
const gib = (bytes) => (bytes == null) ? "?" : `${(bytes / 1073741824).toFixed(1)} Gio`;

const COL = { accent: "oklch(0.82 0.13 205)", good: "oklch(0.82 0.18 150)", warn: "oklch(0.80 0.15 72)", bad: "oklch(0.64 0.21 27)" };

async function getJson(url) {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${url} → ${r.status}`);
  return r.json();
}
async function postJson(url, body) {
  const r = await fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: body ? JSON.stringify(body) : null });
  if (!r.ok) throw new Error(`${url} → ${r.status}`);
  return r.json();
}

// ----- State -----------------------------------------------------------------
const S = {
  view: "overview",
  seconds: 30, goal: "balanced", targetFps: 60,
  monitorPhase: "idle",       // idle | running | done
  countdown: 30,
  analysis: null,             // last /api/monitor result
  hardware: null,
  detected: null,             // { name, processId, isKnown } | null
  tools: null,
  openConfig: null,           // installDir currently expanded
};

const NAV = [
  ["overview", "Vue d'ensemble"],
  ["analyse", "Analyse"],
  ["library", "Jeux installés"],
  ["hardware", "Matériel"],
  ["system", "Système"],
  ["tools", "Outils"],
];

// ----- Gauge helpers ---------------------------------------------------------
function ring(pctVal, color) {
  const v = Math.max(0, Math.min(100, pctVal || 0));
  const deg = (v / 100 * 270).toFixed(1);
  return `background:conic-gradient(from 225deg, ${color} ${deg}deg, #0d1017 ${deg}deg, #0d1017 270deg, transparent 270deg)`;
}
const usageColor = (v) => v == null ? "#2a333f" : v >= 90 ? COL.bad : v >= 75 ? COL.warn : COL.good;
const tempColor = (v) => v == null ? "#2a333f" : v >= 85 ? COL.bad : v >= 75 ? COL.warn : COL.good;

function gauge(g, small) {
  const val = g.value == null ? "—" : g.value;
  return `<div class="gauge${small ? " sm" : ""}">
    <div class="ring"><div class="arc" style="${ring(g.pct, g.color)}"></div><div class="rhole"></div>
      <div class="val">${esc(val)}<u>${esc(g.unit || "")}</u></div></div>
    <div class="gauge-label"><b>${esc(g.label)}</b><small>${esc(g.sub || "")}</small></div>
  </div>`;
}

// ----- Navigation ------------------------------------------------------------
function renderNav() {
  $("nav").innerHTML = NAV.map(([id, label], i) =>
    `<button class="nav-item${S.view === id ? " active" : ""}" data-view="${id}">
      <span class="bar"></span><span class="num">${String(i + 1).padStart(2, "0")}</span><span>${esc(label)}</span>
    </button>`).join("");
  $("nav").querySelectorAll(".nav-item").forEach(b => b.onclick = () => setView(b.dataset.view));
}

function setView(v) {
  S.view = v;
  renderNav();
  renderView();
}

function renderView() {
  const host = $("view");
  if (S.view === "overview") host.innerHTML = viewOverview();
  else if (S.view === "analyse") host.innerHTML = viewAnalyse();
  else if (S.view === "library") host.innerHTML = viewLibrary();
  else if (S.view === "hardware") host.innerHTML = viewHardware();
  else if (S.view === "system") host.innerHTML = viewSystem();
  else if (S.view === "tools") host.innerHTML = viewTools();
  wireView();
}

// ----- Section header helper -------------------------------------------------
function head(num, kicker, title, right) {
  return `<div class="section-head"><div>
    <div class="kicker">${num} — ${esc(kicker)}</div>
    <h1 class="title">${esc(title)}</h1>
  </div>${right || ""}</div>`;
}

// ============================================================================
// 01 — OVERVIEW
// ============================================================================
function viewOverview() {
  const g = S.detected;
  const hw = S.hardware;
  const a = S.analysis;

  const gameName = g ? g.name : "Aucun jeu détecté";
  const chips = [];
  if (g) chips.push(`<span class="chip ${g.isKnown ? "accent" : ""}">${g.isKnown ? "profil détaillé" : "détecté automatiquement"}</span>`);
  if (g) chips.push(`<span class="chip">pid ${g.processId}</span>`);
  if (hw && hw.gpus[0]) {
    const disp = hw.displays.find(d => d.isPrimary) || hw.displays[0];
    chips.push(`<span class="chip">${esc(hw.gpus[0].name)}${disp ? " · " + disp.widthPixels + "p" : ""}</span>`);
  }

  const hero = `<div class="card hero"><div class="hero-halo"></div><div style="position:relative">
    <div style="display:flex;align-items:center;gap:10px;margin-bottom:12px">
      <span class="${g ? "live-dot" : "live-dot off"}"></span>
      <span class="kicker" style="color:${g ? COL.good : "var(--faint)"}">${g ? "jeu détecté · en cours" : "aucune session"}</span>
    </div>
    <div style="font-size:40px;font-weight:700;letter-spacing:-.02em;line-height:1.02">${esc(gameName)}</div>
    <div style="display:flex;gap:8px;margin-top:14px;flex-wrap:wrap">${chips.join("")}</div>
    </div>
    <div style="position:relative;display:flex;align-items:center;justify-content:space-between;gap:18px 32px;flex-wrap:wrap;margin-top:24px;padding-top:22px;border-top:1px solid var(--border-weak)">
      <div style="display:flex;gap:40px">
        <div><div class="kicker">Objectif</div><div style="font-size:18px;font-weight:600;margin-top:4px">${goalLabel(S.goal)}</div></div>
        <div><div class="kicker">FPS visés</div><div class="mono" style="font-size:18px;font-weight:600;margin-top:4px">${S.targetFps}</div></div>
      </div>
      <button class="btn primary big" data-goto="analyse">Lancer une analyse →</button>
    </div></div>`;

  // Gauges from the last analysis (honest: only shown once measured).
  let gauges;
  if (a) {
    gauges = `<div class="gauge-row">
      ${gauge({ label: "GPU", pct: a.gpu.avg, value: rnd(a.gpu.avg), unit: "%", sub: `part jeu ${pctOr(a.gpu.gameShare)}`, color: usageColor(a.gpu.avg) })}
      ${gauge({ label: "CPU", pct: a.cpu.avg, value: rnd(a.cpu.avg), unit: "%", sub: `cœur max ${pctOr(a.cpu.busiestCore)}`, color: usageColor(a.cpu.avg) })}
      ${gauge({ label: "VRAM", pct: a.memory.vram, value: rnd(a.memory.vram), unit: "%", sub: a.memory.vram == null ? "n/a" : "pic session", color: usageColor(a.memory.vram) })}
      ${gauge({ label: "Temp GPU", pct: a.gpu.tempC, value: rnd(a.gpu.tempC), unit: "°", sub: a.gpu.tempC == null ? "capteur absent" : "pic", color: tempColor(a.gpu.tempC) })}
    </div>`;
  } else {
    gauges = `<div class="card" style="text-align:center;color:var(--muted);font-size:13.5px">Aucune mesure encore. <a href="#" data-goto="analyse">Lance une analyse →</a> pour instrumenter le GPU, le CPU, la VRAM et la température.</div>`;
  }

  // Hardware summary + recent diagnostic.
  const hwSummary = hw ? hwSummaryRows(hw) : `<div class="note">Chargement…</div>`;
  const diag = a ? recentDiagnostic(a) : `<p class="note">Le dernier diagnostic apparaîtra ici après une analyse.</p>`;

  const bottom = `<div class="grid2">
    <div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:18px">
        <div class="card-title"><span class="tick"></span><span>Matériel</span></div>
        <a href="#" class="mono" data-goto="hardware" style="font-size:11px">détails →</a>
      </div>
      <div class="kv-list">${hwSummary}</div>
    </div>
    <div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:18px">
        <div class="card-title"><span class="tick"></span><span>Diagnostic récent</span></div>
        <a href="#" class="mono" data-goto="analyse" style="font-size:11px">analyse →</a>
      </div>
      ${diag}
    </div>
  </div>`;

  return `<div class="view">${head("01", "Vue d'ensemble", "Poste de pilotage")}${hero}${gauges}${bottom}</div>`;
}

function hwSummaryRows(hw) {
  const disp = hw.displays.find(d => d.isPrimary) || hw.displays[0];
  const rows = [
    ["CPU", hw.cpu.name],
    ["GPU", hw.gpus[0] ? `${hw.gpus[0].name}${hw.gpus[0].vramBytes ? " · " + gib(hw.gpus[0].vramBytes) : ""}` : "non détecté"],
    ["RAM", gib(hw.memory.totalBytes) + (hw.memory.speedMtPerSecond ? ` · ${hw.memory.speedMtPerSecond} MT/s` : "")],
    ["Écran", disp ? `${disp.widthPixels}×${disp.heightPixels}${disp.refreshRateHz ? " · " + disp.refreshRateHz + " Hz" : ""}` : "non détecté"],
  ];
  return rows.map(([k, v]) => `<div class="kv-row"><span class="k">${k}</span><span class="v">${esc(v)}</span></div>`).join("");
}

function recentDiagnostic(a) {
  const conf = a.bottleneck.confidence === "Confirmed";
  return `<div style="display:flex;align-items:center;gap:12px;margin-bottom:16px">
    <span class="verdict-badge" style="font-size:12px;padding:5px 12px">${bottleneckLabel(a.bottleneck.type)}</span>
    <span class="confirm ${conf ? "" : "suspected"}"><i></i>${conf ? "confirmé" : "suspecté"}</span>
  </div>
  <p style="margin:0;font-size:13.5px;line-height:1.6;color:var(--text3)">${esc(a.bottleneck.explanation || "")}</p>`;
}

// ============================================================================
// 02 — ANALYSE
// ============================================================================
function viewAnalyse() {
  const control = `<div class="card control-bar">
    <label class="field">Durée
      <select id="f-seconds">
        ${[10, 30, 60].map(s => `<option value="${s}" ${S.seconds === s ? "selected" : ""}>${s} s</option>`).join("")}
      </select>
    </label>
    <label class="field">Objectif
      <select id="f-goal">
        ${[["quality", "Qualité"], ["balanced", "Équilibré"], ["performance", "Performance"]].map(([v, l]) => `<option value="${v}" ${S.goal === v ? "selected" : ""}>${l}</option>`).join("")}
      </select>
    </label>
    <label class="field">FPS visés
      <input type="number" id="f-target" min="30" max="360" step="1" value="${S.targetFps}">
    </label>
    <button class="btn primary" id="run-btn" ${S.monitorPhase === "running" ? "disabled" : ""}>${S.monitorPhase === "running" ? "Mesure…" : "Lancer l'analyse"}</button>
    <div style="flex:1;min-width:180px;font-size:12.5px;color:var(--faint);line-height:1.5">Lance ton jeu, reste dans une scène représentative, puis démarre la mesure.</div>
  </div>`;

  let body = "";
  if (S.monitorPhase === "running") {
    body = `<div class="center-note">
      <div class="spinner big"></div>
      <div style="font-size:15px">Mesure en cours — <span class="mono" id="countdown" style="color:${COL.accent}">${S.countdown}</span> s restantes</div>
      <div class="note">Joue normalement, l'attribution suit le processus du jeu.</div>
    </div>`;
  } else if (S.monitorPhase === "done" && S.analysis) {
    body = analyseResults(S.analysis);
  } else {
    body = `<div class="center-note"><div class="note" style="font-size:13.5px">Aucune mesure pour l'instant. Configure la durée et l'objectif, puis lance l'analyse.</div></div>`;
  }

  return `<div class="view">${head("02", "Analyse des performances", "Mesurer, pas deviner")}${control}${body}</div>`;
}

function analyseResults(a) {
  const t = a.target;
  const parts = [];
  // Verdict
  const conf = a.bottleneck.confidence === "Confirmed";
  let fpsLine = "";
  if (t.measuredAverageFps != null) {
    const gap = t.requiredGainPercent > 0 ? `<span style="color:${COL.warn}">+${Math.round(t.requiredGainPercent)} % nécessaires</span>` : "objectif atteint";
    const est = t.gainHigh != null ? ` · gain estimé <span class="mono">+${Math.round(t.gainLow)} % à +${Math.round(t.gainHigh)} %</span>` : "";
    fpsLine = `<div class="foot"><span class="mono" style="color:var(--text)">${t.measuredAverageFps.toFixed(0)} FPS</span> mesurés · objectif ${t.targetFps} · ${gap}${est}</div>`;
  } else {
    fpsLine = `<div class="foot">Sans capture PresentMon, l'écart FPS est théorique. <a href="#" data-goto="tools">Installe PresentMon →</a> pour mesurer les vrais FPS.</div>`;
  }
  parts.push(`<div class="verdict">
    <div style="display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:12px">
      <span class="verdict-badge">${bottleneckLabel(a.bottleneck.type)}</span>
      <span class="confirm ${conf ? "" : "suspected"}"><i></i>diagnostic ${conf ? "confirmé" : "suspecté"}</span>
    </div>
    <p>${esc(a.bottleneck.explanation || "")}</p>${fpsLine}
  </div>`);

  // Gauges
  parts.push(`<div class="gauge-row">
    ${gauge({ label: "CPU moy.", pct: a.cpu.avg, value: rnd(a.cpu.avg), unit: "%", sub: `part jeu ${pctOr(a.cpu.gameShare)}`, color: usageColor(a.cpu.avg) }, true)}
    ${gauge({ label: "Cœur max", pct: a.cpu.busiestCore, value: rnd(a.cpu.busiestCore), unit: "%", sub: "cœur le plus chargé", color: usageColor(a.cpu.busiestCore) }, true)}
    ${gauge({ label: "GPU moy.", pct: a.gpu.avg, value: rnd(a.gpu.avg), unit: "%", sub: `part jeu ${pctOr(a.gpu.gameShare)}`, color: usageColor(a.gpu.avg) }, true)}
    ${gauge({ label: "VRAM pic", pct: a.memory.vram, value: rnd(a.memory.vram), unit: "%", sub: a.memory.vram == null ? "n/a" : "session", color: usageColor(a.memory.vram) }, true)}
    ${gauge({ label: "Temp GPU", pct: a.gpu.tempC, value: rnd(a.gpu.tempC), unit: "°", sub: a.gpu.tempC == null ? "capteur absent" : "pic", color: tempColor(a.gpu.tempC) }, true)}
  </div>`);

  // Chart + stats (only with real frametime data)
  if (a.fps && a.fps.series && a.fps.series.length > 1) {
    const stats = [
      { n: a.fps.averageFps.toFixed(0), l: "FPS moyens", c: "var(--text)" },
      { n: a.fps.onePercentLowFps.toFixed(0), l: "1 % low", c: COL.warn },
      { n: a.fps.worstFrametimeMs.toFixed(1), l: "Pire frametime (ms)", c: "var(--text)" },
      { n: a.fps.stutterPercent.toFixed(1) + " %", l: "À-coups", c: "var(--text)" },
    ];
    parts.push(`<div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:18px;flex-wrap:wrap;gap:8px">
        <div class="card-title"><span class="tick"></span><span>FPS dans le temps</span></div>
        <div class="legend"><span><span class="l-fps"></span>FPS</span><span><span class="l-tgt"></span>cible</span><span><span class="l-stut"></span>à-coup</span></div>
      </div>
      <canvas id="fps-chart"></canvas>
      <div class="stat-row">${stats.map(s => `<div class="stat"><div class="n" style="color:${s.c}">${esc(s.n)}</div><div class="l">${esc(s.l)}</div></div>`).join("")}</div>
    </div>`);
  }

  // Advisories (three states, contextual)
  parts.push(advisoriesCard(a));

  // Recommendations
  if (a.recommendations && a.recommendations.length) {
    const rows = a.recommendations.map(r => {
      const gain = r.expectedGainPercent ? `<span class="gain">jusqu'à ~+${Math.round(r.expectedGainPercent)} %</span>` : "";
      const w = r.expectedGainPercent ? Math.min(100, Math.round(r.expectedGainPercent / 40 * 100)) : 0;
      const bar = r.expectedGainPercent ? `<div class="meter"><div class="progress"><i style="width:${w}%"></i></div>${gain}</div>` : "";
      return `<div class="reco"><div class="body"><div class="t">${esc(r.title)}</div><div class="r">${esc(r.reason)}</div></div>${bar}</div>`;
    }).join("");
    const theo = a.gainsAreMeasured ? "" : `<p style="margin:0 0 18px;font-size:12.5px;color:var(--faint)">Gains <strong style="color:var(--muted)">théoriques</strong> issus du profil, non encore vérifiés sur ta machine.</p>`;
    parts.push(`<div class="card">
      <div class="card-title" style="margin-bottom:6px"><span class="tick"></span><span>Recommandations</span></div>
      ${theo}<div class="stack">${rows}</div></div>`);
  }

  // Apply to config (settings)
  if (a.canApply) {
    window.__opt = { game: a.game, bottleneck: a.bottleneck.type, goal: S.goal };
    parts.push(`<div class="card">
      <div class="card-title" style="margin-bottom:6px"><span class="tick"></span><span>Appliquer aux fichiers de config</span></div>
      <p style="margin:0 0 16px;font-size:12.5px;color:var(--faint);max-width:720px">Modifie les réglages liés à un fichier. Prévisualise d'abord — rien n'est écrit sans ta confirmation, et une sauvegarde est faite avant toute modification.</p>
      <button class="btn" id="preview-btn">Prévisualiser les modifications</button>
      <div id="opt-out" style="margin-top:14px"></div>
    </div>`);
  }

  // Notes
  if (a.notes && a.notes.length) {
    parts.push(`<div class="card"><div class="stack tight">${a.notes.map(n => `<div class="note">${esc(n)}</div>`).join("")}</div></div>`);
  }

  return `<div class="view">${parts.join("")}</div>`;
}

function advisoriesCard(a) {
  const adv = a.advisories || [];
  if (!adv.length) return "";
  const kindLabel = { Crash: "CRASH", Stutter: "SACCADES", PoorValue: "PEU RENTABLE" };
  const kindBadge = { Crash: "bad", Stutter: "bad", PoorValue: "warn" };
  const evLabel = { CuratedCommunityClaim: "conseil communautaire (source unique)", CommunityValidated: "validé par plusieurs sources", Benchmarked: "mesuré en benchmark" };

  const triggered = adv.filter(x => x.status === "Triggered");
  const ok = adv.filter(x => x.status === "AlreadyOk");
  const unknown = adv.filter(x => x.status === "CannotVerify");

  const prov = (x) => {
    let p = evLabel[x.evidenceLevel] || x.evidenceLevel || "";
    if (x.lastVerified) p += ` · vérifié le ${x.lastVerified}`;
    const links = (x.sources || []).filter(s => s.url).map(s => `<a href="${esc(s.url)}" target="_blank" rel="noopener">source</a>`).join(" ");
    if (links) p += " · " + links;
    return p ? `<div class="prov">${p}</div>` : "";
  };

  let rows = "";
  for (const x of triggered) {
    const cur = x.currentValue != null ? ` <span class="mono" style="color:var(--muted);font-size:12px">(valeur : ${esc(x.currentValue)})</span>` : "";
    const prio = x.highPriority ? `<span class="badge bad">PRIORITAIRE</span>` : "";
    const canFix = a.canApply ? `<button class="btn sm" data-fix="${esc(x.id)}" style="margin-top:10px">Vérifier et corriger</button><div class="apply-out hidden" data-fixout="${esc(x.id)}"></div>` : "";
    rows += `<div class="advisory triggered${x.highPriority ? " priority" : ""}">
      <div class="head">🔴 <b>${esc(x.setting)}</b><span class="badge ${kindBadge[x.kind] || "warn"}">${kindLabel[x.kind] || esc(x.kind)}</span>${prio}</div>
      <div class="detail"><strong style="color:var(--text)">Problème détecté dans la configuration actuelle.</strong>${cur} <strong style="color:var(--text)">${esc(x.recommendation)}.</strong> ${esc(x.detail)}</div>
      ${prov(x)}${canFix}</div>`;
  }
  for (const x of ok) {
    const cur = x.currentValue != null ? ` (valeur : ${esc(x.currentValue)})` : "";
    rows += `<div class="advisory ok"><div class="head">🟢 <b>${esc(x.setting)}</b></div>
      <div class="detail muted">Réglage déjà correctement configuré${cur} — aucune action nécessaire.</div></div>`;
  }
  for (const x of unknown) {
    rows += `<div class="advisory unknown"><div class="head">⚪ <b>${esc(x.setting)}</b></div>
      <div class="detail muted">Valeur impossible à vérifier (${esc(x.explanation || "clé absente ou contexte inconnu")}). À surveiller : ${esc(x.recommendation)}.</div></div>`;
  }

  return `<div class="card">
    <div class="card-title bad" style="margin-bottom:6px"><span class="tick"></span><span>Réglages à éviter</span></div>
    <p style="margin:0 0 16px;font-size:12.5px;color:var(--faint)">Évalués contre ta configuration réelle — problème détecté / déjà correct / impossible à vérifier.</p>
    <div class="stack tight">${rows}</div></div>`;
}

// ============================================================================
// 03 — LIBRARY
// ============================================================================
function viewLibrary() {
  const right = `<button class="btn ghost" id="rescan-btn">Rescanner</button>`;
  return `<div class="view">${head("03", "Jeux installés", "Analyse du disque", right)}
    <p class="lead" id="lib-lead">Toutes les bibliothèques Steam de tous les disques, Epic Games et les installations autonomes (Path of Exile 2…).</p>
    <div id="lib-list"><div class="center-note"><div class="spinner big"></div><div class="note">Analyse des disques…</div></div></div>
  </div>`;
}

async function scanLibrary() {
  const host = $("lib-list");
  if (host) host.innerHTML = `<div class="center-note"><div class="spinner big"></div><div class="note">Analyse des disques…</div></div>`;
  try {
    const r = await getJson("/api/library");
    const inspected = r.inspected && r.inspected.length
      ? `<details class="inspected"><summary>Emplacements inspectés (${r.inspected.length})</summary><ul>${r.inspected.map(p => `<li>${esc(p)}</li>`).join("")}</ul></details>`
      : "";
    if ($("lib-lead")) $("lib-lead").innerHTML = `${r.games.length} jeu(x) découverts avec leur dossier et leurs fichiers de configuration.`;
    if (!r.games.length) {
      host.innerHTML = `<div class="card"><div class="note">Aucun jeu détecté sur les disques analysés (Steam, Epic, ou dossiers autonomes).</div>${inspected}</div>`;
      return;
    }
    const cards = r.games.map(g => {
      const srcClass = g.source === "Epic" ? "epic" : g.source === "Folder" ? "folder" : "";
      const srcLabel = g.source === "Folder" ? "autonome" : g.source;
      return `<div class="lib-card">
        <div class="lib-head">
          <div style="min-width:0"><div class="lib-name"><b>${esc(g.name)}</b><span class="src ${srcClass}">${esc(srcLabel)}</span></div>
            <div class="lib-dir">${esc(g.installDir)}</div></div>
          <button class="btn ghost sm" data-cfgdir="${encodeURIComponent(g.installDir)}">Voir la config</button>
        </div>
        <div class="lib-cfg hidden" data-cfgfor="${encodeURIComponent(g.installDir)}"></div>
      </div>`;
    }).join("");
    host.innerHTML = cards + inspected;
    host.querySelectorAll("button[data-cfgdir]").forEach(b => b.onclick = () => toggleConfig(decodeURIComponent(b.dataset.cfgdir), b));
  } catch (e) {
    host.innerHTML = `<div class="card"><div class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div></div>`;
  }
}

async function toggleConfig(dir, btn) {
  const box = document.querySelector(`[data-cfgfor="${CSS.escape(encodeURIComponent(dir))}"]`);
  if (!box) return;
  if (!box.classList.contains("hidden")) { box.classList.add("hidden"); btn.textContent = "Voir la config"; return; }
  btn.textContent = "Masquer";
  box.classList.remove("hidden");
  box.innerHTML = `<div style="padding:4px 20px 16px"><span class="spinner"></span> Lecture des fichiers de config…</div>`;
  try {
    const r = await getJson("/api/library/config?dir=" + encodeURIComponent(dir));
    if (!r.ok) { box.innerHTML = `<div style="padding:4px 20px 16px" class="note">${esc(r.message)}</div>`; return; }
    if (!r.configs.length) { box.innerHTML = `<div style="padding:4px 20px 16px" class="note">Aucun fichier de configuration trouvé pour ce jeu.</div>`; return; }
    let html = `<div style="padding:4px 0 16px">`;
    for (const c of r.configs) {
      html += `<div class="cfg"><div class="cfg-head"><span class="tag">${esc(c.format)}</span><span class="cfg-path">${esc(c.path)}</span></div>`;
      if (c.settings.length) {
        html += `<div class="cfg-count">${c.settings.length} réglage(s) graphique(s) sur ${c.totalKeys} clés</div><div class="cfg-kv">`;
        for (const s of c.settings.slice(0, 40)) html += `<div>${esc(s.key)} = <b>${esc(s.value)}</b></div>`;
        html += `</div>`;
      } else if (c.format !== "Ini") {
        html += `<div class="note" style="margin-top:6px">Format ${esc(c.format)} — localisé, lecture des réglages à venir.</div>`;
      } else {
        html += `<div class="note" style="margin-top:6px">Aucun réglage graphique reconnu (${c.totalKeys} clés).</div>`;
      }
      html += `</div>`;
    }
    box.innerHTML = html + `</div>`;
  } catch (e) {
    box.innerHTML = `<div style="padding:4px 20px 16px" class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div>`;
  }
}

// ============================================================================
// 04 — HARDWARE
// ============================================================================
function viewHardware() {
  const hw = S.hardware;
  if (!hw) return `<div class="view">${head("04", "Matériel détecté", "Configuration")}<div class="center-note"><div class="spinner big"></div></div></div>`;
  const disp = hw.displays.find(d => d.isPrimary) || hw.displays[0];
  const gpu = hw.gpus[0];
  const clock = hw.cpu.maxClockMhz ? ` · jusqu'à ${(hw.cpu.maxClockMhz / 1000).toFixed(1)} GHz` : "";
  const specs = [
    ["Processeur", hw.cpu.name, `${hw.cpu.physicalCores} cœurs · ${hw.cpu.logicalCores} threads${clock}`],
    ["Carte graphique", gpu ? gpu.name : "non détectée", gpu ? `${gpu.isDiscrete ? "dédiée" : "intégrée"}${gpu.vramBytes ? " · " + gib(gpu.vramBytes) + " VRAM" : ""}` : ""],
    ["Mémoire", gib(hw.memory.totalBytes), hw.memory.speedMtPerSecond ? `${hw.memory.speedMtPerSecond} MT/s` : "vitesse non détectée"],
    ["Écran", disp ? disp.name : "non détecté", disp ? `${disp.widthPixels} × ${disp.heightPixels}${disp.refreshRateHz ? " · " + disp.refreshRateHz + " Hz" : ""}${disp.isPrimary ? " · principal" : ""}` : ""],
    ["Système", hw.os.name, hw.os.architecture || ""],
  ];
  const cards = specs.map(([k, v, sub]) => `<div class="hw-card">
    <div class="k"><i></i><span>${esc(k)}</span></div>
    <div class="v">${esc(v)}</div>${sub ? `<div class="sub">${esc(sub)}</div>` : ""}</div>`).join("");
  return `<div class="view">${head("04", "Matériel détecté", "Configuration")}
    <div class="hw-grid">${cards}</div>
    <div class="inset note">Chaque capteur est sondé indépendamment et toutes les mesures sont nullables — « capteur absent » n'est jamais confondu avec « valeur zéro ».</div>
  </div>`;
}

// ============================================================================
// 05 — SYSTEM
// ============================================================================
function viewSystem() {
  return `<div class="view">${head("05", "Optimisation du PC", "Conseiller matériel")}
    <p class="lead">Au-delà d'un jeu précis — optimisations globales classées par gravité.</p>
    <div id="sys-list"><div class="center-note"><div class="spinner big"></div></div></div></div>`;
}

async function loadSystem() {
  const host = $("sys-list");
  if (!host) return;
  try {
    const s = await getJson("/api/system");
    if (!s.advice.length) { host.innerHTML = `<div class="card"><div class="status-line good"><i></i>Rien à signaler — ta configuration est bien équilibrée.</div></div>`; return; }
    const sev = { Warning: ["bad", "À corriger"], Suggestion: ["warn", "Conseillé"], Info: ["accent", "Info"] };
    const border = { bad: COL.bad, warn: COL.warn, accent: COL.accent };
    host.innerHTML = s.advice.map(a => {
      const [cls, label] = sev[a.severity] || ["accent", a.severity];
      return `<div class="card" style="border-left:3px solid ${border[cls]}">
        <div style="display:flex;align-items:center;gap:11px;margin-bottom:7px;flex-wrap:wrap">
          <span style="font-weight:600;font-size:15.5px">${esc(a.title)}</span>
          <span class="tag">${esc(a.category)}</span><span class="badge ${cls}">${esc(label)}</span></div>
        <div style="font-size:13.5px;color:var(--text3);line-height:1.6">${esc(a.detail)}</div></div>`;
    }).join("");
  } catch (e) {
    host.innerHTML = `<div class="card"><div class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div></div>`;
  }
}

// ============================================================================
// 06 — TOOLS
// ============================================================================
function viewTools() {
  return `<div class="view">${head("06", "Outils", "Mesure & sauvegardes")}
    <div id="tools-list"><div class="center-note"><div class="spinner big"></div></div></div></div>`;
}

async function loadTools() {
  const host = $("tools-list");
  if (!host) return;
  try {
    const t = await getJson("/api/tools");
    S.tools = t;
    updatePmChip();
    const pm = t.presentMon;
    let pmCard;
    const pmStatus = pm.installed
      ? `<span class="pm-chip ok" style="border:none;background:oklch(0.82 0.18 150 / .14)">installé</span>`
      : `<span class="pm-chip" style="border:none;background:oklch(0.80 0.15 72 / .14)">non installé</span>`;
    let pmAction;
    if (pm.installed) pmAction = `<div class="status-line good mono" style="font-size:13px"><i></i>${esc(pm.path)}</div>`;
    else if (!t.platform.isWindows) pmAction = `<div class="note">PresentMon est un outil Windows (mesure des FPS réels).</div>`;
    else pmAction = `<button class="btn primary" id="install-pm">Télécharger et installer</button>`;
    pmCard = `<div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:14px"><span style="font-size:16px;font-weight:600">PresentMon</span>${pmStatus}</div>
      <p style="margin:0 0 18px;font-size:13.5px;color:var(--muted);line-height:1.6">Mesure les vrais FPS sans injection, depuis la source officielle Intel. Nécessaire pour l'analyse d'écart FPS et la boucle mesurée.</p>
      ${pmAction}</div>`;

    // Rest of the catalog: link to official download pages.
    const others = (t.catalog || []).filter(x => x.id !== "presentmon").map(x => `<div class="card">
      <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:14px"><span style="font-size:16px;font-weight:600">${esc(x.name)}</span>${x.installed ? `<span class="pm-chip ok" style="border:none;background:oklch(0.82 0.18 150 / .14)">installé</span>` : ""}</div>
      <p style="margin:0 0 18px;font-size:13.5px;color:var(--muted);line-height:1.6">${esc(x.purpose)}</p>
      <button class="btn" data-tool="${esc(x.id)}">Ouvrir la page officielle ↗</button></div>`).join("");

    host.innerHTML = `<div class="grid2">${pmCard}${others}</div>`;
    if ($("install-pm")) $("install-pm").onclick = installPresentMon;
    host.querySelectorAll("button[data-tool]").forEach(b => b.onclick = async () => {
      b.disabled = true; try { await postJson("/api/tools/open", { id: b.dataset.tool }); } finally { setTimeout(() => b.disabled = false, 800); }
    });
  } catch (e) {
    host.innerHTML = `<div class="card"><div class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div></div>`;
  }
}

async function installPresentMon() {
  const btn = $("install-pm");
  btn.disabled = true;
  btn.innerHTML = `<span class="spinner"></span> Téléchargement…`;
  try {
    const r = await postJson("/api/tools/presentmon");
    await loadTools();
    if (!r.success) {
      const host = $("tools-list");
      if (host) host.insertAdjacentHTML("afterbegin", `<div class="card"><div class="status-line bad"><i></i>${esc(r.message)}</div></div>`);
    }
  } catch (e) {
    if (btn) { btn.disabled = false; btn.textContent = "Réessayer"; }
  }
}

// ============================================================================
// Wiring after each render
// ============================================================================
function wireView() {
  $("view").querySelectorAll("[data-goto]").forEach(b => b.onclick = (e) => { e.preventDefault(); setView(b.dataset.goto); });

  if (S.view === "analyse") {
    if ($("f-seconds")) $("f-seconds").onchange = (e) => S.seconds = parseInt(e.target.value, 10) || 30;
    if ($("f-goal")) $("f-goal").onchange = (e) => S.goal = e.target.value;
    if ($("f-target")) $("f-target").onchange = (e) => S.targetFps = parseInt(e.target.value, 10) || 60;
    if ($("run-btn")) $("run-btn").onclick = runMonitor;
    if ($("preview-btn")) $("preview-btn").onclick = previewApply;
    if (S.analysis && S.analysis.fps && S.analysis.fps.series) drawChart(S.analysis.fps.series, S.analysis.target.targetFps);
    // advisory verify-and-fix buttons
    $("view").querySelectorAll("button[data-fix]").forEach(b => b.onclick = () => fixAdvisory(b.dataset.fix, b));
  }
  if (S.view === "library") { if ($("rescan-btn")) $("rescan-btn").onclick = scanLibrary; scanLibrary(); }
  if (S.view === "hardware" && !S.hardware) loadHardware().then(() => { if (S.view === "hardware") renderView(); });
  if (S.view === "system") loadSystem();
  if (S.view === "tools") loadTools();
}

// ----- Monitor ---------------------------------------------------------------
async function runMonitor() {
  if (S.monitorPhase === "running") return;
  S.monitorPhase = "running";
  S.countdown = S.seconds;
  renderView();
  const timer = setInterval(() => {
    S.countdown -= 1;
    if ($("countdown") && S.countdown >= 0) $("countdown").textContent = S.countdown;
    if (S.countdown <= 0) clearInterval(timer);
  }, 1000);
  try {
    const r = await postJson("/api/monitor", { seconds: S.seconds, goal: S.goal, targetFps: S.targetFps });
    clearInterval(timer);
    S.analysis = r;
    S.monitorPhase = "done";
    // refresh detected game from the analysis
    if (r.game) S.detected = { name: r.game, processId: S.detected ? S.detected.processId : 0, isKnown: r.gameKnown };
    updateTopbar();
    renderView();
  } catch (e) {
    clearInterval(timer);
    S.monitorPhase = "idle";
    renderView();
    const host = $("view").querySelector(".view");
    if (host) host.insertAdjacentHTML("beforeend", `<div class="card"><div class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div></div>`);
  }
}

// ----- Advisory verify-and-fix ----------------------------------------------
async function fixAdvisory(advId, btn) {
  const a = S.analysis;
  const out = $("view").querySelector(`[data-fixout="${CSS.escape(advId)}"]`);
  btn.disabled = true;
  out.classList.remove("hidden", "fail");
  out.innerHTML = `<span class="spinner"></span> Vérification et correction…`;
  try {
    const r = await postJson("/api/advisory/apply", { game: a.game, advisoryId: advId, write: true });
    if (!r.ok) { out.innerHTML = esc(r.message); out.classList.add("fail"); btn.disabled = false; return; }
    const change = r.changes.find(c => c.advisoryId === advId) || r.changes[0];
    if (change && (change.status === "Verified" || change.status === "AlreadyAtTarget")) out.innerHTML = `✓ ${esc(change.note)}`;
    else { out.classList.add("fail"); out.innerHTML = esc(change ? change.note : "Aucun changement appliqué."); btn.disabled = false; }
  } catch (e) {
    out.classList.add("fail"); out.innerHTML = `Erreur : ${esc(e.message)}`; btn.disabled = false;
  }
}

// ----- Apply settings to config ----------------------------------------------
async function previewApply() {
  const o = window.__opt;
  const out = $("opt-out");
  out.innerHTML = `<span class="spinner"></span> Calcul des modifications…`;
  try {
    const r = await postJson("/api/apply", { game: o.game, bottleneck: o.bottleneck, goal: o.goal, write: false });
    if (!r.ok) { out.innerHTML = `<div class="status-line muted"><i></i>${esc(r.message)}</div>`; return; }
    renderPlan(r, false);
  } catch (e) { out.innerHTML = `<div class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div>`; }
}

function renderPlan(r, written) {
  const ready = r.changes.filter(c => c.status === "Ready");
  const rows = r.changes.map(c => {
    const ok = c.status === "Ready";
    const dot = written && ok ? "applied" : ok ? "" : "na";
    const chg = ok ? `${esc(c.current)} → ${esc(c.proposed)}` : `<span style="color:var(--faint)">${esc(c.note || "menu du jeu")}</span>`;
    return `<div class="plan-row"><div class="left"><span class="pdot ${dot}"></span><span class="name">${esc(c.setting)}</span></div><div class="chg">${chg}</div></div>`;
  }).join("");
  let tail;
  if (written) tail = `<div class="status-line good"><i></i>Appliqué — sauvegarde créée, tu peux restaurer à tout moment.</div>
    <button class="btn ghost" id="restore-btn">Restaurer</button>`;
  else if (ready.length) tail = `<div style="display:flex;gap:10px;flex-wrap:wrap"><button class="btn primary" id="apply-btn">Appliquer (jeu fermé)</button><button class="btn ghost" id="restore-btn">Restaurer</button></div>
    <p style="margin:12px 0 0;font-size:11.5px;color:var(--faint)">Ferme le jeu avant d'appliquer : il réécrirait sa config en quittant.</p>`;
  else tail = `<p class="note">Aucune modification applicable (réglages déjà au minimum, ou à changer dans le menu).</p>`;
  $("opt-out").innerHTML = `<div class="plan">${rows}</div>${tail}`;
  if ($("apply-btn")) $("apply-btn").onclick = doApply;
  if ($("restore-btn")) $("restore-btn").onclick = doRestore;
}

async function doApply() {
  const o = window.__opt;
  $("opt-out").innerHTML = `<span class="spinner"></span> Application…`;
  try {
    const r = await postJson("/api/apply", { game: o.game, bottleneck: o.bottleneck, goal: o.goal, write: true });
    if (!r.ok) { $("opt-out").innerHTML = `<div class="status-line bad"><i></i>${esc(r.message)}</div>`; return; }
    renderPlan(r, true);
  } catch (e) { $("opt-out").innerHTML = `<div class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div>`; }
}

async function doRestore() {
  const o = window.__opt;
  $("opt-out").innerHTML = `<span class="spinner"></span> Restauration…`;
  try {
    const r = await postJson("/api/restore", { game: o.game });
    $("opt-out").innerHTML = `<div class="status-line ${r.ok ? "good" : "bad"}"><i></i>${esc(r.message)}</div>`;
  } catch (e) { $("opt-out").innerHTML = `<div class="status-line bad"><i></i>Erreur : ${esc(e.message)}</div>`; }
}

// ----- Canvas chart ----------------------------------------------------------
function drawChart(series, target) {
  const cv = $("fps-chart");
  if (!cv || !series.length) return;
  const draw = () => {
    const r = cv.getBoundingClientRect();
    const w = Math.max(220, r.width || 680), h = r.height || 190;
    const dpr = window.devicePixelRatio || 1;
    cv.width = w * dpr; cv.height = h * dpr;
    const x = cv.getContext("2d"); x.setTransform(dpr, 0, 0, dpr, 0, 0); x.clearRect(0, 0, w, h);
    const n = series.length, padT = 12, padB = 6;
    const lo = Math.min(...series, target), hi = Math.max(...series, target);
    const minV = Math.max(0, lo - 6), maxV = hi + 6;
    const px = i => (i / (n - 1)) * w, py = v => padT + (1 - (v - minV) / (maxV - minV)) * (h - padT - padB);
    // grid
    const step = (maxV - minV) > 90 ? 30 : 15;
    for (let g = Math.ceil(minV / step) * step; g < maxV; g += step) {
      const yy = py(g); x.strokeStyle = "rgba(255,255,255,0.045)"; x.lineWidth = 1;
      x.beginPath(); x.moveTo(0, yy); x.lineTo(w, yy); x.stroke();
      x.fillStyle = "rgba(140,151,168,0.5)"; x.font = "10px 'JetBrains Mono', monospace"; x.fillText(String(g), 3, yy - 3);
    }
    // target line
    const ty = py(target); x.strokeStyle = "rgba(224,160,56,0.55)"; x.setLineDash([5, 4]);
    x.beginPath(); x.moveTo(0, ty); x.lineTo(w, ty); x.stroke(); x.setLineDash([]);
    // area
    const grad = x.createLinearGradient(0, padT, 0, h); grad.addColorStop(0, "rgba(56,207,230,0.28)"); grad.addColorStop(1, "rgba(56,207,230,0.01)");
    x.beginPath(); x.moveTo(px(0), py(series[0])); for (let i = 1; i < n; i++) x.lineTo(px(i), py(series[i]));
    x.lineTo(px(n - 1), h); x.lineTo(px(0), h); x.closePath(); x.fillStyle = grad; x.fill();
    // line
    x.beginPath(); x.moveTo(px(0), py(series[0])); for (let i = 1; i < n; i++) x.lineTo(px(i), py(series[i]));
    x.strokeStyle = "rgba(56,207,230,1)"; x.lineWidth = 1.9; x.lineJoin = "round"; x.stroke();
    // stutter dots
    const thr = target * 0.75;
    for (let i = 0; i < n; i++) if (series[i] < thr) { x.beginPath(); x.arc(px(i), py(series[i]), 2.7, 0, 7); x.fillStyle = "rgba(230,80,72,1)"; x.fill(); }
  };
  draw();
  if (window.__chartResize) window.removeEventListener("resize", window.__chartResize);
  window.__chartResize = draw;
  window.addEventListener("resize", draw);
}

// ----- Topbar ----------------------------------------------------------------
function updateTopbar() {
  const g = S.detected;
  $("session-dot").className = g ? "live-dot" : "live-dot off";
  $("session-game").textContent = g ? g.name : "Aucun jeu détecté";
  $("session-pid").textContent = g && g.processId ? `pid ${g.processId}` : "";
}
function updatePmChip() {
  const chip = $("pm-chip");
  if (!S.tools) return;
  const inst = S.tools.presentMon.installed;
  chip.className = inst ? "pm-chip ok" : "pm-chip";
  chip.textContent = inst ? "PresentMon ✓" : "PresentMon · non installé";
}

// ----- Data loaders ----------------------------------------------------------
async function loadHardware() { try { S.hardware = await getJson("/api/hardware"); } catch { /* keep null */ } }
async function loadDetected() {
  try {
    const g = await getJson("/api/games");
    S.detected = g.running ? { name: g.running.name, processId: g.running.processId, isKnown: g.running.isKnown } : null;
  } catch { S.detected = null; }
}
async function loadVersion() { try { const v = await getJson("/api/version"); $("version").textContent = `v${v.version}`; } catch { /* non-essential */ } }

// ----- Helpers ---------------------------------------------------------------
function rnd(v) { return v == null ? null : Math.round(v); }
function pctOr(v) { return v == null ? "n/a" : Math.round(v) + " %"; }
function goalLabel(g) { return { quality: "Qualité", balanced: "Équilibré", performance: "Performance" }[g] || g; }
function bottleneckLabel(t) {
  return {
    Gpu: "Limite GPU", Cpu: "Limite CPU", Vram: "Limite VRAM", Ram: "Limite RAM", Thermal: "Limite thermique",
    ExternalGpuLoad: "Charge GPU externe", ExternalCpuLoad: "Charge CPU externe", GpuHardwareLimit: "Limite matérielle GPU",
    None: "Rien de saturé", Unknown: "Indéterminé",
  }[t] || t;
}

// ----- Quit ------------------------------------------------------------------
$("quit").onclick = async () => {
  try { await postJson("/api/shutdown"); } catch { /* server stops */ }
  $("quit-overlay").classList.remove("hidden");
};

// ----- Boot ------------------------------------------------------------------
(async function boot() {
  renderNav();
  renderView();
  loadVersion();
  await Promise.all([loadDetected(), loadHardware(), (async () => { try { S.tools = await getJson("/api/tools"); } catch { } })()]);
  updateTopbar();
  updatePmChip();
  renderView();
})();
