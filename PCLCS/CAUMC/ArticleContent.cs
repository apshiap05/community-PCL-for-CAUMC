using HtmlAgilityPack;
using System.Net.Http;

namespace PCLCS.CAUMC;

/// <summary>A data-only document. Remote markup is never interpreted as XAML.</summary>
public sealed class ArticleNode {
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public string Url { get; set; } = "";
    public int Level { get; set; }
    public List<ArticleNode> Children { get; set; } = [];
}

public sealed class ArticleSnapshot {
    public string Source { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public string ETag { get; set; } = "";
    public DateTimeOffset? LastModified { get; set; }
    public List<ArticleNode> Nodes { get; set; } = [];
}

public static class ArticleParser {
    private static readonly HashSet<string> Ignored = ["script", "style", "iframe", "object", "embed", "form", "input", "button", "noscript", "svg"];
    public static List<ArticleNode> Parse(string html, Uri source) {
        if (html.Length > 2 * 1024 * 1024) throw new InvalidDataException("文章过大");
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var article = doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ', normalize-space(@class), ' '), ' theme-doc-markdown ')]")
            ?? doc.DocumentNode.SelectSingleNode("//article//*[contains(concat(' ', normalize-space(@class), ' '), ' entry-content ')]");
        if (article is null) throw new InvalidDataException("无法找到文章正文，网站结构可能已变化");
        var result = ParseChildren(article, source, 0);
        if (result.Count == 0 || string.Concat(article.Descendants().Where(n => n.NodeType == HtmlNodeType.Text && !Ignored.Contains(n.ParentNode.Name)).Select(n => n.InnerText)).Trim().Length < 2)
            throw new InvalidDataException("文章正文为空");
        return result;
    }

    public static string SafeUrl(string value, Uri source) {
        return Uri.TryCreate(source, HtmlEntity.DeEntitize(value), out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.AbsoluteUri : "";
    }

    private static List<ArticleNode> ParseChildren(HtmlNode parent, Uri source, int depth) {
        if (depth > 32) throw new InvalidDataException("文章嵌套过深");
        var result = new List<ArticleNode>();
        foreach (var node in parent.ChildNodes) {
            if (node.NodeType == HtmlNodeType.Comment || Ignored.Contains(node.Name)) continue;
            if (node.NodeType == HtmlNodeType.Text) {
                var text = System.Text.RegularExpressions.Regex.Replace(HtmlEntity.DeEntitize(node.InnerText), @"\s+", " ");
                if (text.Length > 0 && !string.IsNullOrWhiteSpace(text)) result.Add(new() { Text = text });
                else if (text.Length > 0 && result.Count > 0) result.Add(new() { Text = " " });
                continue;
            }
            string kind = node.Name switch {
                "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
                "p" => "paragraph", "ul" => "list", "ol" => "orderedlist", "li" => "item",
                "a" => "link", "strong" or "b" => "bold", "em" or "i" => "italic",
                "code" => "code", "pre" => "pre", "blockquote" => "quote", "br" => "break",
                "hr" => "rule", "img" => "image", "table" => "table", "tr" => "row",
                "td" or "th" => "cell", _ => "group"
            };
            var parsed = new ArticleNode { Kind = kind };
            if (kind == "heading") parsed.Level = node.Name[1] - '0';
            if (kind == "link") parsed.Url = SafeUrl(node.GetAttributeValue("href", ""), source);
            if (kind == "image") {
                parsed.Url = SafeUrl(node.GetAttributeValue("src", ""), source);
                parsed.Text = HtmlEntity.DeEntitize(node.GetAttributeValue("alt", "图片"));
            } else if (kind == "pre" || kind == "code") {
                parsed.Text = HtmlEntity.DeEntitize(node.InnerText);
            } else parsed.Children = ParseChildren(node, source, depth + 1);
            result.Add(parsed);
        }
        return result;
    }
}

/// <summary>Fetches a configured guide, revalidates on every request, and preserves valid cache on failure.</summary>
public sealed class ArticleClient {
    public static readonly Uri Source = GuideCatalog.ForPost(2).Source;
    private readonly HttpClient client;
    private readonly string cacheFile;
    private readonly Uri source;
    public ArticleClient(HttpClient client, string cacheFile, Uri? source = null) {
        this.client = client;
        this.cacheFile = cacheFile;
        this.source = source ?? Source;
        if (!GuideCatalog.All.Any(g => g.Source == this.source) && this.source != LauncherUpdates.Source) throw new ArgumentException("未配置的指南地址", nameof(source));
    }

    public ArticleSnapshot? ReadCache() {
        try {
            if (!File.Exists(cacheFile) || new FileInfo(cacheFile).Length > 4 * 1024 * 1024) return null;
            var snapshot = JsonConvert.DeserializeObject<ArticleSnapshot>(File.ReadAllText(cacheFile));
            return snapshot?.Source == source.AbsoluteUri && snapshot.Nodes.Count > 0 ? snapshot : null;
        } catch { return null; }
    }

    public async Task<ArticleSnapshot> RefreshAsync(CancellationToken cancellationToken = default) {
        var cached = ReadCache();
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.UserAgent.ParseAdd("PCL-for-CAUMC/0.1");
        request.Headers.CacheControl = new() { NoCache = true };
        if (!string.IsNullOrEmpty(cached?.ETag)) request.Headers.TryAddWithoutValidation("If-None-Match", cached!.ETag);
        if (cached?.LastModified is not null) request.Headers.IfModifiedSince = cached.LastModified;
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) {
            cached.CheckedAt = DateTimeOffset.Now;
            cancellationToken.ThrowIfCancellationRequested();
            WriteCache(cached);
            return cached;
        }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("文章过大");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false)) > 0) {
            if (buffer.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("文章过大");
            buffer.Write(bytes, 0, count);
        }
        var result = new ArticleSnapshot {
            Source = source.AbsoluteUri, CheckedAt = DateTimeOffset.Now,
            ETag = response.Headers.ETag?.ToString() ?? "", LastModified = response.Content.Headers.LastModified,
            Nodes = ArticleParser.Parse(Encoding.UTF8.GetString(buffer.ToArray()), source)
        };
        cancellationToken.ThrowIfCancellationRequested();
        WriteCache(result);
        return result;
    }

    private void WriteCache(ArticleSnapshot snapshot) {
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        var temp = cacheFile + ".new";
        File.WriteAllText(temp, JsonConvert.SerializeObject(snapshot), new UTF8Encoding(false));
        if (File.Exists(cacheFile)) File.Replace(temp, cacheFile, null); else File.Move(temp, cacheFile);
    }
}
