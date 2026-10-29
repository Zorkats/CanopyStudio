using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace CanopyStudio.Core.Install;

// One package of a release, with the SHA-256 GitHub computed for it when it
// was uploaded.
public sealed record ReleaseAsset(PackageKind Kind, string Name, Uri Download, string Sha256, long Size);

// A package's latest release on GitHub.
public sealed record PackageRelease(string Tag, string Version, ReleaseAsset Asset);

public sealed record PackageCheck(PackageRelease? Release, string? Error)
{
    public bool Ok => Release is not null;
}

// The latest Canopy release, and VillageQoL's. VillageQoL is null when its
// repository has no release with a package that can be checked.
public sealed record CanopyRelease(PackageRelease Canopy, PackageRelease? VillageQoL);

public sealed record ReleaseCheck(CanopyRelease? Release, string? Error)
{
    public bool Ok => Release is not null;
}

public sealed record ReleaseDownload(CanopyPackage? Package, string? Error)
{
    public bool Ok => Package is not null;
}

// The latest releases on GitHub. Canopy and VillageQoL are released from
// their own repositories: Canopy-<version>.zip (the loader) from
// Zorkats/Canopy and VillageQoL-<version>.zip (the mod) from Zorkats/VillageQoL,
// each with its own version. A package is used only when GitHub lists its
// SHA-256, so every download is checked before any of it reaches the game
// folder.
public sealed class ReleaseFeed(
    HttpClient http,
    string canopyRepository = ReleaseFeed.CanopyRepository,
    string qolRepository = ReleaseFeed.VillageQoLRepository)
{
    public const string CanopyRepository = "Zorkats/Canopy";
    public const string VillageQoLRepository = "Zorkats/VillageQoL";

    // A package is a few megabytes; anything far larger is not one.
    private const long MaxPackageSize = 64L * 1024 * 1024;

    public static string AssetName(PackageKind kind, string version) => $"{kind}-{version}.zip";

    // Canopy is needed; without a VillageQoL release Canopy is still offered.
    public async Task<ReleaseCheck> LatestAsync(CancellationToken cancel = default)
    {
        Task<PackageCheck> canopy = LatestAsync(canopyRepository, PackageKind.Canopy, cancel);
        Task<PackageCheck> qol = LatestAsync(qolRepository, PackageKind.VillageQoL, cancel);
        await Task.WhenAll(canopy, qol);
        return canopy.Result.Release is { } release
            ? new(new CanopyRelease(release, qol.Result.Release), null)
            : new(null, canopy.Result.Error);
    }

    private async Task<PackageCheck> LatestAsync(string repository, PackageKind kind, CancellationToken cancel)
    {
        using HttpRequestMessage request = Request($"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancel);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new(null, $"No {kind} release is published yet.");
            }
            if (!response.IsSuccessStatusCode)
            {
                return new(null, $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            return Parse(await response.Content.ReadAsStringAsync(cancel), kind);
        }
        catch (HttpRequestException exception)
        {
            return new(null, $"GitHub could not be reached: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new(null, "GitHub did not answer in time.");
        }
    }

    // The package of kind in a GitHub "latest release" answer, or why it
    // cannot be installed from it.
    public static PackageCheck Parse(string json, PackageKind kind)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string tag = root.TryGetProperty("tag_name", out JsonElement tagValue) ? tagValue.GetString() ?? "" : "";
            if (tag.Length == 0)
            {
                return new(null, "GitHub's answer names no release.");
            }
            if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return new(null, $"Release {tag} has no files.");
            }
            string version = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
            (ReleaseAsset? asset, string? error) = Asset(assets, kind, tag, version);
            return asset is null ? new(null, error) : new(new PackageRelease(tag, version, asset), null);
        }
        catch (JsonException)
        {
            return new(null, "GitHub's answer could not be read.");
        }
    }

    // The package's SHA-256 checked, then the package read.
    public async Task<ReleaseDownload> DownloadAsync(ReleaseAsset asset, string version, CancellationToken cancel = default)
    {
        if (asset.Size > MaxPackageSize)
        {
            return new(null, $"{asset.Name} is too large to be a {asset.Kind} package.");
        }
        byte[] bytes;
        try
        {
            using HttpRequestMessage request = Request(asset.Download.AbsoluteUri);
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode)
            {
                return new(null, $"The download of {asset.Name} failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancel);
            using MemoryStream memory = new();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancel)) > 0)
            {
                if (memory.Length + read > MaxPackageSize)
                {
                    return new(null, $"{asset.Name} is too large to be a {asset.Kind} package.");
                }
                memory.Write(buffer, 0, read);
            }
            bytes = memory.ToArray();
        }
        catch (HttpRequestException exception)
        {
            return new(null, $"The download of {asset.Name} failed: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new(null, $"The download of {asset.Name} did not finish in time.");
        }
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != asset.Sha256)
        {
            return new(null, $"{asset.Name} does not match the SHA-256 GitHub lists for it, so it was not used.");
        }
        (CanopyPackage? package, string? error) = CanopyPackage.Read(bytes, asset.Kind, version);
        return new(package, error);
    }

    // The release's package of kind, or why it cannot be used.
    private static (ReleaseAsset? Asset, string? Error) Asset(JsonElement assets, PackageKind kind, string tag, string version)
    {
        string wanted = AssetName(kind, version);
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out JsonElement nameValue) ? nameValue.GetString() ?? "" : "";
            if (!name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string url = asset.TryGetProperty("browser_download_url", out JsonElement urlValue) ? urlValue.GetString() ?? "" : "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? download) || download.Scheme != Uri.UriSchemeHttps)
            {
                return (null, $"Release {tag} gives no download address for {name}.");
            }
            string digest = asset.TryGetProperty("digest", out JsonElement digestValue) &&
                            digestValue.ValueKind == JsonValueKind.String
                ? digestValue.GetString() ?? ""
                : "";
            if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 7 + 64)
            {
                return (null, $"Release {tag} does not list a SHA-256 for {name}, so it cannot be checked.");
            }
            long size = asset.TryGetProperty("size", out JsonElement sizeValue) && sizeValue.TryGetInt64(out long value) ? value : 0;
            return (new ReleaseAsset(kind, name, download, digest[7..].ToLowerInvariant(), size), null);
        }
        return (null, $"Release {tag} has no {wanted}.");
    }

    private static HttpRequestMessage Request(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        // GitHub refuses requests that do not name their program.
        request.Headers.UserAgent.ParseAdd("CanopyStudio");
        return request;
    }
}
