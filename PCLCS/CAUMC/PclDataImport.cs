
namespace PCLCS.CAUMC;

public sealed class PclImportProgress
{
    public long CompletedBytes { get; init; }
    public long TotalBytes { get; init; }
    public string CurrentFile { get; init; } = "";
}

public static class PclDataImport
{
    public static IReadOnlyList<string> Discover(string selectedDirectory)
    {
        var root = Path.GetFullPath(selectedDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (Directory.Exists(Path.Combine(root, "versions"))) return new[] { root };
        var candidates = new List<string> { root, Path.Combine(root, ".minecraft"), Path.Combine(root, "CAUMC", ".minecraft") };
        foreach (var child in Directory.GetDirectories(root).Where(p => !IsLink(p)))
        {
            candidates.Add(child);
            candidates.Add(Path.Combine(child, ".minecraft"));
        }
        return candidates.Where(p => Directory.Exists(Path.Combine(p, "versions")))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Copy into a fresh folder, then publish it atomically. Existing instances,
    // world saves and configuration are never merged or replaced in place.
    public static string CopyGame(string sourceDirectory, string importsDirectory, CancellationToken token,
        IProgress<PclImportProgress>? progress = null, string? checkpointId = null)
    {
        var source = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var imports = Path.GetFullPath(importsDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (ContainsPath(source, imports) || ContainsPath(imports, source))
            throw new IOException("源目录与导入目录不能互相包含。请使用另一个启动器文件夹。");
        if (!Directory.Exists(Path.Combine(source, "versions"))) throw new IOException("未找到游戏版本目录。");
        var files = EnumerateFiles(source, token).ToList();
        var total = files.Sum(f => new FileInfo(f).Length);
        Directory.CreateDirectory(imports);
        var name = new DirectoryInfo(source).Name;
        if (name == ".minecraft") name = new DirectoryInfo(source).Parent!.Name;
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        var destination = Path.Combine(imports, name);
        for (var i = 2; Directory.Exists(destination) || File.Exists(destination); i++) destination = Path.Combine(imports, name + " (" + i + ")");
        if (checkpointId != null && !Guid.TryParseExact(checkpointId, "N", out _)) throw new ArgumentException("无效的导入恢复编号");
        var staging = Path.Combine(imports, ".import-" + (checkpointId ?? Guid.NewGuid().ToString("N")));
        var resume = Path.Combine(staging, ".resume");
        long copied = 0;
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, ".minecraft", "versions"));
            if (checkpointId != null) Directory.CreateDirectory(resume);
            var buffer = new byte[1024 * 1024];
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var relative = file.Substring(source.Length + 1);
                var target = Path.Combine(staging, ".minecraft", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var sourceInfo = new FileInfo(file);
                long offset = 0;
                if (checkpointId != null) {
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    var key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(relative))).Replace("-", "");
                    var stampFile = Path.Combine(resume, key);
                    var stamp = sourceInfo.Length + ":" + sourceInfo.LastWriteTimeUtc.Ticks;
                    if (File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp && File.Exists(target) && new FileInfo(target).Length <= sourceInfo.Length)
                        offset = new FileInfo(target).Length;
                    File.WriteAllText(stampFile, stamp);
                }
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(target, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write))
                {
                    input.Position = offset; output.Position = offset; copied += offset;
                    int size;
                    while ((size = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, size);
                        copied += size;
                        progress?.Report(new() { CompletedBytes = copied, TotalBytes = total, CurrentFile = relative });
                    }
                }
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
            }
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(resume)) Directory.Delete(resume, true);
            if (checkpointId != null) {
                var expected = new HashSet<string>(files.Select(f => f.Substring(source.Length + 1)), StringComparer.OrdinalIgnoreCase);
                foreach (var stale in Directory.GetFiles(Path.Combine(staging, ".minecraft"), "*", SearchOption.AllDirectories))
                    if (!expected.Contains(stale.Substring(Path.Combine(staging, ".minecraft").Length + 1))) File.Delete(stale);
            }
            Directory.Move(staging, destination);
            return Path.Combine(destination, ".minecraft") + Path.DirectorySeparatorChar;
        }
        finally
        {
            if (checkpointId == null && Directory.Exists(staging) && ContainsPath(imports, staging)) Directory.Delete(staging, true);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsLink(directory)) throw new IOException("此目录使用了链接，请选择实际游戏目录：" + directory);
        foreach (var file in Directory.GetFiles(directory))
        {
            if (IsLink(file)) throw new IOException("此游戏包含链接文件，请先将其转换为实际文件：" + file);
            yield return file;
        }
        foreach (var child in Directory.GetDirectories(directory))
            foreach (var file in EnumerateFiles(child, token)) yield return file;
    }

    public static bool ContainsPath(string parent, string child) =>
        Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public static void MergeIni(string source, string destination)
    {
        if (!File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var result = File.Exists(destination) ? File.ReadAllLines(destination).ToList() : new List<string>();
        var keys = new HashSet<string>(result.Where(l => l.IndexOf(':') > 0).Select(l => l.Substring(0, l.IndexOf(':'))), StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(source))
        {
            var delimiter = line.IndexOf(':');
            if (delimiter > 0 && keys.Add(line.Substring(0, delimiter))) result.Add(line);
        }
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllLines(temporary, result, new UTF8Encoding(false));
        try
        {
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".before-caumc-migration.bak");
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool IsPackLink(int postId, Uri? address) => postId == 5 && address is { IsAbsoluteUri: true } &&
        (address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp) &&
        Uri.UnescapeDataString(address.AbsolutePath).EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(address.UserInfo);

    public static string UniqueInstanceName(string versionsDirectory, string desired)
    {
        var name = desired;
        foreach (var c in Path.GetInvalidFileNameChars().Concat(new[] { '!', ';' })) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (name.Length == 0 || Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "CAUMC 整合包";
        if (name.Length > 60) name = name.Substring(0, 60).TrimEnd('.');
        var result = name;
        for (var i = 2; Directory.Exists(Path.Combine(versionsDirectory, result)) || File.Exists(Path.Combine(versionsDirectory, result)); i++) result = name + " (" + i + ")";
        return result;
    }
}
