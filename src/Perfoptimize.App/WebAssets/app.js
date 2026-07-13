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
    const running = g.running
      ? `<span class="status-ok">▶ ${g.running.name}</span> <span class="muted">(pid ${g.running.processId})</span>`
      : `<span class="status-off">Aucun jeu connu en cours d'exécution</span>`;
    const known = g.known.map(k => `<span class="chip">${k.name}</span>`).join("");
    $("game").innerHTML =
      `<div style="font-size:16px;margin-bottom:10px">${running}</div>
       <div class="muted" style="font-size:12px;margin-bottom:6px">${g.known.length} jeux reconnus :</div>
       <div>${known}</div>
       <button class="ghost" id="refresh-game" style="margin-top:12px">Rafraîchir</button>`;
    $("refresh-game").onclick = loadGames;
  } catch (e) {
    $("game").innerHTML = `<span class="status-off">Erreur : ${e.message}</span>`;
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

  html += `<div style="margin:6px 0 14px">
      <span class="badge ${badgeClass}${suspected}">${b.type}</span>
    </div>
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

  if (r.recommendations.length) {
    html += `<h3 style="font-size:14px;margin:16px 0 4px">Recommandations</h3>`;
    for (const rec of r.recommendations) {
      const gain = rec.expectedGainPercent ? `<span class="g"> ~+${Math.round(rec.expectedGainPercent)}%</span>` : "";
      html += `<div class="reco"><div class="t">${rec.title}${gain}</div><div class="r">${rec.reason}</div></div>`;
    }
  }
  for (const note of r.notes) html += `<div class="note">${note}</div>`;

  $("results").innerHTML = html;
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

loadHardware();
loadGames();
loadTools();
