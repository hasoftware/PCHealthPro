using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace PCHealthPro.App.Services;

internal sealed record ReleaseAsset(string Name, long Size, string? Digest);
internal sealed record GitRelease([property: JsonPropertyName("tag_name")] string Tag, ReleaseAsset[] Assets);
internal sealed record UpdatePackage(string Tag, ReleaseAsset Asset, bool Installed);

internal static class ReleaseUpdates
{
    private const string Repo = "hasoftware/PCHealthPro";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static ReleaseUpdates() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("PCHealthPro-Update/1.0");

    internal static async Task<UpdatePackage?> CheckAsync(CancellationToken token)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<GitRelease>(cancellationToken: token);
        if (release?.Tag is not { } tag || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
            version.Build < 0 || version.ToString(3) != tag[1..]) return null;
        var current = typeof(ReleaseUpdates).Assembly.GetName().Version!;
        if (version <= new Version(current.Major, current.Minor, current.Build)) return null;
        var installedPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\HASOFTWARE\PCHealthPro\Installer", "Path", null) as string;
        bool installed = installedPath != null && string.Equals(Path.TrimEndingDirectorySeparator(installedPath),
            Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase);
        string name = $"PCHealthPro-{version}-win-x64.{(installed ? "msi" : "zip")}";
        var asset = release.Assets.FirstOrDefault(a => a.Name == name && a.Size > 0);
        if (asset?.Digest is not { Length: 71 } digest || !digest.StartsWith("sha256:") || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException("Release asset or SHA-256 unavailable.");
        return new(tag, asset, installed);
    }

    internal static async Task ApplyAsync(UpdatePackage update, CancellationToken token)
    {
        var cache = Directory.CreateTempSubdirectory("PCHealthPro-update-");
        var download = Path.Combine(cache.FullName, update.Asset.Name);
        try
        {
            using var response = await Http.GetAsync($"https://github.com/{Repo}/releases/download/{update.Tag}/{update.Asset.Name}",
                HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var file = new FileStream(download, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                await response.Content.CopyToAsync(file, token);
                if (file.Length != update.Asset.Size) throw new InvalidDataException("Incomplete download.");
                file.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
                if (!hash.Equals(update.Asset.Digest![7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 mismatch.");
            }
            token.ThrowIfCancellationRequested();
            if (update.Installed)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HASOFTWARE", "PCHealthPro", "updates");
                Directory.CreateDirectory(dir);
                var cached = Path.Combine(dir, update.Asset.Name);
                File.Move(download, cached, true);
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe")) { UseShellExecute = false };
                start.ArgumentList.Add("/i"); start.ArgumentList.Add(cached);
                using var installer = Process.Start(start) ?? throw new IOException();
            }
            else
            {
                string current = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
                string parent = Directory.GetParent(current)?.FullName ?? current;
                string destination = Path.Combine(parent, $"PCHealthPro-{update.Tag}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(destination);
                try
                {
                    await Task.Run(() => ZipFile.ExtractToDirectory(download, destination), token);
                    token.ThrowIfCancellationRequested();
                    using var next = Process.Start(new ProcessStartInfo(Path.Combine(destination, "PCHealthPro.App.exe"))
                        { WorkingDirectory = destination, UseShellExecute = false }) ?? throw new IOException();
                }
                catch
                {
                    Directory.Delete(destination, true);
                    throw;
                }
            }
        }
        finally { File.Delete(download); cache.Delete(); }
    }
}
