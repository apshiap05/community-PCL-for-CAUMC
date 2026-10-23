using System.IO.Compression;
using System.Security.Cryptography;

namespace PCLCS.CAUMC;

public sealed class GuidePackSignature {
    public string VersionName { get; set; } = "";
    public string ArchiveRoot { get; set; } = "";
    public Dictionary<string, string> Mods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Fingerprint { get; set; } = "";
}

public static class GuidePackIdentity {
    public static GuidePackSignature? Read(ZipArchive archive, CancellationToken token = default) {
        var entries = archive.Entries.ToList();
        var versions = new List<(GuidePackSignature Signature, int Priority)>();
        foreach (var entry in entries) {
            token.ThrowIfCancellationRequested();
            var match = Regex.Match(entry.FullName, @"^(?<root>(?:.*/)?)(?:versions/)(?<name>[^/]+)/\k<name>\.json$", RegexOptions.IgnoreCase);
            if (!match.Success || !Safe(entry.FullName)) continue;
            var root = match.Groups["root"].Value;
            var name = match.Groups["name"].Value;
            JObject json;
            using (var reader = new StreamReader(entry.Open())) json = JObject.Parse(reader.ReadToEnd());
            if ((string?)json["id"] != name) continue;
            var versionRoot = root + "versions/" + name + "/";
            var versionIni = Text(entries, versionRoot + "PCL/Setup.ini");
            var isolated = BoolSetting(versionIni, "VersionArgumentIndieV2");
            if (isolated is null) {
                var legacy = Setting(versionIni, "VersionArgumentIndie");
                if (legacy is "1" or "2") isolated = legacy == "1";
            }
            var hasLocalMods = entries.Any(e => e.FullName.StartsWith(versionRoot + "mods/", StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith("/"));
            var modsRoot = isolated == true || (isolated is null && hasLocalMods) ? versionRoot + "mods/" : root + "mods/";
            var mods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in entries.Where(e => e.FullName.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith("/"))) {
                token.ThrowIfCancellationRequested();
                if (!Safe(mod.FullName)) throw new InvalidDataException("整合包的 Mod 路径无效");
                var relative = mod.FullName.Substring(modsRoot.Length);
                if (mods.ContainsKey(relative)) throw new InvalidDataException("整合包包含重复 Mod 路径");
                using var stream = mod.Open();
                mods.Add(relative, Hash(stream, token));
            }
            var selected = Setting(Text(entries, root + "PCL.ini"), "Version");
            var modded = json.ToString().IndexOf("forge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                json.ToString().IndexOf("fabric-loader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                json.ToString().IndexOf("quilt-loader", StringComparison.OrdinalIgnoreCase) >= 0;
            versions.Add((new GuidePackSignature { VersionName = name, ArchiveRoot = root, Mods = mods, Fingerprint = Fingerprint(name, mods) },
                selected == name ? 0 : hasLocalMods ? 1 : modded ? 2 : 3));
        }
        return versions.OrderBy(v => v.Priority).Select(v => v.Signature).FirstOrDefault();
    }

    public static bool Matches(GuidePackSignature signature, string versionDirectory, string gameDirectory, bool defaultIsolated, CancellationToken token = default) {
        try {
            var version = new DirectoryInfo(versionDirectory);
            if (!version.Exists || !version.Name.Equals(signature.VersionName, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(version.FullName, version.Name + ".json")) || IsLink(version.FullName)) return false;
            var json = JObject.Parse(File.ReadAllText(Path.Combine(version.FullName, version.Name + ".json")));
            if (!string.Equals((string?)json["id"], signature.VersionName, StringComparison.OrdinalIgnoreCase)) return false;
            var ini = Path.Combine(version.FullName, "PCL", "Setup.ini");
            var contents = File.Exists(ini) ? File.ReadAllText(ini) : "";
            var isolated = BoolSetting(contents, "VersionArgumentIndieV2");
            if (isolated is null) {
                var legacy = Setting(contents, "VersionArgumentIndie");
                if (legacy is "1" or "2") isolated = legacy == "1";
            }
            var local = Path.Combine(version.FullName, "mods");
            if (isolated is null) isolated = (Directory.Exists(local) && Directory.EnumerateFileSystemEntries(local).Any()) ||
                (Directory.Exists(Path.Combine(version.FullName, "saves")) && Directory.EnumerateDirectories(Path.Combine(version.FullName, "saves")).Any()) || defaultIsolated;
            var directory = isolated == true ? local : Path.Combine(gameDirectory, "mods");
            var actual = ModFiles(directory, token).ToList();
            if (actual.Count != signature.Mods.Count) return false;
            foreach (var file in actual) {
                token.ThrowIfCancellationRequested();
                var relative = file.Substring(directory.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
                if (!signature.Mods.TryGetValue(relative, out var expected)) return false;
                using var stream = File.OpenRead(file);
                if (Hash(stream, token) != expected) return false;
            }
            return true;
        } catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    private static IEnumerable<string> ModFiles(string root, CancellationToken token) {
        if (!Directory.Exists(root)) yield break;
        token.ThrowIfCancellationRequested();
        if (IsLink(root)) throw new IOException("跳过链接形式的 Mod 目录");
        foreach (var file in Directory.EnumerateFiles(root)) {
            token.ThrowIfCancellationRequested();
            if (IsLink(file)) throw new IOException("跳过链接形式的 Mod 文件");
            yield return file;
        }
        foreach (var folder in Directory.EnumerateDirectories(root))
            foreach (var file in ModFiles(folder, token)) yield return file;
    }

    private static string Fingerprint(string name, Dictionary<string, string> mods) {
        var text = name.ToLowerInvariant() + "\n" + string.Join("\n", mods.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key.ToLowerInvariant() + ":" + p.Value));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return Hash(stream, CancellationToken.None);
    }
    private static string Hash(Stream stream, CancellationToken token) {
        using var sha = SHA256.Create();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) {
            token.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, count, buffer, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(sha.Hash!).Replace("-", "");
    }
    private static bool Safe(string path) => !path.Contains("\\") && !path.Contains(":") && !path.StartsWith("/") &&
        !path.Split('/').Any(p => p is "." or "..");
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static string Text(List<ZipArchiveEntry> entries, string path) {
        var entry = entries.FirstOrDefault(e => e.FullName.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return "";
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
    private static string? Setting(string ini, string key) => ini.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))?.Substring(key.Length + 1).Trim();
    private static bool? BoolSetting(string ini, string key) => bool.TryParse(Setting(ini, key), out var result) ? result : null;
}
