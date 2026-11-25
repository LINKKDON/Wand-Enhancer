// Fixture for the patch-locator checks. Mirrors the shapes the patches anchor on
// in the real Wand bundles: the account reducer, the main-process update handler
// and the Electron require. It is built to index.js (prettified and minified)
// and every patch must locate on both variants.
const r = require("electron");

function expectUpdateFeedUrl(e, t) {
  return typeof e === "string" && e.startsWith("https://") ? t(e) : Promise.reject(new TypeError("Invalid update feed URL string."));
}
function checkForUpdate(e) { return Promise.resolve(e); }
function downloadAndApplyUpdate(e) { return Promise.resolve(e); }
const handlers = {};
function registerHandler(name, handler) { handlers[name] = handler; }
registerHandler("ACTION_CHECK_FOR_UPDATE", (e => expectUpdateFeedUrl(e, (e => checkForUpdate(e)))));
registerHandler("ACTION_APPLY_UPDATE", (e => expectUpdateFeedUrl(e, (e => downloadAndApplyUpdate(e)))));

const ACTION_SET_USER_ACCOUNT = "ACTION_SET_USER_ACCOUNT";
const ACTION_DESTROY_USER = "ACTION_DESTROY_USER";
function setBoosts(e, t) { return e.account ? { ...e, account: { ...e.account, boosts: t } } : { ...e }; }
const accountReducer = {
  [ACTION_SET_USER_ACCOUNT]: function (state, action) { return state.account && JSON.stringify(action) === JSON.stringify(state.account) ? state : { ...state, account: action }; },
  [ACTION_DESTROY_USER]: function (e) { return { ...e, token: null, account: null }; },
};

// Performance telemetry (main process). The config handler and the sampling
// gate are the two anchors the telemetry patches locate on.
let qe = null;
function isSamplingEnabled() { return qe?.enabled === true; }
function isTelemetryConfig(e) { return typeof e === "object" && e !== null && typeof e.enabled === "boolean"; }
function startSampling() {}
function stopSampling() {}
function setPerformanceTelemetryConfig(e) { qe = { ...e, updatedAt: Date.now() }; if (isSamplingEnabled()) { startSampling(); return; } stopSampling(); }
function registerPerformanceTelemetryChannels(e) {
  e("ACTION_SET_PERFORMANCE_TELEMETRY_CONFIG", (e => { if (!isTelemetryConfig(e)) { return Promise.reject(new TypeError("Invalid performance telemetry config.")); } setPerformanceTelemetryConfig(e); return Promise.resolve(); }));
}

// Analytics frame (renderer). The webview creator is what the analytics
// patch locates on, via the frame's element id.
class AnalyticsFrame {
  #w(e) { e(true); }
  #b(e) {
    const t = document.createElement("webview");
    t.id = "wemod-analytics-frame";
    this.#w(e);
  }
}
