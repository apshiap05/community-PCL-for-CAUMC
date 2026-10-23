using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PCLCS.CAUMC;

/// <summary>A separate, owned cache survives native loader cancellation and temporary-file cleanup.</summary>
public sealed class DownloadCheckpoint {
    public static Func<HttpClient, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Sender { get; set; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, IntPtr input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
    private sealed class Metadata {
        public string Target = "";
        public long Length;
        public string Validator = "";
        public string Hash = "";
        public List<long[]> Ranges = new(); // Half-open byte ranges, merged before persistence.
    }
    private readonly object gate = new();
    private readonly string dataPath, metaPath;
    private Metadata meta;
    private bool dirty;
    private DownloadCheckpoint(string root, string target) {
        Directory.CreateDirectory(root);
        using var sha = SHA256.Create();
        var key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(target).ToUpperInvariant()))).Replace("-", "");
        dataPath = System.IO.Path.Combine(root, key + ".part"); metaPath = dataPath + ".json";
        try { meta = JsonConvert.DeserializeObject<Metadata>(File.ReadAllText(metaPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { meta = new(); }
        if (!File.Exists(dataPath)) meta.Ranges.Clear();
        if (!string.Equals(meta.Target, target, StringComparison.OrdinalIgnoreCase)) meta = new() { Target = target };
        var actualLength = File.Exists(dataPath) ? new FileInfo(dataPath).Length : 0;
        meta.Ranges = Merge(meta.Ranges.Where(r => r.Length == 2 && r[0] >= 0 && r[0] < r[1] && r[1] <= actualLength && r[1] <= meta.Length));
    }
    public static DownloadCheckpoint Open(string root, string target) => new(root, target);
    public long SavedBytes { get { lock (gate) return meta.Ranges.Sum(r => r[1] - r[0]); } }
    public void Begin(long length, string? validator, string? hash) {
        lock (gate) {
            // An expected content hash also identifies a file across mirror URLs.
            var same = meta.Length == length && length > 0 &&
                (!string.IsNullOrEmpty(hash) ? string.Equals(meta.Hash, hash, StringComparison.OrdinalIgnoreCase) :
                !string.IsNullOrEmpty(validator) && meta.Validator == validator);
            if (!same) { meta.Ranges.Clear(); if (File.Exists(dataPath)) File.Delete(dataPath); }
            meta.Length = length; meta.Validator = validator ?? ""; meta.Hash = hash ?? ""; dirty = true;
        }
    }
    public void Write(long offset, byte[] bytes, int count) {
        lock (gate) {
            if (meta.Length <= 0 || offset < 0 || count <= 0 || offset + count > meta.Length) return;
            using (var stream = new FileStream(dataPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read)) {
                if (stream.Length == 0 && Environment.OSVersion.Platform == PlatformID.Win32NT)
                    DeviceIoControl(stream.SafeFileHandle, 0x900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero); // FSCTL_SET_SPARSE
                stream.Position = offset; stream.Write(bytes, 0, count);
            }
            meta.Ranges.Add([offset, offset + count]);
            if (meta.Ranges.Count > 64) meta.Ranges = Merge(meta.Ranges);
            dirty = true;
        }
    }
    public void Flush() {
        lock (gate) {
            if (!dirty) return;
            meta.Ranges = Merge(meta.Ranges);
            File.WriteAllText(metaPath + ".new", JsonConvert.SerializeObject(meta));
            if (File.Exists(metaPath)) File.Replace(metaPath + ".new", metaPath, null); else File.Move(metaPath + ".new", metaPath);
            dirty = false;
        }
    }
    private static List<long[]> Merge(IEnumerable<long[]> ranges) {
        var result = new List<long[]>();
        foreach (var r in ranges.OrderBy(r => r[0])) {
            if (result.Count == 0 || result[result.Count - 1][1] < r[0]) result.Add([r[0], r[1]]);
            else result[result.Count - 1][1] = Math.Max(result[result.Count - 1][1], r[1]);
        }
        return result;
    }
    public static string Validator(HttpResponseMessage response) =>
        response.Headers.ETag is { IsWeak: false } etag ? etag.ToString() :
        response.Content.Headers.LastModified?.ToString("R") ?? "";

    // Preparation runs only after the user chooses Resume. Normal scheduling stays in PCL.
    // False means no usable checkpoint: let the native loader download and validate normally.
    public async Task<bool> RestoreAsync(HttpClient http, DownloadSpec spec, CancellationToken token, Action<double>? progress = null) {
        if (spec.Checker().Check(spec.Path) == null) return true;
        lock (gate) {
            if (meta.Length <= 0 || meta.Ranges.Count == 0 || (meta.Hash.Length == 0 && meta.Validator.Length == 0)) return false;
            if (meta.Hash.Length > 0 && !string.Equals(meta.Hash, spec.Hash, StringComparison.OrdinalIgnoreCase)) return false;
        }
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++) foreach (var url in spec.Urls) {
            token.ThrowIfCancellationRequested();
            try {
                List<long[]> gaps; long length; string validator;
                lock (gate) {
                    length = meta.Length; validator = meta.Validator;
                    gaps = new(); long next = 0;
                    foreach (var range in Merge(meta.Ranges)) { if (range[0] > next) gaps.Add([next, range[0]]); next = range[1]; }
                    if (next < length) gaps.Add([next, length]);
                }
                foreach (var gap in gaps) {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new RangeHeaderValue(gap[0], gap[1] - 1);
                    if (validator.Length > 0) request.Headers.TryAddWithoutValidation("If-Range", validator);
                    if (spec.BrowserHeaders) { request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0"); request.Headers.Referrer = new Uri(url); }
                    using var response = await (Sender?.Invoke(http, request, token) ?? http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable) {
                        Begin(-1, "", ""); Flush(); return false;
                    }
                    response.EnsureSuccessStatusCode();
                    var range = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != gap[0] || range.To != gap[1] - 1 || range.Length != length ||
                        (meta.Hash.Length == 0 && Validator(response) != validator)) { Begin(-1, "", ""); Flush(); return false; }
                    using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    var buffer = new byte[65536]; long offset = gap[0];
                    while (offset < gap[1]) {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        var read = await input.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, gap[1] - offset), timeout.Token).ConfigureAwait(false);
                        if (read == 0) throw new EndOfStreamException("下载连接提前结束");
                        Write(offset, buffer, read); offset += read; progress?.Invoke((double)SavedBytes / length);
                    }
                    Flush();
                }
                token.ThrowIfCancellationRequested();
                if (spec.Checker().Check(dataPath) is { } error) { Begin(-1, "", ""); Flush(); throw new InvalidDataException(error); }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(spec.Path)!);
                File.Copy(dataPath, spec.Path, true);
                return true;
            } catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) {
                token.ThrowIfCancellationRequested(); last = ex; Flush();
            }
        }
        throw new IOException("恢复下载失败，已保留进度。", last);
    }
    public void Delete() { lock (gate) { if (File.Exists(dataPath)) File.Delete(dataPath); if (File.Exists(metaPath)) File.Delete(metaPath); meta.Ranges.Clear(); dirty = false; } }
}
