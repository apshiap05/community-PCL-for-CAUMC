using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace PCLCS.CAUMC;

public sealed class LauncherRelease {
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    [JsonIgnore] public Version Number => LauncherUpdates.VersionOf(Version);
}

public static class LauncherUpdates {
    public static readonly Uri Source = new("https://doc.caumc.cn/?post=7");
    private static readonly Regex Label = new(@"^PCL-for-CAUMC-(\d+\.\d+\.\d+(?:\.\d+)?)\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static Version VersionOf(string text) {
        var value = System.Version.Parse(text);
        return new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
    }
    public static string Display(string value) {
        var version = VersionOf(value);
        return version.Revision == 0 ? version.ToString(3) : version.ToString(4);
    }
    private static string Text(ArticleNode node) => node.Text + string.Concat(node.Children.Select(Text));
    public static LauncherRelease? Latest(ArticleSnapshot article, Version current) {
        if (article.Source != Source.AbsoluteUri) throw new InvalidDataException("更新文章来源不正确。");
        LauncherRelease? best = null;
        void Visit(IEnumerable<ArticleNode> nodes) {
            foreach (var node in nodes) {
                var match = Label.Match(Text(node).Trim());
                if (node.Kind == "link" && match.Success && Uri.TryCreate(node.Url, UriKind.Absolute, out var url) &&
                    url.Scheme == "https" && string.IsNullOrEmpty(url.UserInfo) && url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    System.Version.TryParse(match.Groups[1].Value, out _)) {
                    var version = VersionOf(match.Groups[1].Value);
                    if (version > current && (best is null || version > best.Number))
                        best = new() { Version = version.ToString(4), Url = new UriBuilder(url) { Fragment = "" }.Uri.AbsoluteUri };
                }
                Visit(node.Children);
            }
        }
        Visit(article.Nodes);
        return best;
    }
    public static string Hash(string file) {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
    public static void ValidateExecutable(string file, string expectedVersion) {
        using (var stream = File.OpenRead(file)) {
            using var reader = new BinaryReader(stream);
            if (stream.Length < 1024 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("下载文件不是有效的启动器 EXE。");
            stream.Position = 0x3c;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > stream.Length - 24) throw new InvalidDataException("启动器 EXE 文件已损坏。");
            stream.Position = offset;
            if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("启动器 EXE 文件已损坏。");
        }
        var info = FileVersionInfo.GetVersionInfo(file);
        if (!string.Equals(info.ProductName, "PCL for CAUMC", StringComparison.Ordinal) ||
            VersionOf(info.FileVersion ?? "0.0.0") != VersionOf(expectedVersion) ||
            VersionOf(AssemblyName.GetAssemblyName(file).Version!.ToString()) != VersionOf(expectedVersion))
            throw new InvalidDataException("下载文件的产品或内置版本与发布链接不一致，请联系发布者。");
    }
}

// All paths and the health token are created locally, never supplied by the article.
public sealed class LauncherUpdateTransaction {
    public string Target { get; set; } = "";
    public string Package { get; set; } = "";
    public string Version { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Nonce { get; set; } = Guid.NewGuid().ToString("N");
    public int ParentId { get; set; }
    public long ParentStarted { get; set; }
    public string Status { get; set; } = "prepared";
    public string Error { get; set; } = "";
    [JsonIgnore] public string Backup => Target + ".caumc-" + Nonce + ".bak";
    [JsonIgnore] public string Replacement => Target + ".caumc-" + Nonce + ".new";
    public static LauncherUpdateTransaction Read(string file) => JsonConvert.DeserializeObject<LauncherUpdateTransaction>(File.ReadAllText(file)) ?? throw new InvalidDataException("更新记录为空。");
    public void Save(string file) {
        var temporary = file + ".new";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(this), new UTF8Encoding(false));
        if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
    }
    public void Validate(string file) {
        if (!Guid.TryParseExact(Nonce, "N", out _) || !Path.IsPathRooted(Target) || !Path.IsPathRooted(Package) ||
            !Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetDirectoryName(Path.GetFullPath(Package)), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFullPath(Target), Path.GetFullPath(Package), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新记录的路径不正确。");
        LauncherUpdates.ValidateExecutable(Package, Version);
        if (!string.Equals(LauncherUpdates.Hash(Package), Sha256, StringComparison.Ordinal)) throw new InvalidDataException("已下载的更新文件发生变化，请重新下载。");
    }
    public static void Acknowledge(string file, string target, string version) {
        var tx = Read(file);
        if (tx.Status != "installed" || !string.Equals(Path.GetFullPath(target), Path.GetFullPath(tx.Target), StringComparison.OrdinalIgnoreCase) ||
            LauncherUpdates.VersionOf(version) != LauncherUpdates.VersionOf(tx.Version)) return;
        File.WriteAllText(file + ".ready", new JObject { ["nonce"] = tx.Nonce, ["pid"] = Process.GetCurrentProcess().Id }.ToString());
    }
    public bool Ready(string file, int processId) {
        try {
            var ack = JObject.Parse(File.ReadAllText(file + ".ready"));
            return (string?)ack["nonce"] == Nonce && (int?)ack["pid"] == processId;
        } catch (IOException) { return false; }
        catch (JsonException) { return false; }
    }
    // Used by both the helper and isolated tests: backup remains until health confirmation.
    public void Replace(string file, Func<bool> healthCheck) {
        Validate(file);
        if (File.Exists(Backup)) throw new IOException("上次更新的备份仍在处理中。");
        Status = "installing"; Save(file);
        var replaced = false;
        try {
            File.Copy(Package, Replacement, true);
            if (LauncherUpdates.Hash(Replacement) != Sha256) throw new InvalidDataException("复制更新文件时校验失败。");
            File.Replace(Replacement, Target, Backup);
            replaced = true;
            Status = "installed"; Save(file);
            if (!healthCheck()) throw new IOException("新版未能完成启动初始化，已恢复旧版。");
            Status = "succeeded"; Save(file);
        } catch (Exception ex) {
            Error = ex.Message;
            if (replaced) {
                File.Copy(Backup, Replacement, true);
                File.Replace(Replacement, Target, null);
                File.Delete(Backup);
            }
            Status = replaced ? "rolledback" : "failed";
            Save(file);
            throw;
        } finally {
            if (File.Exists(Replacement)) File.Delete(Replacement);
        }
        // Cleanup failure must not roll back an already-running healthy launcher.
        try { File.Delete(Backup); File.Delete(Package); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static int RunHelper(string file) {
        var tx = Read(file);
        Process? candidate = null;
        var parentExited = false;
        try {
            tx.Validate(file);
            tx.Status = "waiting"; tx.Save(file);
            Log(file, "Waiting for original launcher " + tx.ParentId);
            try {
                using var parent = Process.GetProcessById(tx.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks == tx.ParentStarted && !parent.WaitForExit(120000))
                    throw new IOException("旧启动器尚未退出，本次未替换文件。");
            } catch (ArgumentException) { /* Parent has already exited. */ }
            parentExited = true;
            if (Read(file).Status != "approved") throw new IOException("本次更新安装已取消，下载文件仍保留。");
            Log(file, "Original launcher exited; installing");
            tx.Replace(file, () => {
                try {
                    Log(file, "Starting new launcher");
                    candidate = StartLauncher(tx.Target, "--caumc-update-ready \"" + file + "\"");
                    Log(file, "New launcher PID " + candidate.Id);
                    if (candidate is null) return false;
                    var until = DateTime.UtcNow.AddSeconds(90);
                    while (DateTime.UtcNow < until) {
                        if (candidate.HasExited) return false;
                        if (tx.Ready(file, candidate.Id)) { Log(file, "New launcher ready"); return true; }
                        Thread.Sleep(100);
                    }
                    return false;
                } finally {
                    // A hung candidate must release the EXE before rollback.
                    if (candidate is not null && !tx.Ready(file, candidate.Id) && !candidate.HasExited) {
                        candidate.Kill(); candidate.WaitForExit(10000);
                    }
                }
            });
            return 0;
        } catch (Exception ex) {
            Log(file, ex.ToString());
            if (tx.Status is not "rolledback" and not "failed") { tx.Status = "failed"; tx.Error = ex.Message; tx.Save(file); }
            if (File.Exists(tx.Target) && (tx.Status == "rolledback" || parentExited))
                StartLauncher(tx.Target, "--caumc-update-result \"" + file + "\"");
            return 1;
        } finally { candidate?.Dispose(); }
    }

    private static void Log(string file, string message) {
        try { File.AppendAllText(file + ".log", DateTime.Now.ToString("O") + " " + message + Environment.NewLine); }
        catch (IOException) { }
    }

    private static Process StartLauncher(string target, string arguments) {
        // Windows/AV can briefly retain an image handle after atomic replacement.
        for (var attempt = 0; ; attempt++) {
            try {
                return Process.Start(new ProcessStartInfo(target, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! })
                    ?? throw new IOException("未能创建启动器进程。");
            } catch (System.ComponentModel.Win32Exception ex) when (attempt < 49 && ex.NativeErrorCode is 5 or 32) {
                Thread.Sleep(100);
            }
        }
    }
}
