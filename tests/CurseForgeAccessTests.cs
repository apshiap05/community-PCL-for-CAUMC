using System.Net;
using System.Net.Http;
using PCLCS.CAUMC;

internal static class CurseForgeAccessTests {
    public static async Task Run() {
        const string key = "fixture-key-not-a-real-credential";
        foreach (var address in new[] { "https://api.curseforge.com/v1/mods", "https://edge.forgecdn.net/files/a.jar", "https://mediafilez.forgecdn.net/files/a.jar" }) {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            CurseForgeAccess.Sign(request, key);
            Check(request.Headers.GetValues("x-api-key").Single() == key, "官方 API 与文件 CDN 使用请求头认证");
        }
        foreach (var address in new[] { "http://api.curseforge.com/v1/mods", "https://api.curseforge.com:8443/", "https://user@api.curseforge.com/", "https://api.curseforge.com.other.invalid/", "https://mod.mcimirror.top/curseforge/" }) {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            CurseForgeAccess.Sign(request, key);
            Check(!request.Headers.Contains("x-api-key"), "非官方安全端点不携带项目凭据");
        }
        var count = 0;
        using var client = new HttpClient(new Handler(request => {
            count++;
            if (count == 1) {
                Check(request.Headers.Contains("x-api-key"), "初始官方请求携带凭据");
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://other.invalid/file.jar"); return redirect;
            }
            Check(!request.Headers.Contains("x-api-key") && request.RequestUri!.Host == "other.invalid", "跨站重定向移除项目凭据");
            return new(HttpStatusCode.OK) { Content = new StringContent("fixture") };
        }));
        using var initial = new HttpRequestMessage(HttpMethod.Get, "https://edge.forgecdn.net/files/a.jar");
        CurseForgeAccess.Sign(initial, key);
        using var response = await CurseForgeAccess.SendAsync(client, initial, key, default, client);
        Check(response.IsSuccessStatusCode && count == 2, "认证下载支持安全的跨站重定向");
    }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(callback(request));
    }
}
