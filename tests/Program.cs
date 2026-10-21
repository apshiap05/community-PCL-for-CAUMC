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
        RunImportChecks();
        await RunPackUpdateChecks();
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
    private static async Task RunPackUpdateChecks() {
        var source = GuideCatalog.ForPost(5).Source;
        var snapshot = new ArticleSnapshot { Source = source.AbsoluteUri, Nodes = ArticleParser.Parse(
            "<div class='theme-doc-markdown'><p>整合包<a href='/a.zip'>A</a><a href='/a.zip#again'>重复 A</a></p>" +
            "<ul><li><a href='/b.ZIP?download=1'>B</a></li></ul><a href='/a.zip?revision=2'>C</a>" +
            "<a href='/a.zip.exe'>不是整合包</a></div>", source) };
        var links = GuidePackUpdates.Links(snapshot);
        Assert(links.Select(u => u.PathAndQuery).SequenceEqual(new[] { "/a.zip", "/b.ZIP?download=1", "/a.zip?revision=2" }), "启动检查按正文次序递归提取 ZIP、重复链接和锚点只匹配一次、保留不同查询链接");
        Assert(GuidePackUpdates.Links(new ArticleSnapshot { Source = GuideCatalog.ForPost(2).Source.AbsoluteUri, Nodes = snapshot.Nodes }).Count == 0,
            "启动检查仅匹配模组服指南，不扫描其他选项卡");
        var handler = new Handler();
        using var http = new HttpClient(handler);
        handler.Queue.Enqueue(request => {
            Assert(request.Method == HttpMethod.Head, "弹窗前只请求下载响应头");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentDisposition = new("attachment") { FileName = "fallback.zip", FileNameStar = "CAUMC 模组服 2.zip" };
            return response;
        });
        Assert(await GuidePackUpdates.FileNameAsync(http, links[0]) == "CAUMC 模组服 2.zip", "弹窗名称优先使用服务器提供的中文文件名");
        handler.Queue.Enqueue(_ => new(HttpStatusCode.MethodNotAllowed));
        handler.Queue.Enqueue(request => {
            Assert(request.Method == HttpMethod.Get, "服务器不支持 HEAD 时使用只读响应头的 GET");
            return new(HttpStatusCode.OK) { RequestMessage = new(HttpMethod.Get, "https://files.example/redirected.zip"), Content = new ByteArrayContent([]) };
        });
        Assert(await GuidePackUpdates.FileNameAsync(http, links[0]) == "redirected.zip", "无文件名响应头时采用最终重定向文件名");
        handler.Queue.Enqueue(_ => new(HttpStatusCode.ServiceUnavailable));
        Assert(await GuidePackUpdates.FileNameAsync(http, new Uri("https://files.example/%E6%96%B0%E5%8C%85.zip")) == "新包.zip", "文件名探测失败时回退到解码后的 ZIP 链接文件名");
        var imported = new HashSet<string>();
        var prompted = new List<string>();
        var installed = new List<string>();
        var inFlight = false;
        void Headers(int count) { for (var i = 0; i < count; i++) handler.Queue.Enqueue(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }); }
        bool IsImported(Uri address) => imported.Contains(GuidePackUpdates.Key(address));
        void MarkImported(Uri address) => imported.Add(GuidePackUpdates.Key(address));
        Headers(3);
        await GuidePackUpdates.CheckAsync(snapshot, http, IsImported, update => {
            Assert(!inFlight, "多个整合包按顺序先询问，再依次导入");
            prompted.Add(update.Address.PathAndQuery);
            return Task.FromResult(update.Address.AbsolutePath != "/a.zip" || update.Address.Query.Length > 0);
        }, async update => {
            Assert(prompted.Count == 3, "先完成所有链接的询问，长时间下载不会推迟后续弹窗");
            inFlight = true;
            await Task.Delay(5);
            installed.Add(update.Address.PathAndQuery);
            inFlight = false;
            return update.Address.Query.Contains("revision=2"); // B simulates failure/cancellation.
        }, MarkImported);
        Assert(prompted.SequenceEqual(links.Select(u => u.PathAndQuery)), "相同链接一次、不同链接按顺序逐项询问");
        Assert(installed.SequenceEqual(new[] { "/b.ZIP?download=1", "/a.zip?revision=2" }), "选择暂不不触发下载，选择导入沿用安装回调");
        Assert(imported.SetEquals(new[] { GuidePackUpdates.Key(links[2]) }), "只有完整导入成功才记录，拒绝、失败或取消均不记为已导入");
        prompted.Clear();
        Headers(2);
        await GuidePackUpdates.CheckAsync(snapshot, http, IsImported, update => { prompted.Add(update.Address.PathAndQuery); return Task.FromResult(false); },
            _ => throw new Exception("暂不应触发下载"), MarkImported);
        Assert(prompted.SequenceEqual(new[] { "/a.zip", "/b.ZIP?download=1" }), "下次启动重试暂不和失败的整合包，成功导入的链接不再提醒");
        Assert(handler.Queue.Count == 0, "已成功导入的链接不再探测或下载");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try {
            await GuidePackUpdates.CheckAsync(snapshot, http, IsImported, _ => throw new Exception("已取消不应弹窗"), _ => throw new Exception("已取消不应安装"), MarkImported, cancelled.Token);
            throw new Exception("应响应取消");
        } catch (OperationCanceledException) { Assert(true, "启动检查可取消，不留下已导入记录"); }
    }

    private static void RunImportChecks() {
        var scratch = Path.Combine(Path.GetTempPath(), "caumc-import-" + Guid.NewGuid().ToString("N"));
        var launcher = Path.Combine(scratch, "旧 PCL");
        var game = Path.Combine(launcher, ".minecraft");
        var instance = Path.Combine(game, "versions", "Fabric 游戏");
        Directory.CreateDirectory(Path.Combine(instance, "saves", "我的世界"));
        Directory.CreateDirectory(Path.Combine(instance, "mods"));
        Directory.CreateDirectory(Path.Combine(instance, "PCL"));
        var samples = new Dictionary<string,string> {
            ["versions/Fabric 游戏/Fabric 游戏.json"] = "{\"id\":\"Fabric 游戏\",\"inheritsFrom\":\"1.21.1\"}",
            ["versions/Fabric 游戏/PCL/Setup.ini"] = "VersionArgumentIndieV2:True\n",
            ["versions/Fabric 游戏/saves/我的世界/level.dat"] = "world-data",
            ["versions/Fabric 游戏/mods/example.jar"] = new string('X', 2 * 1024 * 1024),
            ["options.txt"] = "key_key.sneak:key.keyboard.left.shift",
            ["libraries/example.jar"] = "library-data",
            ["assets/indexes/example.json"] = "{}"
        };
        try {
            foreach (var entry in samples) { var file = Path.Combine(game, entry.Key); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, entry.Value); }
            Directory.CreateDirectory(Path.Combine(launcher, "另一个游戏", "versions"));
            Assert(PclDataImport.Discover(launcher).Count == 2 && PclDataImport.Discover(game).Contains(game), "识别旧 PCL 多游戏目录及直接选择 .minecraft");
            var imports = Path.Combine(scratch, "新启动器", "ImportedGames");
            var target = PclDataImport.CopyGame(game, imports, CancellationToken.None);
            foreach (var entry in samples) Assert(File.ReadAllText(Path.Combine(target, entry.Key)) == entry.Value && File.ReadAllText(Path.Combine(game, entry.Key)) == entry.Value, "完整复制且保留原文件：" + entry.Key);
            var second = PclDataImport.CopyGame(game, imports, CancellationToken.None);
            Assert(second != target && Directory.Exists(target) && Directory.Exists(second), "重复和同名导入创建新目录，保留原实例 ID");
            using var cancelled = new CancellationTokenSource();
            try { PclDataImport.CopyGame(game, imports, cancelled.Token, new ImportProgress(_ => cancelled.Cancel())); throw new Exception("应被取消"); } catch (OperationCanceledException) { }
            Assert(Directory.GetDirectories(imports).Length == 2, "导入中取消清理临时文件，不产生半成品目录");
            try { PclDataImport.CopyGame(game, Path.Combine(game, "ImportedGames"), CancellationToken.None); throw new Exception("应拒绝嵌套导入"); } catch (IOException) { }
            Assert(File.Exists(Path.Combine(game, "options.txt")), "拒绝递归复制，原游戏保留");
            var oldIni = Path.Combine(scratch, "old.ini"); var newIni = Path.Combine(scratch, "new.ini");
            File.WriteAllText(oldIni, "WindowWidth:800\nJavaPath:java.exe\n"); File.WriteAllText(newIni, "WindowWidth:1200\n");
            PclDataImport.MergeIni(oldIni, newIni);
            Assert(File.ReadAllText(newIni).Contains("WindowWidth:1200") && File.ReadAllText(newIni).Contains("JavaPath:java.exe"), "迁移设置只补缺失键，保留现有配置");
            Assert(File.ReadAllText(newIni + ".before-caumc-migration.bak").Contains("WindowWidth:1200"), "迁移设置保留原配置备份");
            foreach (var url in new[]{"https://doc.caumc.cn/a.zip", "https://files.example/包.ZIP?download=1#top", "https://files.example/a%2Ezip"})
                Assert(PclDataImport.IsPackLink(5, new Uri(url)), "模组服 ZIP 链接匹配：" + url);
            Assert(!PclDataImport.IsPackLink(2, new Uri("https://files.example/a.zip")) && !PclDataImport.IsPackLink(6, new Uri("https://files.example/a.zip")), "其他指南的 ZIP 链接保留普通打开方式");
            Assert(!PclDataImport.IsPackLink(5, new Uri("https://files.example/a.zip.exe")) && !PclDataImport.IsPackLink(5, new Uri("file:///C:/a.zip")), "不匹配伪后缀及本地文件链接");
            var versionRoot = Path.Combine(scratch, "version-names");
            Directory.CreateDirectory(Path.Combine(versionRoot, "社区包"));
            Assert(PclDataImport.UniqueInstanceName(versionRoot, "社区包") == "社区包 (2)", "自动安装同名整合包使用新版本名称，不覆盖已有游戏");
            Assert(PclDataImport.UniqueInstanceName(versionRoot, "CON") == "CAUMC 整合包" && PclDataImport.UniqueInstanceName(versionRoot, "../unsafe;!").IndexOfAny(new[]{'/', ';', '!'}) < 0, "自动安装版本名处理路径字符和保留名称");
        } finally { Directory.Delete(scratch, true); }
    }
    private sealed class ImportProgress(Action<PclImportProgress> action) : IProgress<PclImportProgress> {
        public void Report(PclImportProgress value) => action(value);
    }
    private sealed class Handler : HttpMessageHandler {
        public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Queue { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Queue.Dequeue()(request));
    }
}
