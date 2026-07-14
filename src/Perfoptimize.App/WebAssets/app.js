"use strict";

const $ = (id) => document.getElementById(id);
const el = (tag, cls, html) => {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (html !== undefined) e.innerHTML = html;
  return e;
};
const pct = (v) => (v === null || v === undefined) ? "n/a" : `${Math.round(v)}%`;
const gib = (bytes) => (bytes === null || bytes === undefined) ? "?" : `${(bytes / 1073741824).toFixed(1)} GiB`;

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

// ----- Hardware --------------------------------------------------------------
async function loadHardware() {
  try {
    const h = await getJson("/api/hardware");
    const kv = (k, v) => `<div class="kv"><span class="k">${k}</span><span class="v">${v}</span></div>`;
    const clock = h.cpu.maxClockMhz ? `, base ${(h.cpu.maxClockMhz / 1000).toFixed(1)} GHz` : "";
    const gpu = h.gpus.map(g =>
      `${g.name} <span class="chip ${g.isDiscrete ? "discrete" : ""}">${g.isDiscrete ? "dédiée" : "intégrée"}</span>` +
      (g.vramBytes ? `<span class="chip">${gib(g.vramBytes)} VRAM</span>` : "")
    ).join("<br>") || "non détecté";
    const disp = h.displays.map(d =>
      `${d.name} — ${d.widthPixels}×${d.heightPixels}${d.refreshRateHz ? " @ " + d.refreshRateHz + " Hz" : ""}${d.isPrimary ? " (principal)" : ""}`
    ).join("<br>") || "non détecté";
    $("hardware").innerHTML =
      `<div class="row">
        ${kv("Processeur", `${h.cpu.name}<br><span class="muted">${h.cpu.physicalCores} cœurs / ${h.cpu.logicalCores} threads${clock}</span>`)}
       </div>
       <div class="row" style="margin-top:12px">${kv("Carte graphique", gpu)}</div>
       <div class="row" style="margin-top:12px">
        ${kv("Mémoire", gib(h.memory.totalBytes) + (h.memory.speedMtPerSecond ? ` <span class="muted">@ ${h.memory.speedMtPerSecond} MT/s</span>` : ""))}
        ${kv("Écran", disp)}
        ${kv("Système", `${h.os.name}<br><span class="muted">${h.os.architecture}</span>`)}
       </div>`;
  } catch (e) {
    $("hardware").innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

// ----- Games -----------------------------------------------------------------
async function loadGames() {
  try {
    const g = await getJson("/api/games");
    let running;
    if (!g.running) {
      running = `<span class="status-off">Aucun jeu en cours d'exécution</span>`;
    } else if (g.running.isKnown) {
      running = `<span class="status-ok">▶ ${g.running.name}</span> <span class="muted">(pid ${g.running.processId}) · profil détaillé</span>`;
    } else {
      const why = (g.running.reasons || []).join(" · ");
      running = `<span class="status-ok">▶ ${g.running.name}</span> <span class="muted">(pid ${g.running.processId}) · détecté automatiquement</span>` +
        (why ? `<div class="muted" style="font-size:12px;margin-top:4px">${why}</div>` : "") +
        `<div class="muted" style="font-size:12px;margin-top:2px">Jeu sans profil : analyse et conseils génériques disponibles, pas d'application automatique.</div>`;
    }
    const known = g.known.map(k => `<span class="chip">${k.name}</span>`).join("");
    $("game").innerHTML =
      `<div style="font-size:16px;margin-bottom:10px">${running}</div>
       <div class="muted" style="font-size:12px;margin-bottom:6px">${g.known.length} jeux avec profil détaillé :</div>
       <div>${known}</div>
       <button class="ghost" id="refresh-game" style="margin-top:12px">Rafraîchir</button>`;
    $("refresh-game").onclick = loadGames;
  } catch (e) {
    $("game").innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

// ----- Installed-game library ------------------------------------------------
async function scanLibrary() {
  const btn = $("scan-library");
  btn.disabled = true;
  btn.innerHTML = `<span class="spinner"></span> Analyse du disque…`;
  try {
    const r = await getJson("/api/library");
    if (!r.games.length) {
      $("library").innerHTML = `<span class="muted">Aucun jeu Steam installé détecté (ou Steam introuvable).</span>`;
    } else {
      let html = `<div class="muted" style="font-size:12px;margin-bottom:8px">${r.games.length} jeu(x) installé(s) :</div>`;
      for (const g of r.games) {
        const id = "cfg-" + btoa(g.installDir).replace(/[^a-zA-Z0-9]/g, "");
        html += `<div style="padding:8px 0;border-top:1px solid var(--border)">
          <div style="display:flex;justify-content:space-between;align-items:center;gap:10px">
            <div><strong>${g.name}</strong> <span class="chip">${g.source}</span>
              <div class="muted" style="font-size:11px">${g.installDir}</div></div>
            <button class="ghost" data-dir="${encodeURIComponent(g.installDir)}" data-out="${id}">Voir la config</button>
          </div>
          <div id="${id}"></div>
        </div>`;
      }
      $("library").innerHTML = html;
      $("library").querySelectorAll("button[data-dir]").forEach(b => {
        b.onclick = () => loadGameConfig(decodeURIComponent(b.dataset.dir), b.dataset.out, b);
      });
    }
  } catch (e) {
    $("library").innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  } finally {
    btn.disabled = false;
    btn.textContent = "Rescanner";
  }
}

async function loadGameConfig(dir, outId, btn) {
  const out = $(outId);
  out.innerHTML = `<span class="spinner"></span> Lecture des fichiers de config…`;
  btn.disabled = true;
  try {
    const r = await getJson("/api/library/config?dir=" + encodeURIComponent(dir));
    if (!r.ok) { out.innerHTML = `<span class="muted">${r.message}</span>`; return; }
    if (!r.configs.length) {
      out.innerHTML = `<span class="muted" style="font-size:13px">Aucun fichier de configuration trouvé pour ce jeu.</span>`;
      return;
    }
    let html = "";
    for (const c of r.configs) {
      html += `<div style="margin-top:10px;padding:8px 10px;background:var(--chip);border-radius:8px">
        <div style="font-size:12px" class="muted">${c.format} · ${c.path}</div>`;
      if (c.settings.length) {
        html += `<div class="muted" style="font-size:11px;margin:4px 0">${c.settings.length} réglage(s) graphique(s) sur ${c.totalKeys} clés :</div>
          <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:3px 14px;font-size:12.5px">`;
        for (const s of c.settings.slice(0, 40)) {
          html += `<div style="font-variant-numeric:tabular-nums"><span class="muted">${s.key}</span> = <strong>${s.value}</strong></div>`;
        }
        html += `</div>`;
      } else if (c.format !== "Ini") {
        html += `<div class="muted" style="font-size:12px;margin-top:4px">Format ${c.format} — lecture des réglages non encore prise en charge, mais le fichier est localisé.</div>`;
      } else {
        html += `<div class="muted" style="font-size:12px;margin-top:4px">Aucun réglage graphique reconnu (${c.totalKeys} clés).</div>`;
      }
      html += `</div>`;
    }
    out.innerHTML = html;
  } catch (e) {
    out.innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  } finally {
    btn.disabled = false;
  }
}

// ----- System optimization ---------------------------------------------------
async function loadSystem() {
  try {
    const s = await getJson("/api/system");
    if (!s.advice.length) {
      $("system").innerHTML = `<span class="status-ok">✓ Rien à signaler — ta configuration est bien équilibrée pour le jeu.</span>`;
      return;
    }
    const sevClass = { Warning: "bad", Suggestion: "warn", Info: "muted" };
    const sevLabel = { Warning: "À corriger", Suggestion: "Conseillé", Info: "Info" };
    let html = "";
    for (const a of s.advice) {
      const c = sevClass[a.severity] || "muted";
      html += `<div class="reco" style="border-left-color:var(--${c === "muted" ? "border" : c})">
        <div class="t">${a.title} <span class="chip">${a.category}</span>
          <span class="badge ${c === "bad" ? "external" : c === "warn" ? "cpu" : "none"}">${sevLabel[a.severity] || a.severity}</span></div>
        <div class="r">${a.detail}</div></div>`;
    }
    $("system").innerHTML = html;
  } catch (e) {
    $("system").innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

// ----- Monitor ---------------------------------------------------------------
async function runMonitor() {
  const seconds = parseInt($("seconds").value, 10);
  const goal = $("goal").value;
  const targetFps = parseInt($("targetFps").value, 10) || null;
  const btn = $("run");
  btn.disabled = true;
  const results = $("results");
  results.classList.remove("hidden");

  let left = seconds;
  results.innerHTML = `<p><span class="spinner"></span> Mesure en cours… <span id="count">${left}</span> s restantes. Joue normalement.</p>`;
  const timer = setInterval(() => {
    left -= 1;
    const c = $("count");
    if (c && left >= 0) c.textContent = left;
  }, 1000);

  try {
    const r = await postJson("/api/monitor", { seconds, goal, targetFps });
    clearInterval(timer);
    renderResults(r);
  } catch (e) {
    clearInterval(timer);
    results.innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  } finally {
    btn.disabled = false;
  }
}

function stat(n, l) { return `<div class="stat"><div class="n">${n}</div><div class="l">${l}</div></div>`; }

function renderResults(r) {
  const b = r.bottleneck;
  const type = (b.type || "unknown").toLowerCase();
  const badgeClass =
    type.includes("external") ? "external" :
    type.includes("hardware") ? "hardware" :
    ["gpu", "cpu", "none", "unknown"].includes(type) ? type : "gpu";
  const suspected = b.confidence === "Suspected" ? " suspected" : "";

  let html = "";
  if (r.sampleCount < 3) {
    html += `<p class="status-off">Trop peu d'échantillons collectés (${r.sampleCount}).` +
      (r.primeSeconds >= 2 ? ` La première sonde a pris ${r.primeSeconds}s — la télémétrie est lente à démarrer, réessaie.` : "") + `</p>`;
  }

  const confidenceLine = b.confidence === "Suspected"
    ? `<div class="muted" style="font-size:13px;margin-top:4px">⚠ Diagnostic <strong>suspecté</strong> — confiance moyenne. À confirmer avant toute modification.</div>`
    : `<div class="muted" style="font-size:13px;margin-top:4px">Diagnostic confirmé.</div>`;
  html += `<div style="margin:6px 0 10px">
      <span class="badge ${badgeClass}${suspected}">${b.type}</span>
    </div>
    ${confidenceLine}
    <p>${b.explanation}</p>`;

  const g = r.game ? `<span class="muted">Jeu : ${r.game}</span>` : `<span class="muted">Analyse système</span>`;
  html += `<div style="margin:10px 0">${g}</div>`;

  html += `<div class="grid2" style="margin:14px 0">
    ${stat(pct(r.cpu.avg), "CPU moyen")}
    ${stat(pct(r.cpu.busiestCore), "Cœur le + chargé")}
    ${stat(pct(r.gpu.avg), "GPU moyen")}
    ${stat(pct(r.memory.vram), "VRAM (pic)")}
    ${r.gpu.tempC != null ? stat(Math.round(r.gpu.tempC) + "°C", "Temp GPU") : ""}
    ${r.fps ? stat(r.fps.averageFps.toFixed(0), "FPS moyens") : ""}
    ${r.fps ? stat(r.fps.onePercentLowFps.toFixed(0), "1% low") : ""}
  </div>`;

  if (r.cpu.gameShare != null || r.gpu.gameShare != null) {
    html += `<p class="muted" style="font-size:13px">Part du jeu : ` +
      `CPU ${pct(r.cpu.gameShare)} · GPU ${pct(r.gpu.gameShare)}</p>`;
  }

  const t = r.target;
  if (t.measuredAverageFps != null) {
    const gap = t.requiredGainPercent > 0
      ? `objectif ${t.targetFps} FPS — +${Math.round(t.requiredGainPercent)}% nécessaire`
      : `objectif ${t.targetFps} FPS atteint`;
    let est = "";
    if (t.gainHigh != null) {
      est = Math.abs(t.gainHigh - t.gainLow) < 0.5
        ? ` · gain estimé ~+${Math.round(t.gainHigh)}%`
        : ` · gain estimé +${Math.round(t.gainLow)}% à +${Math.round(t.gainHigh)}%`;
    }
    html += `<p><strong>${t.measuredAverageFps.toFixed(0)} FPS mesurés</strong> — ${gap}${est}</p>`;
  } else {
    html += `<p class="muted">Installe PresentMon (section Outils) pour mesurer les vrais FPS et dimensionner le plan.</p>`;
  }

  // Community-documented "settings to avoid" — shown proactively, before the
  // FPS recommendations, because they prevent crashes/stutter, not just low FPS.
  if (r.advisories && r.advisories.length) {
    const kindLabel = { Crash: "⚠ Crash", Stutter: "⚠ Saccades", PoorValue: "Peu rentable" };
    html += `<h3 style="font-size:14px;margin:16px 0 4px">Réglages à éviter (connus de la communauté)</h3>`;
    for (const a of r.advisories) {
      html += `<div class="reco" style="border-left-color:var(--bad)">
        <div class="t">${a.setting} <span class="badge cpu">${kindLabel[a.kind] || a.kind}</span></div>
        <div class="r"><strong>${a.recommendation}.</strong> ${a.detail}</div></div>`;
    }
  }

  if (r.recommendations.length) {
    html += `<h3 style="font-size:14px;margin:16px 0 4px">Recommandations</h3>`;
    if (!r.gainsAreMeasured) {
      html += `<p class="muted" style="font-size:12.5px;margin:0 0 8px">Gains <strong>théoriques</strong> issus du profil,
        non vérifiés sur ta machine. Installe PresentMon (onglet Système) pour mesurer les vrais gains.</p>`;
    }
    for (const rec of r.recommendations) {
      let gain = "";
      if (rec.expectedGainPercent) {
        const measured = rec.evidence === "LocallyMeasured";
        const label = measured
          ? `~+${Math.round(rec.expectedGainPercent)}% (mesuré)`
          : `jusqu'à ~+${Math.round(rec.expectedGainPercent)}% (théorique)`;
        gain = `<span class="g" style="${measured ? "" : "opacity:.85;font-weight:600"}"> ${label}</span>`;
      }
      html += `<div class="reco"><div class="t">${rec.title}${gain}</div><div class="r">${rec.reason}</div></div>`;
    }
  }
  for (const note of r.notes) html += `<div class="note">${note}</div>`;

  // Optimization panel: only for a KNOWN game (config-bound) with a tunable verdict.
  const tunable = ["Gpu", "Cpu", "Vram", "Ram", "Thermal"].includes(b.type);
  if (r.game && r.canApply && tunable) {
    window.__opt = { game: r.game, bottleneck: b.type, goal: $("goal").value };
    html += `<div style="margin-top:18px;padding-top:14px;border-top:1px solid var(--border)">
      <h3 style="font-size:14px;margin:0 0 8px">Appliquer aux fichiers de config</h3>
      <p class="muted" style="font-size:13px">Modifie automatiquement les réglages liés à un fichier de configuration.
      Prévisualise d'abord ; rien n'est écrit sans ta confirmation, et une sauvegarde est faite avant toute modification.</p>
      <button id="preview" style="margin-top:6px">Prévisualiser les modifications</button>
      <div id="opt-out" style="margin-top:12px"></div>
    </div>`;
  }

  $("results").innerHTML = html;
  const pv = $("preview");
  if (pv) pv.onclick = previewApply;
}

async function previewApply() {
  const o = window.__opt;
  const out = $("opt-out");
  out.innerHTML = `<span class="spinner"></span> Calcul des modifications…`;
  try {
    const r = await postJson("/api/apply", { game: o.game, bottleneck: o.bottleneck, goal: o.goal, write: false });
    if (!r.ok) { out.innerHTML = `<span class="status-off">${r.message}</span>`; return; }
    renderPlan(r, false);
  } catch (e) {
    out.innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

function renderPlan(r, written) {
  const ready = r.changes.filter(c => c.status === "Ready");
  let html = "";
  for (const c of r.changes) {
    const ok = c.status === "Ready";
    const label = ok
      ? `<strong>${c.setting}</strong> : ${c.current} → ${c.proposed}`
      : `<span class="muted">${c.setting} — ${c.note}</span>`;
    html += `<div style="padding:4px 0">${written && ok ? "✓ " : ""}${label}</div>`;
  }
  if (written) {
    html += `<div class="status-ok" style="margin-top:10px">✓ Appliqué. Sauvegarde créée — tu peux restaurer à tout moment.</div>
      <button id="restore" class="ghost" style="margin-top:10px">Restaurer les anciens réglages</button>`;
  } else if (ready.length) {
    html += `<div style="margin-top:12px">
      <button class="primary" id="do-apply">Appliquer (jeu fermé)</button>
      <button id="restore" class="ghost" style="margin-left:8px">Restaurer</button></div>
      <p class="muted" style="font-size:12px;margin-top:6px">Ferme le jeu avant d'appliquer : il réécrirait sa config en quittant.</p>`;
  } else {
    html += `<p class="muted" style="margin-top:8px">Aucune modification applicable (réglages déjà au minimum, ou à changer dans le menu).</p>`;
  }
  $("opt-out").innerHTML = html;
  const ap = $("do-apply"); if (ap) ap.onclick = doApply;
  const rs = $("restore"); if (rs) rs.onclick = doRestore;
}

async function doApply() {
  const o = window.__opt;
  const out = $("opt-out");
  out.innerHTML = `<span class="spinner"></span> Application…`;
  try {
    const r = await postJson("/api/apply", { game: o.game, bottleneck: o.bottleneck, goal: o.goal, write: true });
    if (!r.ok) { out.innerHTML = `<span class="status-off">${r.message}</span>
      <button id="restore" class="ghost" style="margin-top:8px">Restaurer</button>`;
      $("restore").onclick = doRestore; return; }
    renderPlan(r, true);
  } catch (e) {
    out.innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

async function doRestore() {
  const o = window.__opt;
  const out = $("opt-out");
  out.innerHTML = `<span class="spinner"></span> Restauration…`;
  try {
    const r = await postJson("/api/restore", { game: o.game });
    out.innerHTML = r.ok ? `<span class="status-ok">✓ ${r.message}</span>` : `<span class="status-off">${r.message}</span>`;
  } catch (e) {
    out.innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

// ----- Tools -----------------------------------------------------------------
async function loadTools() {
  try {
    const t = await getJson("/api/tools");
    const pm = t.presentMon;
    let html;
    if (pm.installed) {
      html = `<div><span class="status-ok">✓ PresentMon installé</span>` +
        `<div class="muted" style="font-size:12px;margin-top:4px">${pm.path}</div></div>`;
    } else if (!t.platform.isWindows) {
      html = `<span class="status-off">PresentMon est un outil Windows (mesure des FPS réels).</span>`;
    } else {
      html = `<p class="muted">PresentMon (Intel) mesure les vrais FPS sans injection. Installe-le pour l'analyse d'écart FPS.</p>
        <button class="primary" id="install-pm">Télécharger et installer PresentMon</button>`;
    }
    $("tools").innerHTML = html;
    const btn = $("install-pm");
    if (btn) btn.onclick = installPresentMon;
  } catch (e) {
    $("tools").innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

async function installPresentMon() {
  const btn = $("install-pm");
  btn.disabled = true;
  btn.innerHTML = `<span class="spinner"></span> Téléchargement…`;
  try {
    const r = await postJson("/api/tools/presentmon");
    if (r.success) {
      $("tools").innerHTML = `<span class="status-ok">✓ ${r.message}</span>`;
    } else {
      $("tools").innerHTML = `<span class="status-off">${r.message}</span>
        <button class="ghost" id="retry-pm" style="margin-top:10px">Réessayer</button>`;
      $("retry-pm").onclick = loadTools;
    }
  } catch (e) {
    $("tools").innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
  }
}

// ----- Quit ------------------------------------------------------------------
$("quit").onclick = async () => {
  try { await postJson("/api/shutdown"); } catch { /* server stops */ }
  document.body.innerHTML =
    `<main><section class="card"><h2>Perfoptimize arrêté</h2>
     <p class="muted">Tu peux fermer cet onglet.</p></section></main>`;
};

$("run").onclick = runMonitor;

// ----- Tabs ------------------------------------------------------------------
let systemLoaded = false;
document.querySelectorAll(".tab").forEach(tab => {
  tab.onclick = () => {
    document.querySelectorAll(".tab").forEach(t => t.classList.remove("active"));
    tab.classList.add("active");
    const target = tab.dataset.tab;
    $("panel-games").classList.toggle("hidden", target !== "games");
    $("panel-system").classList.toggle("hidden", target !== "system");
    // The system tab does its heavy scan lazily, the first time it's opened.
    if (target === "system" && !systemLoaded) {
      systemLoaded = true;
      loadHardware();
      loadSystem();
      loadTools();
    }
  };
});

async function loadVersion() {
  try {
    const v = await getJson("/api/version");
    $("version").textContent = `· v${v.version}`;
  } catch { /* non-essential */ }
}

$("scan-library").onclick = scanLibrary;

loadGames();
loadVersion();
