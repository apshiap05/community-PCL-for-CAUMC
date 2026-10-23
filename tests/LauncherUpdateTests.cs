using System.Diagnostics;
using PCLCS.CAUMC;

internal static class LauncherUpdateTests {
    private static void Check(bool condition, string name) {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    private static ArticleSnapshot Article(string body) => new() { Source = LauncherUpdates.Source.AbsoluteUri,
        Nodes = ArticleParser.Parse("<div class='theme-doc-markdown'>" + body + "</div><nav><a href='x.exe'>PCL-for-CAUMC-99.0.0.exe</a></nav>", LauncherUpdates.Source) };
    public static Task Run() {
        var article = Article("<a href='old.exe'>PCL-for-CAUMC-1.2.0.exe</a><a href='random.exe'><b>PCL-for-CAUMC-1.10.0.exe</b></a>" +
            "<a href='second.exe'>PCL-for-CAUMC-1.10.0.0.exe</a><a href='beta.exe'>PCL-for-CAUMC-9.0.0-beta.exe</a>" +
            "<a href='http://example.com/unsafe.exe'>PCL-for-CAUMC-99.0.0.exe</a><a href='zip.zip'>PCL-for-CAUMC-90.0.0.exe</a>");
        var latest = LauncherUpdates.Latest(article, new Version(1, 3, 0, 0));
        Check(latest?.Version == "1.10.0.0" && latest.Url.EndsWith("random.exe"), "按数字版本选择最高正式版，同版本取首个，忽略侧栏与测试版");
        Check(LauncherUpdates.Latest(article, new Version(1, 10, 0, 0)) is null, "相同或更低版本不更新");
        Check(LauncherUpdates.VersionOf("1.3.0") == LauncherUpdates.VersionOf("1.3.0.0"), "三段四段版本号归一化");
        Check(LauncherUpdates.Display("1.3.0.1") == "1.3.0.1", "保留非零修订版本");
        var exe = Path.GetFullPath("dist/PCL for CAUMC.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("请先编译主程序，再运行更新事务测试", exe);
        var version = FileVersionInfo.GetVersionInfo(exe).FileVersion!;
        LauncherUpdates.ValidateExecutable(exe, version);
        Check(true, "不执行下载文件即可核对真实启动器产品与版本");
        var root = Path.Combine(Path.GetTempPath(), "caumc-update-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var target = Path.Combine(root, "用户 自定义名字.exe");
            var package = Path.Combine(root, "package.exe");
            var record = Path.Combine(root, "transaction.json");
            File.Copy(exe, target); File.AppendAllText(target, "old-marker");
            var oldHash = LauncherUpdates.Hash(target);
            File.Copy(exe, package);
            var tx = new LauncherUpdateTransaction { Target = target, Package = package, Version = version, Sha256 = LauncherUpdates.Hash(package) };
            tx.Save(record);
            tx.Replace(record, () => {
                Check(File.Exists(tx.Backup) && LauncherUpdates.Hash(tx.Backup) == oldHash, "新版初始化完成前保留原 EXE 备份");
                Check(LauncherUpdates.Hash(target) == tx.Sha256 && Path.GetFileName(target) == "用户 自定义名字.exe", "原路径原文件名原子替换 EXE");
                return true;
            });
            Check(LauncherUpdateTransaction.Read(record).Status == "succeeded" && !File.Exists(tx.Backup) && !File.Exists(package), "成功初始化后删除旧 EXE 与临时包");
            File.AppendAllText(target, "rollback-old");
            oldHash = LauncherUpdates.Hash(target);
            File.Copy(exe, package);
            tx = new() { Target = target, Package = package, Version = version, Sha256 = LauncherUpdates.Hash(package) };
            tx.Save(record);
            try { tx.Replace(record, () => false); throw new Exception("应回退"); } catch (IOException) { }
            Check(LauncherUpdates.Hash(target) == oldHash && LauncherUpdateTransaction.Read(record).Status == "rolledback", "新版未完成初始化则恢复原 EXE 并保留错误");
            Check(File.Exists(package) && !File.Exists(tx.Backup), "失败回退保留可重试安装包");
            tx = new() { Target = target, Package = package, Version = version, Sha256 = new string('0', 64) };
            try { tx.Replace(record, () => true); throw new Exception("应拒绝变化文件"); } catch (InvalidDataException) { }
            Check(LauncherUpdates.Hash(target) == oldHash, "安装包变化时不触碰原 EXE");
            try { LauncherUpdates.ValidateExecutable(package, "99.0.0"); throw new Exception("应拒绝版本不符"); } catch (InvalidDataException) { }
            File.WriteAllText(package, "<html>download error</html>");
            try { LauncherUpdates.ValidateExecutable(package, version); throw new Exception("应拒绝网页"); } catch (InvalidDataException) { }
            Check(LauncherUpdates.Hash(target) == oldHash, "版本不符和错误网页均无法进入替换流程");
            var invalid = Article("<a href='file.exe'>PCL-for-CAUMC-999999999999999999999.0.0.exe</a><a href='next.exe'>PCL-for-CAUMC-1.4.0.exe</a>");
            Check(LauncherUpdates.Latest(invalid, new Version(1, 3, 0, 0))?.Version == "1.4.0.0", "损坏版本文字不影响其他有效链接");
        } finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }
}
