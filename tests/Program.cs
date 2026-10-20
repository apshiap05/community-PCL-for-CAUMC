using System.Net;
using System.Net.Http;
using PCLCS.CAUMC;

internal static class Program {
    private static int checks;
    private static readonly string Html = "<html><nav>sidebar-not-content</nav><div class='theme-doc-markdown markdown'><h1>标题</h1><p>正文 <strong>加粗</strong> <a href='?post=5'>指南</a></p><ul><li>一<ul><li>二</li></ul></li></ul></div></html>";
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    private static IEnumerable<ArticleNode> All(IEnumerable<ArticleNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(All(n.Children)));
    private static HttpResponseMessage Response(string html, string etag = "\"v1\"") {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
        response.Headers.ETag = new(etag); return response;
    }
    private static async Task Run(string fixture) {
        var parsed = ArticleParser.Parse(Html, ArticleClient.Source);
        Assert(All(parsed).Any(n => n.Kind == "heading" && n.Level == 1), "保留标题层级");
        Assert(!All(parsed).Any(n => n.Text.Contains("sidebar-not-content")), "只提取正文");
        Assert(All(parsed).Any(n => n.Kind == "link" && n.Url == "https://doc.caumc.cn/?post=5"), "解析相对链接");
        Assert(All(parsed).Count(n => n.Kind == "list") == 2, "保留嵌套列表");
        foreach (var unsafeUrl in new[] { "javascript:alert(1)", "file:///C:/test.txt", "data:text/html,test", "https://user:pass@example.com" })
            Assert(ArticleParser.SafeUrl(unsafeUrl, ArticleClient.Source) == "", "拒绝危险链接类型 " + unsafeUrl.Split(':')[0]);
        var hostile = ArticleParser.Parse("<div class='theme-doc-markdown'><p>正常<script>do-not-execute</script><iframe>hidden</iframe></p><a href='javascript:test'>链接</a></div>", ArticleClient.Source);
        Assert(!All(hostile).Any(n => n.Text.Contains("do-not-execute") || n.Text.Contains("hidden")), "剔除脚本和嵌入内容");
        Assert(All(hostile).Where(n => n.Kind == "link").All(n => n.Url == ""), "危险链接保留文字但不能执行");
        if (File.Exists(fixture)) {
            var live = ArticleParser.Parse(File.ReadAllText(fixture), ArticleClient.Source);
            Assert(All(live).Any(n => n.Text.Contains("CAUMC")), "解析实际 CAUMC 网页快照");
            Assert(All(live).Any(n => n.Kind == "link" && n.Url.Contains("post=5")), "实际网页指南链接完整");
            Assert(All(live).Any(n => n.Kind == "quote"), "实际网页引用块完整");
        }
        var folder = Path.Combine(Path.GetTempPath(), "CAUMC-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var cache = Path.Combine(folder, "article.json");
        var handler = new Handler();
        using var http = new HttpClient(handler);
        var client = new ArticleClient(http, cache);
        handler.Queue.Enqueue(_ => Response(Html));
        await client.RefreshAsync();
        Assert(client.ReadCache()?.Nodes.Count > 0, "首次刷新成功后生成缓存");
        handler.Queue.Enqueue(request => {
            Assert(request.Headers.IfNoneMatch.Any(t => t.Tag == "\"v1\""), "再次进入发送 ETag 重验证");
            return new(HttpStatusCode.NotModified);
        });
        await client.RefreshAsync();
        var validCache = File.ReadAllText(cache);
        handler.Queue.Enqueue(_ => new(HttpStatusCode.ServiceUnavailable));
        try { await client.RefreshAsync(); throw new Exception("应报告网络失败"); } catch (HttpRequestException) { checks++; }
        Assert(File.ReadAllText(cache) == validCache, "网络错误不覆盖有效缓存");
        handler.Queue.Enqueue(_ => Response("<html>网站结构已变化</html>"));
        try { await client.RefreshAsync(); throw new Exception("应报告解析失败"); } catch (InvalidDataException) { checks++; }
        Assert(File.ReadAllText(cache) == validCache, "解析错误不覆盖有效缓存");
        handler.Queue.Enqueue(_ => Response(Html.Replace("正文", "网站已更新"), "\"v2\""));
        var changed = await client.RefreshAsync();
        Assert(All(changed.Nodes).Any(n => n.Text.Contains("网站已更新")), "刷新取得更新正文");
        File.WriteAllText(cache, "{broken");
        Assert(client.ReadCache() is null, "损坏缓存可恢复");
        Console.WriteLine($"RESULT {checks} checks passed");
        File.Delete(cache); Directory.Delete(folder);
        await RunGuides(Path.GetDirectoryName(fixture) ?? "");
    }

    private static async Task RunGuides(string fixtureFolder) {
        Assert(GuideCatalog.All.Select(g => g.Title).SequenceEqual(new[] { "主服务器指南", "模组服指南", "皮肤站指南" }), "三个指南的顺序和名称");
        foreach (var guide in GuideCatalog.All)
            Assert(GuideCatalog.ResolvePostId(guide.Source.AbsoluteUri, ArticleClient.Source) == guide.PostId, "站内指南路由 " + guide.PostId);
        Assert(GuideCatalog.ResolvePostId("?post=6#step", GuideCatalog.ForPost(5).Source) == 6, "相对指南链接和锚点切换");
        Assert(GuideCatalog.ResolvePostId("/index.php?ref=launcher&post=5", ArticleClient.Source) == 5, "支持指南链接的查询参数");
        Assert(GuideCatalog.ResolvePostId("http://doc.caumc.cn/?post=2", ArticleClient.Source) == 2, "兼容同站 HTTP 指南链接");
        foreach (string url in new[] {
            "https://doc.caumc.cn/?post=3", "https://example.com/?post=5", "https://doc.caumc.cn.evil.test/?post=5",
            "https://doc.caumc.cn:8443/?post=5", "https://doc.caumc.cn/other?post=5", "?post=5&post=6",
            "javascript:alert(1)", "https://name:pass@doc.caumc.cn/?post=5"
        }) Assert(GuideCatalog.ResolvePostId(url, ArticleClient.Source) is null, "不接管非指南或不明确的链接");

        var folder = Path.Combine(Path.GetTempPath(), "CAUMC-guides-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var handler = new Handler();
        using var http = new HttpClient(handler);
        foreach (var guide in GuideCatalog.All) {
            var client = new ArticleClient(http, Path.Combine(folder, guide.CacheFileName), guide.Source);
            handler.Queue.Enqueue(request => {
                Assert(request.RequestUri == guide.Source && !request.Headers.IfNoneMatch.Any(), "独立文章请求及初始缓存 " + guide.PostId);
                return Response(Html.Replace("正文", "article-" + guide.PostId), "\"guide-" + guide.PostId + "\"");
            });
            var snapshot = await client.RefreshAsync();
            Assert(snapshot.Source == guide.Source.AbsoluteUri && All(snapshot.Nodes).Any(n => n.Text.Contains("article-" + guide.PostId)), "指南正文与来源对应 " + guide.PostId);
            var wrong = new ArticleClient(http, Path.Combine(folder, guide.CacheFileName), GuideCatalog.ForPost(guide.PostId == 2 ? 5 : 2).Source);
            Assert(wrong.ReadCache() is null, "拒绝误用其他指南的缓存 " + guide.PostId);
        }
        var mods = new ArticleClient(http, Path.Combine(folder, GuideCatalog.ForPost(5).CacheFileName), GuideCatalog.ForPost(5).Source);
        handler.Queue.Enqueue(_ => new(HttpStatusCode.ServiceUnavailable));
        try { await mods.RefreshAsync(); throw new Exception("应报告网络失败"); } catch (HttpRequestException) { checks++; }
        Assert(All(mods.ReadCache()!.Nodes).Any(n => n.Text.Contains("article-5")), "模组指南失败仍保留自身缓存");
        Assert(new ArticleClient(http, Path.Combine(folder, GuideCatalog.ForPost(6).CacheFileName), GuideCatalog.ForPost(6).Source).ReadCache() is not null, "一个指南失败不影响其他缓存");
        foreach (var tuple in new[] { ("guide-mods.html", 5), ("guide-skin.html", 6) }) {
            var path = Path.Combine(fixtureFolder, tuple.Item1);
            if (!File.Exists(path)) continue;
            var nodes = ArticleParser.Parse(File.ReadAllText(path), GuideCatalog.ForPost(tuple.Item2).Source);
            Assert(All(nodes).Any(n => n.Kind == "heading"), "解析真实指南网页快照 " + tuple.Item2);
            Assert(All(nodes).Any(n => n.Kind == "link" && Uri.TryCreate(n.Url, UriKind.Absolute, out _)), "真实指南正文链接完整 " + tuple.Item2);
        }
        foreach (var path in Directory.GetFiles(folder)) File.Delete(path);
        Directory.Delete(folder);
        Console.WriteLine($"RESULT {checks} checks passed");
    }
    public static int Main(string[] args) {
        try {
            Run(args.Length > 0 ? args[0] : "").GetAwaiter().GetResult();
            if (args.Contains("--live")) {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                string folder = Path.Combine(Path.GetTempPath(), "CAUMC-live-" + Guid.NewGuid().ToString("N"));
                foreach (var guide in GuideCatalog.All) {
                    var client = new ArticleClient(http, Path.Combine(folder, guide.CacheFileName), guide.Source);
                    var live = client.RefreshAsync().GetAwaiter().GetResult();
                    Assert(live.Nodes.Count > 0, "真实 HTTPS 指南获取与解析 " + guide.PostId);
                    Assert(client.ReadCache()?.Source == guide.Source.AbsoluteUri, "真实 HTTPS 指南缓存可读 " + guide.PostId);
                    File.Delete(Path.Combine(folder, guide.CacheFileName));
                }
                Directory.Delete(folder);
                Console.WriteLine($"TOTAL {checks} checks passed");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private sealed class Handler : HttpMessageHandler {
        public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Queue { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Queue.Dequeue()(request));
    }
}
