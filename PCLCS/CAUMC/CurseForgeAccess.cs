using System.Net;
using System.Net.Http;

namespace PCLCS.CAUMC;

public static class CurseForgeAccess {
    private static readonly HttpClient AuthenticatedClient = new(new HttpClientHandler {
        AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    }) { Timeout = Timeout.InfiniteTimeSpan };
    public static bool Trusted(Uri? uri) => uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 && new[] { "api.curseforge.com", "edge.forgecdn.net", "mediafilez.forgecdn.net" }
            .Contains(uri.Host.TrimEnd('.'), StringComparer.OrdinalIgnoreCase);
    public static void Sign(HttpRequestMessage request, string key) {
        if (!Trusted(request.RequestUri)) return;
        request.Headers.Remove("x-api-key");
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.TryAddWithoutValidation("x-api-key", key);
    }
    public static async Task<HttpResponseMessage> SendAsync(HttpClient ordinary, HttpRequestMessage request,
        string key, CancellationToken token, HttpClient? authenticated = null) {
        if (!Trusted(request.RequestUri) || string.IsNullOrEmpty(key))
            return await ordinary.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        var client = authenticated ?? AuthenticatedClient;
        var method = request.Method;
        var content = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var address = request.RequestUri!;
        for (var redirects = 0; redirects <= 10; redirects++) {
            var next = new HttpRequestMessage(method, address);
            foreach (var header in request.Headers.Where(h => !new[] { "x-api-key", "Host", "Authorization", "Proxy-Authorization" }.Contains(h.Key, StringComparer.OrdinalIgnoreCase)))
                next.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (content != null && method != HttpMethod.Get && method != HttpMethod.Head) {
                next.Content = new ByteArrayContent(content);
                foreach (var header in request.Content!.Headers) next.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            Sign(next, key);
            var response = await client.SendAsync(next, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.RequestMessage = next;
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location == null) return response;
            var location = new Uri(address, response.Headers.Location);
            var status = (int)response.StatusCode;
            response.Dispose(); next.Dispose();
            if (location.Scheme != Uri.UriSchemeHttps) throw new HttpRequestException("CurseForge 下载重定向未使用 HTTPS");
            if (status == 303 || ((status == 301 || status == 302) && method == HttpMethod.Post)) { method = HttpMethod.Get; content = null; }
            address = location;
        }
        throw new HttpRequestException("CurseForge 重定向次数过多");
    }
}
