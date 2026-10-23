using System.IO;

namespace PCLCS.CAUMC;

public sealed class TaskRecord {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string State { get; set; } = "running";
    public string Stage { get; set; } = "";
    public double Progress { get; set; }
    public string Error { get; set; } = "";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;
    public JObject Recipe { get; set; } = new();
    public List<DownloadSpec> Downloads { get; set; } = new();
    public bool NeedsAttention => State is "interrupted" or "paused" or "failed";
}

public sealed class DownloadSpec {
    public string Path { get; set; } = "";
    public string[] Urls { get; set; } = [];
    public long ActualSize { get; set; } = -1;
    public long MinSize { get; set; } = -1;
    public string? Hash { get; set; }
    public bool IsJson { get; set; }
    public bool BrowserHeaders { get; set; }
    public FileChecker Checker() => new() { ActualSize = ActualSize, MinSize = MinSize, Hash = Hash, IsJson = IsJson };
}

/// <summary>Only data is persisted. Runtime delegates and credentials never enter the journal.</summary>
public sealed class TaskJournal {
    private readonly string path;
    private readonly object gate = new();
    private List<TaskRecord> records = new();
    public TaskJournal(string path) {
        this.path = path;
        foreach (var candidate in new[] { path, path + ".bak" }) {
            try {
                if (!File.Exists(candidate)) continue;
                var document = JObject.Parse(File.ReadAllText(candidate));
                if ((int?)document["version"] != 1) continue;
                records = document["tasks"]?.ToObject<List<TaskRecord>>() ?? new();
                records = records.Where(t => Guid.TryParseExact(t.Id, "N", out _) && t.Recipe != null).ToList();
                foreach (var task in records) {
                    task.Downloads ??= new(); task.Name ??= "未命名任务"; task.Kind ??= "任务";
                    task.Stage ??= ""; task.Error ??= "";
                    task.Progress = double.IsNaN(task.Progress) || double.IsInfinity(task.Progress) ? 0 : Math.Max(0, Math.Min(1, task.Progress));
                    if (task.State is not ("running" or "paused" or "interrupted" or "failed" or "succeeded" or "canceled")) task.State = "interrupted";
                }
                break;
            } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        foreach (var task in records.Where(t => t.State == "running")) task.State = "interrupted";
    }
    public List<TaskRecord> Snapshot() { lock (gate) return records.Select(Clone).ToList(); }
    public List<TaskRecord> Summaries() { lock (gate) return records.Select(t => new TaskRecord {
        Id = t.Id, Name = t.Name, Kind = t.Kind, State = t.State, Stage = t.Stage, Progress = t.Progress,
        Error = t.Error, Created = t.Created, Updated = t.Updated, Recipe = (JObject)t.Recipe.DeepClone()
    }).ToList(); }
    public TaskRecord? Find(string id) { lock (gate) return records.FirstOrDefault(t => t.Id == id) is { } t ? Clone(t) : null; }
    private static TaskRecord Clone(TaskRecord task) => JObject.FromObject(task).ToObject<TaskRecord>()!;
    public void Put(TaskRecord task) {
        lock (gate) {
            var index = records.FindIndex(t => t.Id == task.Id);
            if (index < 0) records.Add(Clone(task)); else records[index] = Clone(task);
        }
    }
    public void Save() {
        lock (gate) {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var text = new JObject { ["version"] = 1, ["tasks"] = JArray.FromObject(records) }.ToString(Formatting.None);
            var pending = path + ".new";
            using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None)) {
                var bytes = Encoding.UTF8.GetBytes(text); stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(pending, path, path + ".bak"); else File.Move(pending, path);
        }
    }
    public static string StateText(string state) => state switch {
        "running" => "进行中", "interrupted" => "等待恢复", "paused" => "已暂停", "failed" => "失败",
        "succeeded" => "已完成", "canceled" => "已取消", _ => "等待中"
    };
}
