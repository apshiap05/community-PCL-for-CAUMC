using System.Net.Http;

namespace PCLCS.CAUMC;

public sealed class GuidePackUpdate(Uri address, string fileName) {
    public Uri Address { get; } = address;
    public string FileName { get; } = fileName;
}

public static class GuidePackUpdates {
    // Fragments only locate content; query parameters can identify different downloads.
    public static string Key(Uri address) => new UriBuilder(address) { Fragment = "" }.Uri.AbsoluteUri;

    public static IReadOnlyList<Uri> Links(ArticleSnapshot snapshot) {
        if (snapshot.Source != GuideCatalog.ForPost(5).Source.AbsoluteUri) return Array.Empty<Uri>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<Uri>();
        void Visit(IEnumerable<ArticleNode> nodes) {
            foreach (var node in nodes) {
                if (node.Kind == "link" && Uri.TryCreate(node.Url, UriKind.Absolute, out var address) &&
                    PclDataImport.IsPackLink(5, address) && string.IsNullOrEmpty(address.UserInfo) && seen.Add(Key(address)))
                    links.Add(new Uri(Key(address)));
                Visit(node.Children);
            }
        }
        Visit(snapshot.Nodes);
        return links;
    }

    public static async Task<string> FileNameAsync(HttpClient http, Uri address, CancellationToken token = default) {
        var fallback = NameFromPath(address);
        try {
            using var head = new HttpRequestMessage(HttpMethod.Head, address);
            using var response = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented) {
                using var get = new HttpRequestMessage(HttpMethod.Get, address);
                using var headers = await http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                return ResponseName(headers, fallback);
            }
            return ResponseName(response, fallback);
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) { return fallback; }
        catch (HttpRequestException) { return fallback; }
        catch (FormatException) { return fallback; }
    }

    private static string NameFromPath(Uri address) => Uri.UnescapeDataString(address.AbsolutePath.Split('/').Last());

    private static string ResponseName(HttpResponseMessage response, string fallback) {
        if (!response.IsSuccessStatusCode) return fallback;
        var disposition = response.Content?.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name)) {
            var finalAddress = response.RequestMessage?.RequestUri;
            return finalAddress is not null && PclDataImport.IsPackLink(5, finalAddress) ? NameFromPath(finalAddress) : fallback;
        }
        // Content-Disposition sometimes includes quotes or an entire local path.
        var fileName = name!.Trim('"').Replace('\\', '/').Split('/').Last();
        return string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
    }

    public static async Task CheckAsync(ArticleSnapshot snapshot, HttpClient http, Func<Uri, bool> imported,
        Func<GuidePackUpdate, Task<bool>> prompt, Func<GuidePackUpdate, Task<bool>> install,
        Action<Uri> markImported, CancellationToken token = default, Action<Uri>? selectPreferred = null) {
        var accepted = new List<GuidePackUpdate>();
        foreach (var address in Links(snapshot)) {
            token.ThrowIfCancellationRequested();
            if (imported(address)) continue;
            var update = new GuidePackUpdate(address, await FileNameAsync(http, address, token).ConfigureAwait(false));
            // A manual click may have finished while the metadata request was running.
            if (imported(address) || !await prompt(update).ConfigureAwait(false)) continue;
            accepted.Add(update);
        }
        // Ask all questions in article order, then serialize imports so an earlier
        // installation cannot change the game folder underneath the next one.
        Uri? firstSuccessful = null;
        foreach (var update in accepted) {
            token.ThrowIfCancellationRequested();
            if (imported(update.Address) || await install(update).ConfigureAwait(false)) {
                markImported(update.Address);
                firstSuccessful ??= update.Address;
            }
        }
        if (firstSuccessful is not null) selectPreferred?.Invoke(firstSuccessful);
    }
}
