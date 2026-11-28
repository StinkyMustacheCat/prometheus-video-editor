using System.Diagnostics;
using Prometheus.Client.Services;

namespace Prometheus.UnitTests;

public class YouTubeDownloadPolicyTests
{
    [Fact]
    public async Task CookieFailureUsesTheSameLinkWithoutSessionAndPreservesSettings()
    {
        var saved = new WebDownloadSettings(CookieBrowser: "chrome", CookieProfile: "Default");
        var tried = new List<WebDownloadSettings>();
        var result = await YouTubeDownloadPolicy.RunAsync(saved, (settings, _, _) =>
        {
            tried.Add(settings);
            return settings.HasSession
                ? Task.FromException<string>(new WebDownloadFailure(WebFailureKind.CookiesUnreadable))
                : Task.FromResult("completed");
        }, CancellationToken.None);
        Assert.Equal("completed", result);
        Assert.Equal(new[] { saved, saved with { CookieBrowser = null, CookieProfile = null } }, tried);
        Assert.Equal("Default", saved.CookieProfile);
    }

    [Fact]
    public async Task AlternateOptionsComposeButNeverTryMoreThanThreeConfigurations()
    {
        var saved = new WebDownloadSettings(WithoutPoToken: false, CookieBrowser: "chrome");
        var attempts = new List<WebDownloadSettings>();
        await Assert.ThrowsAsync<WebDownloadFailure>(() =>
            YouTubeDownloadPolicy.RunAsync<string>(saved, (settings, _, _) =>
            {
                attempts.Add(settings);
                return Task.FromException<string>(new WebDownloadFailure(attempts.Count switch
                {
                    1 => WebFailureKind.PoToken,
                    2 => WebFailureKind.CookiesLocked,
                    _ => WebFailureKind.Authentication
                }));
            }, CancellationToken.None, () => Task.FromResult<WebBrowserProfiles.BrowserSelection?>(new("firefox", "Other"))));
        Assert.Equal(3, attempts.Count);
        Assert.True(attempts[1].WithoutPoToken);
        Assert.False(attempts[2].HasSession);
        Assert.True(attempts[2].WithoutPoToken);
        Assert.False(saved.WithoutPoToken);
    }

    [Fact]
    public async Task ASessionIsUsedOnlyAfterTheUserSuppliesOne()
    {
        var original = new WebDownloadSettings();
        int attempts = 0, prompts = 0;
        var result = await YouTubeDownloadPolicy.RunAsync(original, (settings, _, _) =>
        {
            attempts++;
            return settings.HasSession
                ? Task.FromResult(settings.CookieBrowser)
                : Task.FromException<string?>(new WebDownloadFailure(WebFailureKind.Authentication));
        }, CancellationToken.None, () =>
        {
            Assert.Equal(1, attempts);
            prompts++;
            return Task.FromResult<WebBrowserProfiles.BrowserSelection?>(new("firefox", "My Profile"));
        });
        Assert.Equal("firefox", result);
        Assert.Equal(2, attempts);
        Assert.Equal(1, prompts);
        Assert.False(original.HasSession);

        prompts = attempts = 0;
        await Assert.ThrowsAsync<WebDownloadFailure>(() => YouTubeDownloadPolicy.RunAsync(original, (_, _, _) =>
        {
            attempts++;
            return Task.FromException<bool>(new WebDownloadFailure(WebFailureKind.Authentication));
        }, CancellationToken.None, () =>
        {
            prompts++;
            return Task.FromResult<WebBrowserProfiles.BrowserSelection?>(null);
        }));
        Assert.Equal(1, attempts);
        Assert.Equal(1, prompts);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task TokenProviderOptInDoesNotModifyTheSavedPreference(bool optIn, int expected)
    {
        var original = new WebDownloadSettings(AllowPoTokenFallback: optIn);
        var settings = new List<WebDownloadSettings>();
        await Assert.ThrowsAsync<WebDownloadFailure>(() => YouTubeDownloadPolicy.RunAsync(original, (effective, _, _) =>
        {
            settings.Add(effective);
            return Task.FromException<bool>(new WebDownloadFailure(WebFailureKind.PoToken));
        }, CancellationToken.None));
        Assert.Equal(expected, settings.Count);
        Assert.True(settings[0].WithoutPoToken);
        if (optIn) Assert.False(settings[1].WithoutPoToken);
        Assert.True(original.WithoutPoToken);
    }

    [Fact]
    public async Task TemporaryNetworkFailureRetriesTheSameConfigurationOnlyOnce()
    {
        var counts = new List<int>();
        var delays = new List<TimeSpan>();
        await Assert.ThrowsAsync<WebDownloadFailure>(() => YouTubeDownloadPolicy.RunAsync(new(), (_, number, _) =>
        {
            counts.Add(number);
            return Task.FromException<bool>(new WebDownloadFailure(WebFailureKind.Network));
        }, CancellationToken.None, delay: (wait, _) => { delays.Add(wait); return Task.CompletedTask; }));
        Assert.Equal(new[] { 1, 1 }, counts);
        Assert.True(Assert.Single(delays) > TimeSpan.Zero);
    }

    [Fact]
    public async Task RateLimitWaitIsSharedAcrossJobsAndHonorsServerDeadline()
    {
        var now = new DateTimeOffset(2026, 11, 27, 20, 0, 0, TimeSpan.Zero);
        var delays = new List<TimeSpan>();
        var gate = new YouTubeRequestQueue(() => now, (delay, _) =>
        {
            delays.Add(delay);
            now += delay;
            return Task.CompletedTask;
        });
        var until = now.AddMinutes(3);
        await Assert.ThrowsAsync<WebDownloadFailure>(() => gate.RunAsync(
            () => Task.FromException<bool>(new WebDownloadFailure(WebFailureKind.RateLimit, retryAfter: until)), CancellationToken.None));
        var delayedForRate = false;
        await gate.RunAsync(() => { Assert.Equal(until, now); return Task.FromResult(true); }, CancellationToken.None,
            (_, limited) => delayedForRate = limited);
        await gate.RunAsync(() => Task.FromResult(true), CancellationToken.None);
        Assert.True(delayedForRate);
        Assert.Equal(new[] { TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(5) }, delays);
    }

    [Fact]
    public async Task VerificationWaitBlocksAllRequestsUntilUserResumes()
    {
        var now = DateTimeOffset.UtcNow;
        var gate = new YouTubeRequestQueue(() => now, (delay, _) => { now += delay; return Task.CompletedTask; });
        await Assert.ThrowsAsync<WebDownloadFailure>(() => gate.RunAsync(
            () => Task.FromException<bool>(new WebDownloadFailure(WebFailureKind.Verification)), CancellationToken.None));
        var called = false;
        var blocked = false;
        var pending = gate.RunAsync(() => { called = true; return Task.FromResult(true); }, CancellationToken.None,
            verificationWait: () => blocked = true);
        Assert.True(blocked);
        Assert.False(called);
        Assert.False(pending.IsCompleted);
        gate.ResumeAfterVerification();
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task CancelQueuedDownloadDoesNotSendRequestsOrReleaseVerification()
    {
        var gate = new YouTubeRequestQueue();
        await Assert.ThrowsAsync<WebDownloadFailure>(() => gate.RunAsync(
            () => Task.FromException<int>(new WebDownloadFailure(WebFailureKind.Verification)), CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        var pending = gate.RunAsync<int>(() => throw new InvalidOperationException(), cancellation.Token, verificationWait: () => { });
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var error = await Assert.ThrowsAsync<WebDownloadFailure>(() => gate.RunAsync(() => Task.FromResult(1), CancellationToken.None));
        Assert.Equal(WebFailureKind.Verification, error.Kind);
    }

    [Theory]
    [InlineData("ERROR: Sign in to confirm you’re not a bot", "Verification")]
    [InlineData("WARNING: Some formats require a PO Token\nERROR: HTTP Error 429: Too Many Requests", "RateLimit")]
    [InlineData("WARNING: Some formats require a PO Token\nERROR: HTTP Error 403: Forbidden", "Forbidden")]
    [InlineData("ERROR: Could not find chrome cookies database", "ProfileMissing")]
    [InlineData("ERROR: Could not copy Chrome cookie database", "CookiesLocked")]
    [InlineData("ERROR: Failed to decrypt with DPAPI", "CookiesUnreadable")]
    public void ClassifiesTerminalErrorsWithoutConfusingWarning(string diagnostic, string kind) =>
        Assert.Equal(kind, WebDownloadFailure.FromDiagnostics(diagnostic).Kind.ToString());

    [Fact]
    public void OperaGxProfilesAreDiscoveredAndUseTheOperaBackend()
    {
        var root = Path.Combine(Path.GetTempPath(), "prometheus-browser-" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(root, "Opera Software", "Opera GX Stable", "Default");
        var network = Path.Combine(profile, "Network");
        Directory.CreateDirectory(network);
        var placeholder = Path.Combine(network, "Cookies");
        File.WriteAllText(placeholder, "test only");
        try
        {
            var browser = Assert.Single(WebBrowserProfiles.Find(local: root, roaming: root));
            Assert.Equal("Opera GX", browser.Label);
            Assert.Equal(profile, Assert.Single(browser.Profiles).Directory);
            var start = new ProcessStartInfo("yt-dlp");
            new WebDownloadSettings(CookieBrowser: browser.Id, CookieProfile: profile)
                .Configure(start, new Uri("https://youtu.be/test"));
            Assert.Contains("opera:" + profile, start.ArgumentList);
        }
        finally
        {
            File.Delete(placeholder);
            Directory.Delete(network);
            Directory.Delete(profile);
            Directory.Delete(Path.GetDirectoryName(profile)!);
            Directory.Delete(Path.Combine(root, "Opera Software"));
            Directory.Delete(root);
        }
    }
}
