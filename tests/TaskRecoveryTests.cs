using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using PCLCS.CAUMC;
using Newtonsoft.Json.Linq;

internal static class TaskRecoveryTests {
    public static async Task Run() {
        var root = Path.Combine(Path.GetTempPath(), "CAUMC-task-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var path = Path.Combine(root, "tasks.json");
            var journal = new TaskJournal(path);
            var running = new TaskRecord { Name = "正在下载", Progress = .4, Recipe = new() { ["type"] = "files" } };
            var canceled = new TaskRecord { Name = "主动取消", State = "canceled" };
            var completed = new TaskRecord { Name = "已完成", State = "succeeded" };
            journal.Put(running); journal.Put(canceled); journal.Put(completed); journal.Save(); journal.Save();
            var reloaded = new TaskJournal(path);
            Check(reloaded.Find(running.Id) is { State: "interrupted", Progress: .4 }, "重建任务存储后保留进度且不自动运行");
            Check(!reloaded.Find(canceled.Id)!.NeedsAttention && !reloaded.Find(completed.Id)!.NeedsAttention, "取消与完成任务保留记录但不提示恢复");
            File.WriteAllText(path, "broken");
            Check(new TaskJournal(path).Snapshot().Count == 3, "任务文件损坏可从上次原子备份恢复");
            var data = Enumerable.Range(0, 524288).Select(i => (byte)(i % 251)).ToArray();
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
            var target = Path.Combine(root, "game", "fixture.bin");
            var spec = new DownloadSpec { Path = target, Urls = ["https://first.invalid/file", "https://backup.invalid/file"], ActualSize = data.Length, Hash = hash };
            var cacheRoot = Path.Combine(root, "cache");
            var cache = DownloadCheckpoint.Open(cacheRoot, target);
            cache.Begin(data.Length, "\"v1\"", hash);
            cache.Write(0, data, 100000);
            cache.Write(200000, data.Skip(200000).ToArray(), 50000); cache.Flush();
            cache = DownloadCheckpoint.Open(cacheRoot, target);
            Check(cache.SavedBytes == 150000, "分段缓存重新打开后保留非连续字节范围");
            long requested = 0; int calls = 0;
            using var http = new HttpClient(new Handler(request => {
                calls++;
                if (request.RequestUri!.Host == "first.invalid") return new(HttpStatusCode.ServiceUnavailable);
                var range = request.Headers.Range!.Ranges.Single();
                var from = range.From!.Value; var to = range.To!.Value; requested += to - from + 1;
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data.Skip((int)from).Take((int)(to - from + 1)).ToArray()) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.Length);
                response.Headers.ETag = new EntityTagHeaderValue("\"v1\""); return response;
            }));
            Check(await cache.RestoreAsync(http, spec, default), "恢复失败源后自动尝试可用备用源");
            Check(File.ReadAllBytes(target).SequenceEqual(data) && requested == data.Length - 150000 && calls == 3, "只请求缺失范围并校验完整文件内容");
            cache.Delete(); File.Delete(target);
            cache.Begin(data.Length, "\"old\"", ""); cache.Write(0, data, 2000); cache.Flush();
            spec.Hash = null;
            using var ignoresRange = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) }));
            Check(!await cache.RestoreAsync(ignoresRange, spec, default) && cache.SavedBytes == 0 && !File.Exists(target), "服务器不支持续传时丢弃旧片段并交回原生下载器");
            cache.Begin(data.Length, "", ""); cache.Write(0, data, 2000); cache.Flush();
            Check(!await cache.RestoreAsync(ignoresRange, spec, default), "没有文件校验标识时不拼接未知版本的数据");
            cache.Begin(data.Length, "\"v2\"", hash); cache.Write(0, data, 4000); cache.Flush();
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); spec.Hash = hash;
            try { await cache.RestoreAsync(http, spec, cancel.Token); throw new Exception("取消未生效"); } catch (OperationCanceledException) { }
            Check(DownloadCheckpoint.Open(cacheRoot, target).SavedBytes == 4000 && !File.Exists(target), "取消恢复保留缓存且不生成伪完成文件");
            cache.Delete();
            Check(!Directory.GetFiles(cacheRoot).Any(), "清理只删除任务自有下载缓存");
            var source = Path.Combine(root, "old-pcl", ".minecraft");
            Directory.CreateDirectory(Path.Combine(source, "versions", "fixture"));
            File.WriteAllText(Path.Combine(source, "versions", "fixture", "fixture.json"), "{\"id\":\"fixture\"}");
            File.WriteAllBytes(Path.Combine(source, "world.dat"), Enumerable.Repeat((byte)42, 3145728).ToArray());
            var importRoot = Path.Combine(root, "imports"); var key = Guid.NewGuid().ToString("N");
            using var copyCancel = new CancellationTokenSource();
            try { PclDataImport.CopyGame(source, importRoot, copyCancel.Token, new CancelCopy(copyCancel), key); throw new Exception("导入未停止"); }
            catch (OperationCanceledException) { }
            Check(Directory.Exists(Path.Combine(importRoot, ".import-" + key)) && !Directory.GetDirectories(importRoot).Any(d => !Path.GetFileName(d).StartsWith(".import-")), "中断的旧 PCL 导入保存在独立暂存目录且不注册半成品");
            var imported = PclDataImport.CopyGame(source, importRoot, default, checkpointId: key);
            Check(File.ReadAllBytes(Path.Combine(imported, "world.dat")).All(b => b == 42) && File.ReadAllBytes(Path.Combine(source, "world.dat")).Length == 3145728,
                "旧 PCL 导入恢复完整复制且保留源文件");
        } finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }
    private sealed class CancelCopy(CancellationTokenSource cancel) : IProgress<PclImportProgress> {
        public void Report(PclImportProgress value) { if (value.CompletedBytes >= 1048576) cancel.Cancel(); }
    }
}
