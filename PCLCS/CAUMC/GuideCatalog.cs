namespace PCLCS.CAUMC;

public sealed class GuideDefinition {
    public int PostId { get; }
    public string Title { get; }
    public Uri Source { get; }
    public string CacheFileName => PostId == 2 ? "article.json" : $"article-{PostId}.json";
    internal GuideDefinition(int postId, string title) {
        PostId = postId;
        Title = title;
        Source = new Uri($"https://doc.caumc.cn/?post={postId}");
    }
}

public static class GuideCatalog {
    public static IReadOnlyList<GuideDefinition> All { get; } = Array.AsReadOnly(new[] {
        new GuideDefinition(2, "主服务器指南"),
        new GuideDefinition(5, "模组服指南"),
        new GuideDefinition(6, "皮肤站指南")
    });

    public static GuideDefinition ForPost(int postId) => All.FirstOrDefault(g => g.PostId == postId)
        ?? throw new ArgumentOutOfRangeException(nameof(postId));

    /// <summary>Only the three guide articles are internal navigation; other links retain normal browser behavior.</summary>
    public static int? ResolvePostId(string href, Uri source) {
        var safe = ArticleParser.SafeUrl(href, source);
        if (string.IsNullOrEmpty(safe)) return null;
        var uri = new Uri(safe);
        if (!uri.IsDefaultPort || !uri.Host.Equals("doc.caumc.cn", StringComparison.OrdinalIgnoreCase) ||
            !(uri.AbsolutePath == "/" || uri.AbsolutePath.Equals("/index.php", StringComparison.OrdinalIgnoreCase))) return null;
        try {
            var values = uri.Query.TrimStart('?').Split('&').Select(part => part.Split(new[] { '=' }, 2))
                .Where(pair => Uri.UnescapeDataString(pair[0]).Equals("post", StringComparison.OrdinalIgnoreCase)).ToList();
            if (values.Count != 1 || values[0].Length != 2 ||
                !int.TryParse(Uri.UnescapeDataString(values[0][1]), out int id)) return null;
            return All.Any(g => g.PostId == id) ? id : null;
        } catch (UriFormatException) { return null; }
    }
}
