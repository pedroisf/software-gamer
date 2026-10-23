using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SoftwareGamer.Core;

public static class AppRelease
{
    public static Version Version { get; } = Normalize(typeof(AppRelease).Assembly.GetName().Version!);
    public const string RepositoryUrl = "https://github.com/pedroisf/software-gamer";
    public const string FeedUrl = "https://api.github.com/repos/pedroisf/software-gamer/releases?per_page=100";
    private static Version Normalize(Version value) => new(value.Major, value.Minor, value.Build);
}

public sealed record AvailableUpdate(Version Version, Uri Page, bool IsPrerelease);

public sealed class UpdateClient(HttpClient http)
{
    private const long MaxResponseBytes = 4 * 1024 * 1024;

    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        cancellation = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, AppRelease.FeedUrl);
        request.Headers.UserAgent.ParseAdd($"SoftwareGamer/{current.ToString(3)}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("Resposta de atualização muito grande.");
        // Bound the streamed body as well; Content-Length is optional.
        await response.Content.LoadIntoBufferAsync(MaxResponseBytes, cancellation);
        return FindNewer(await response.Content.ReadAsStringAsync(cancellation), current);
    }

    public static AvailableUpdate? FindNewer(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Lista de versões inválida.");
        AvailableUpdate? latest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object ||
                !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !release.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String) continue;
            var tag = tagValue.GetString()!;
            var number = tag.StartsWith('v') ? tag[1..] : tag;
            if (number.Split('.').Length != 3 || !Version.TryParse(number, out var version) || version.Build < 0 || version <= current) continue;
            // Stable versions never offer a preview. The current 0.x line is a preview channel.
            if (prerelease.GetBoolean() && current.Major > 0) continue;
            // Only offer a release once its Windows installer is available in this repository.
            var expectedAsset = $"{AppRelease.RepositoryUrl}/releases/download/{tag}/SoftwareGamer-Setup-x64.exe";
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
                !assets.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.Object &&
                    a.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "SoftwareGamer-Setup-x64.exe" &&
                    a.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "uploaded" &&
                    a.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String && url.GetString() == expectedAsset)) continue;
            // Derive the URL from the fixed repository; never open arbitrary feed-provided URLs.
            if (latest is null || version > latest.Version)
                latest = new(version, new Uri($"{AppRelease.RepositoryUrl}/releases/tag/{tag}"), prerelease.GetBoolean());
        }
        return latest;
    }
}
