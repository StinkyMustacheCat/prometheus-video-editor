using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Prometheus.Client.Services;

internal static class YouTubeDownloadPolicy
{
    internal static async Task<T> RunAsync<T>(WebDownloadSettings saved,
        Func<WebDownloadSettings, int, string, Task<T>> attempt, CancellationToken token,
        Func<Task<WebBrowserProfiles.BrowserSelection?>>? authorize = null, Action? onVerification = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        var settings = saved;
        var seen = new HashSet<WebDownloadSettings> { settings };
        var label = "Intento 1 · ajustes guardados";
        var networkRetried = false;
        var rateRetried = false;
        var sessionAsked = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return await attempt(settings, seen.Count, label); }
            catch (WebDownloadFailure error)
            {
                switch (error.Kind)
                {
                    case WebFailureKind.Verification when onVerification != null:
                        onVerification();
                        continue; // The shared request queue waits for the user before sending anything else.
                    case WebFailureKind.RateLimit when !rateRetried:
                        rateRetried = true;
                        continue; // The shared request queue enforces the deadline for all jobs.
                    case WebFailureKind.Network when !networkRetried:
                        networkRetried = true;
                        await delay(TimeSpan.FromSeconds(3), token); // The queue applies any longer server deadline.
                        continue;
                }

                if (seen.Count >= 3) throw;
                WebDownloadSettings? alternate = null;
                string? description = null;
                if (error.IsCookieFailure && settings.HasSession)
                {
                    alternate = settings with { CookieBrowser = null, CookieProfile = null, CookieFile = null };
                    description = "sin sesión";
                }
                else if (error.Kind == WebFailureKind.PoToken && (!settings.WithoutPoToken || saved.AllowPoTokenFallback))
                {
                    alternate = settings with { WithoutPoToken = !settings.WithoutPoToken };
                    description = alternate.WithoutPoToken ? "sin PO Token" : "con proveedor de PO Token";
                }
                else if (error.Kind == WebFailureKind.Authentication && authorize != null && !sessionAsked)
                {
                    sessionAsked = true;
                    var session = await authorize();
                    token.ThrowIfCancellationRequested();
                    if (session != null)
                    {
                        alternate = settings with { CookieBrowser = session.Id, CookieProfile = session.Profile,
                            CookieFile = null };
                        description = "sesión para este enlace";
                    }
                }
                if (alternate == null || !seen.Add(alternate)) throw;
                settings = alternate;
                label = $"Intento {seen.Count} · {description}";
            }
        }
    }
}
