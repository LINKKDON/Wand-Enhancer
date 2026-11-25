using System;
using System.Collections.Generic;
using WandEnhancer.Core.Js;
using WandEnhancer.Models;

namespace WandEnhancer.Core
{
    /// <summary>
    /// The patch definitions. A fresh instance set is built per call so a previous
    /// run never leaves an Applied flag behind.
    /// </summary>
    internal sealed class EnhancerConfig
    {
        public sealed class PatchEntry
        {
            public string Name { get; set; }
            public string[] SearchHints { get; set; }
            public string[] CapabilityHints { get; set; }
            public string[] CandidateFileNames { get; set; }
            public bool IsOptional { get; set; }
            public Func<JsCursor, JsEdit[]> Locate { get; set; }
            public bool Applied { get; set; }
            public bool CapabilityDetected { get; set; }

            // Accounted for: applied, or the capability is absent so skipping is fine.
            // A detected capability that never applied is an unsupported build.
            public bool IsResolved => Applied || !CapabilityDetected;
        }

        public static Dictionary<EPatchType, PatchEntry[]> GetInstance()
        {
            return new Dictionary<EPatchType, PatchEntry[]>
            {
                [EPatchType.ActivatePro] = new[] { ProSubscriptionPatch() },
                [EPatchType.DisableUpdates] = new[] { DisableUpdatesPatch() },
                [EPatchType.DevToolsOnF12] = new[] { DevToolsPatch() },
                [EPatchType.DisableTelemetry] = new[]
                {
                    PerfTelemetryConfigPatch(),
                    PerfTelemetrySamplingPatch(),

                    // The analytics frame exists in both the app and overlay
                    // bundles, so two entries cover both. The second is optional:
                    // the overlay bundle may not exist, and a single-file fixture
                    // can only satisfy one entry.
                    AnalyticsFramePatch(),
                    AnalyticsFramePatch(isOptional: true),
                },

                // The remote panel is wired by RemotePanelInjector, not by JS patches.
                // Its bridge entries are not restored yet, so this stays empty.
                [EPatchType.RemoteWebPanelPreview] = new PatchEntry[0],
            };
        }

        private static PatchEntry ProSubscriptionPatch()
        {
            return new PatchEntry
            {
                Name = "Pro subscription",
                SearchHints = new[] { "ACTION_SET_USER_ACCOUNT" },
                CapabilityHints = new[] { "ACTION_SET_USER_ACCOUNT" },
                CandidateFileNames = null,
                IsOptional = false,
                Locate = cursor =>
                {
                    if (cursor.IndexOf("\"ACTION_SET_USER_ACCOUNT\"") < 0)
                    {
                        return null;
                    }

                    // Parameter names change with minification, so they are read
                    // out of the located function instead of being baked in.
                    const string comparison = @"JSON\.stringify\((?<account>[\w$]+)\)\s*===\s*JSON\.stringify\((?<state>[\w$]+)\.account\)";
                    string subscription = PatchPayload.Load("pro-subscription");
                    int searchFrom = 0;
                    while (true)
                    {
                        int stringify = cursor.IndexOf("JSON.stringify(", searchFrom);
                        if (stringify < 0)
                        {
                            throw new Exception("Account reducer found but the set-account case changed shape");
                        }

                        JsFunction setAccount = cursor.EnclosingFunction(stringify);
                        if (setAccount != null)
                        {
                            try
                            {
                                string account = setAccount.Resolve(comparison, "account");
                                string state = setAccount.Resolve(comparison, "state");

                                // Every client gate reads account.subscription, so injecting
                                // it here covers every fetch, login and refresh.
                                string body = PatchPayload.Load("pro-subscription-body", "account", account, "state", state, "subscription", subscription);
                                return new[] { setAccount.ReplaceBody(body) };
                            }
                            catch (Exception e) when (e.Message.StartsWith("Could not resolve", StringComparison.Ordinal))
                            {
                                // Not the set-account case; keep looking.
                            }
                        }

                        searchFrom = stringify + 1;
                    }
                },
            };
        }

        private static PatchEntry DisableUpdatesPatch()
        {
            return new PatchEntry
            {
                Name = "Disable updates",
                SearchHints = new[] { "ACTION_CHECK_FOR_UPDATE" },
                CapabilityHints = new[] { "ACTION_CHECK_FOR_UPDATE" },
                CandidateFileNames = new[] { "index.js" },
                IsOptional = false,
                Locate = cursor =>
                {
                    int open = cursor.FindCall("registerHandler", "\"ACTION_CHECK_FOR_UPDATE\"");
                    if (open < 0)
                    {
                        return null;
                    }

                    int close = cursor.MatchClose(open);
                    if (close < 0)
                    {
                        throw new Exception("Update handler found but its call never closes");
                    }

                    // The payload replaces the whole handler registration so the update
                    // check resolves to null instead of asking the feed for a version.
                    string payload = PatchPayload.Load("disable-updates");
                    return new[] { new JsEdit(open + 1, close, payload) };
                },
            };
        }

        private static PatchEntry DevToolsPatch()
        {
            return new PatchEntry
            {
                Name = "DevTools on F12",
                SearchHints = new[] { "require(\"electron\")" },
                CapabilityHints = new[] { "require(\"electron\")" },
                CandidateFileNames = new[] { "index.js" },
                IsOptional = false,
                Locate = cursor =>
                {
                    int anchor = cursor.IndexOf("require(\"electron\")");
                    if (anchor < 0)
                    {
                        return null;
                    }

                    int statementEnd = cursor.Text.IndexOf(';', anchor);
                    if (statementEnd < 0)
                    {
                        throw new Exception("Electron require found but its statement never ends");
                    }

                    // The payload is a sequence element, so its trailing comma is dropped.
                    // require("electron").app is used directly, not a minified alias.
                    string payload = PatchPayload.Load("devtools-f12", "app", "require(\"electron\").app").TrimEnd(',');
                    return new[] { new JsEdit(statementEnd + 1, statementEnd + 1, payload + ";") };
                },
            };
        }

        private static PatchEntry PerfTelemetryConfigPatch()
        {
            return new PatchEntry
            {
                Name = "Performance telemetry config",
                SearchHints = new[] { "ACTION_SET_PERFORMANCE_TELEMETRY_CONFIG" },
                CapabilityHints = new[] { "ACTION_SET_PERFORMANCE_TELEMETRY_CONFIG" },
                CandidateFileNames = new[] { "index.js" },
                IsOptional = false,
                Locate = cursor =>
                {
                    int anchor = cursor.IndexOf("\"ACTION_SET_PERFORMANCE_TELEMETRY_CONFIG\"");
                    if (anchor < 0)
                    {
                        return null;
                    }

                    JsFunction register = cursor.EnclosingFunction(anchor);
                    if (register == null)
                    {
                        throw new Exception("Telemetry config channel found but its registration changed shape");
                    }

                    // The setter already stops sampling and discards stored
                    // telemetry when disabled, so forcing the flag off kills it.
                    return new[] { register.ReplaceInBody(@"setPerformanceTelemetryConfig\((?<config>[\w$]+)\)", "setPerformanceTelemetryConfig({...${config},enabled:false})") };
                },
            };
        }

        private static PatchEntry PerfTelemetrySamplingPatch()
        {
            return new PatchEntry
            {
                Name = "Performance telemetry sampling",
                SearchHints = new[] { "isSamplingEnabled" },
                CapabilityHints = new[] { "isSamplingEnabled" },
                CandidateFileNames = new[] { "index.js" },
                IsOptional = false,
                Locate = cursor =>
                {
                    // FindFunction would also match a call site followed by a
                    // block, so locate the declaration by its keyword instead.
                    int declaration = cursor.IndexOf("function isSamplingEnabled");
                    if (declaration < 0)
                    {
                        return null;
                    }

                    JsFunction sampling = cursor.FindFunctionAfter(declaration);
                    if (sampling == null || sampling.Name != "isSamplingEnabled")
                    {
                        throw new Exception("Sampling gate found but its function changed shape");
                    }

                    // The collector and the config setter both consult this gate,
                    // so forcing it off stops everything, including sampling
                    // started from a stored config at launch.
                    return new[] { sampling.ReplaceBody("return false;") };
                },
            };
        }

        private static PatchEntry AnalyticsFramePatch(bool isOptional = false)
        {
            return new PatchEntry
            {
                Name = "Analytics frame",
                SearchHints = new[] { "wemod-analytics-frame" },
                CapabilityHints = new[] { "wemod-analytics-frame" },
                CandidateFileNames = null,
                IsOptional = isOptional,
                Locate = cursor =>
                {
                    int anchor = cursor.IndexOf("\"wemod-analytics-frame\"");
                    if (anchor < 0)
                    {
                        return null;
                    }

                    JsFunction creator = cursor.EnclosingFunction(anchor);
                    if (creator == null)
                    {
                        throw new Exception("Analytics frame found but its webview creator changed shape");
                    }

                    // The creator resolves its promise through a helper; resolve
                    // it as failed so the frame is never built and GTM never loads.
                    string resolve = creator.Resolve(@"this\.#[\w$]+\((?<resolve>[\w$]+)\)", "resolve");
                    return new[] { creator.ReplaceBody(resolve + "(!1);") };
                },
            };
        }
    }
}
