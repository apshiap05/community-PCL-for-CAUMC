using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
[assembly: AssemblyProduct("PCL for CAUMC")]
#if NEW_VERSION
[assembly: AssemblyVersion("99.0.0.0")]
[assembly: AssemblyFileVersion("99.0.0.0")]
#else
[assembly: AssemblyVersion("98.0.0.0")]
[assembly: AssemblyFileVersion("98.0.0.0")]
#endif
internal static class UpdateTarget {
    [STAThread] private static void Main(string[] args) {
        if (args.Length == 1 && args[0] == "--parent") { Thread.Sleep(120000); return; }
        if (args.Length != 2) return;
        var record = args[1];
        if (args[0] == "--caumc-update-result") { File.WriteAllText(record + ".old-started", "restored"); return; }
        if (args[0] != "--caumc-update-ready") return;
        if (File.Exists(record + ".fail")) return;
        if (File.Exists(record + ".hang")) { Thread.Sleep(120000); return; }
        var nonce = Regex.Match(File.ReadAllText(record), "\"Nonce\"\\s*:\\s*\"([a-f0-9]+)\"").Groups[1].Value;
        File.WriteAllText(record + ".ready", "{\"nonce\":\"" + nonce + "\",\"pid\":" + Process.GetCurrentProcess().Id + "}");
        Thread.Sleep(1000);
    }
}
