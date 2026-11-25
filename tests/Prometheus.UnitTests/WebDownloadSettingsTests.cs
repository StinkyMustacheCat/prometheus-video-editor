using System.Diagnostics;
using System.Security.Cryptography;
using Prometheus.Client.Services;
using Prometheus.Core.Model;
using Prometheus.Core.Storage;

namespace Prometheus.UnitTests;

public class WebDownloadSettingsTests
{
    [Fact]
    public void DefaultUsesNoAccountNoProvidersAndDoesNotForceYoutubeClient()
    {
        var start = new ProcessStartInfo("yt-dlp");
        using var lease = new WebDownloadSettings().Configure(start, new Uri("https://www.youtube.com/watch?v=test"), WebDownloadQuality.Light);
        Assert.Contains("--ignore-config", start.ArgumentList);
        Assert.Contains("--no-plugin-dirs", start.ArgumentList);
        Assert.Contains("bv*[height<=720]+ba/b[height<=720]", start.ArgumentList);
        Assert.DoesNotContain("--cookies-from-browser", start.ArgumentList);
        Assert.DoesNotContain("--cookies", start.ArgumentList);
        Assert.DoesNotContain(start.ArgumentList, a => a.Contains("player-client"));
    }

    [Fact]
    public void BrowserSessionIsOptionalAndAppliedOnlyToYoutube()
    {
        var settings = new WebDownloadSettings(CookieBrowser: "firefox", CookieProfile: "My Profile");
        var youtube = new ProcessStartInfo("yt-dlp");
        using var lease = settings.Configure(youtube, new Uri("https://youtu.be/test"));
        Assert.Contains("firefox:My Profile", youtube.ArgumentList);
        var elsewhere = new ProcessStartInfo("yt-dlp");
        using var other = settings.Configure(elsewhere, new Uri("https://example.com/video"));
        Assert.DoesNotContain("--cookies-from-browser", elsewhere.ArgumentList);
        Assert.DoesNotContain("--cookies", elsewhere.ArgumentList);
        Assert.False(WebDownloadSettings.IsYouTube(new Uri("https://youtube.com.example.org/watch?v=1")));
    }

    [Fact]
    public void CookieFileUsesTemporaryCopyAndNeverChangesOriginal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cookie-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "cookies.txt");
        File.WriteAllText(original, "# Netscape HTTP Cookie File\n.youtube.com\tTRUE\t/\tTRUE\t0\tfake_session\ttest_value\n");
        var hash = SHA256.HashData(File.ReadAllBytes(original));
        try
        {
            var start = new ProcessStartInfo("yt-dlp");
            var lease = new WebDownloadSettings(CookieFile: original).Configure(start, new Uri("https://www.youtube.com/watch?v=test"));
            Assert.NotNull(lease);
            var copy = start.ArgumentList[start.ArgumentList.IndexOf("--cookies") + 1];
            Assert.NotEqual(original, copy);
            Assert.True(File.Exists(copy));
            File.AppendAllText(copy, "# downloader update\n");
            lease.Dispose();
            Assert.False(File.Exists(copy));
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(original)));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void InvalidCookieExportFailsBeforeStartingDownloader()
    {
        var original = Path.GetTempFileName();
        try
        {
            File.WriteAllText(original, "not cookies");
            var start = new ProcessStartInfo("yt-dlp");
            Assert.Throws<InvalidOperationException>(() => new WebDownloadSettings(CookieFile: original)
                .Configure(start, new Uri("https://www.youtube.com/watch?v=test")));
            Assert.DoesNotContain("--cookies", start.ArgumentList);
        }
        finally { File.Delete(original); }
    }

    [Theory]
    [InlineData("Sign in to confirm your age. This video may be inappropriate", "restricción de edad")]
    [InlineData("Failed to decrypt with DPAPI", "Firefox")]
    [InlineData("Could not copy Chrome cookie database", "cookies del navegador")]
    [InlineData("HTTP Error 429: Too Many Requests", "limitó las solicitudes")]
    [InlineData("Sign in to confirm you're not a bot", "verificar la sesión")]
    [InlineData("Private video. Sign in if you've been granted access", "permiso")]
    [InlineData("Read timed out", "conexión")]
    [InlineData("Requested format is not available", "vista ligera")]
    public void FailureExplainsDistinctAction(string diagnostic, string expected) =>
        Assert.Contains(expected, WebDownloadSettings.ExplainFailure(diagnostic, WebDownloadQuality.Light));

    [Fact]
    public void ExistingPreferencesMigrateAndInvalidQualityFallsBack()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"Quality\":1,\"WithoutPoToken\":true,\"CookieBrowser\":\"edge\"}");
            var old = WebDownloadSettings.Load(path);
            Assert.Equal(WebDownloadQuality.Light, old.Quality);
            Assert.Equal("edge", old.CookieBrowser);
            Assert.False(old.PasteChooseRange);
            Assert.Null(old.CookieFile);
            File.WriteAllText(path, "{\"Quality\":90}");
            Assert.Equal(WebDownloadQuality.Complete, WebDownloadSettings.Load(path).Quality);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AssetPreferenceSurvivesProjectRoundTripAndOverridesChangedDefault()
    {
        var project = ProjectStore.CreateNew();
        project.Assets.Add(new Asset { Id = "asset", WebPreferredQuality = "light" });
        var saved = ProjectStore.Clone(project).Assets[0];
        var settings = new WebDownloadSettings(WebDownloadQuality.Complete);
        Assert.Equal(WebDownloadQuality.Light, settings.QualityFor(saved));
        Assert.Equal(WebDownloadQuality.Complete, settings.QualityFor(saved, WebDownloadQuality.Complete));
    }
}
