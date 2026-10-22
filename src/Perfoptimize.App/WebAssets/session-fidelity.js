"use strict";

// The cockpit keeps one analysis result per browser tab in `state.lastReport`.
// Extend the existing apply context with that result's immutable session id so
// preview/apply cannot drift to another analysis of the same game in another
// tab or process. The backend still validates that the session belongs to the
// requested game and contains an exact frozen plan.
const baseApplyContext = applyContext;
applyContext = function () {
  return {
    ...baseApplyContext(),
    sessionId: state.lastReport && state.lastReport.sessionId != null
      ? state.lastReport.sessionId
      : null,
  };
};

// After an actual write, restore the exact backup paths returned by THAT apply
// operation. A preview/history restore intentionally keeps the older "latest
// known backup" behavior; an immediate rollback never does.
doRestore = async function () {
  state.applyMsg = "";
  const exact = state.applyPhase === "applied";
  const backups = exact && state.applyResult && Array.isArray(state.applyResult.backups)
    ? state.applyResult.backups
    : [];

  try {
    const r = await postJson("/api/restore", {
      game: state.lastReport.game,
      backups,
      exact,
    });
    if (!r.ok) {
      state.applyMsg = r.message;
    } else {
      state.applyPhase = "restored";
      state.applyMsg = r.message;
    }
  } catch (e) {
    state.applyMsg = e.message;
  }
  render();
};
