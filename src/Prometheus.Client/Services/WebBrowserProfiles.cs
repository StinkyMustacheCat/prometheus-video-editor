using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Prometheus.Client.Services;

internal static class WebBrowserProfiles
{
    internal sealed record BrowserSelection(string Id, string? Profile);
    internal sealed record Profile(string Name, string Directory);
    internal sealed record Browser(string Id, string Label, IReadOnlyList<Profile> Profiles);
    private sealed record BrowserLocation(string Id, string Label, string RelativePath, bool Roaming = false);

    private static readonly BrowserLocation[] Locations =
    {
        new("chrome", "Chrome", "Google/Chrome/User Data"),
        new("edge", "Edge", "Microsoft/Edge/User Data"),
        new("firefox", "Firefox", "Mozilla/Firefox/Profiles", true),
        new("opera", "Opera", "Opera Software/Opera Stable", true),
        new("opera-gx", "Opera GX", "Opera Software/Opera GX Stable", true),
        new("brave", "Brave", "BraveSoftware/Brave-Browser/User Data"),
        new("vivaldi", "Vivaldi", "Vivaldi/User Data"),
        new("chromium", "Chromium", "Chromium/User Data"),
        new("whale", "Whale", "Naver/Naver Whale/User Data")
    };

    internal static IReadOnlyList<Browser> Find(string? configured = null, string? local = null, string? roaming = null)
    {
        local ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        roaming ??= Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var result = new List<Browser>();
        foreach (var entry in Locations)
        {
            var root = Path.GetFullPath(Path.Combine(entry.Roaming ? roaming : local, entry.RelativePath));
            if (!Directory.Exists(root) && !entry.Id.Equals(configured, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new(entry.Id, entry.Label, Profiles(root)));
        }
        return result;
    }

    internal static string? Specifier(string? browser, string? profile)
    {
        var id = Locations.FirstOrDefault(location => location.Id.Equals(browser, StringComparison.OrdinalIgnoreCase))?.Id;
        if (id == null) return null;
        if (id == "opera-gx")
        {
            profile = string.IsNullOrWhiteSpace(profile)
                ? Find(id).FirstOrDefault(item => item.Id == id)?.Profiles.FirstOrDefault()?.Directory
                : profile;
            if (string.IsNullOrWhiteSpace(profile)) return null;
            id = "opera";
        }
        return string.IsNullOrWhiteSpace(profile) ? id : id + ":" + profile.Trim();
    }

    private static IReadOnlyList<Profile> Profiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return Array.Empty<Profile>();
            return Directory.EnumerateDirectories(directory)
                .Where(path => Path.GetFileName(path) is not ("Guest Profile" or "System Profile"))
                .Prepend(directory)
                .Where(path => File.Exists(Path.Combine(path, "Network", "Cookies")) ||
                    File.Exists(Path.Combine(path, "Cookies")) ||
                    File.Exists(Path.Combine(path, "cookies.sqlite")))
                .Select(path => new Profile(path == directory ? "Perfil principal" : Path.GetFileName(path), path))
                .OrderByDescending(profile => profile.Name is "Default" or "Perfil principal")
                .ThenBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (IOException) { return Array.Empty<Profile>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<Profile>(); }
    }
}
