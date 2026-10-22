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
