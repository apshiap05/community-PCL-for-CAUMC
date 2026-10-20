param([string]$SourceRoot = (Split-Path $PSScriptRoot))
$ErrorActionPreference = 'Stop'
if (-not ('CaumcShaderCompiler' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
public static class CaumcShaderCompiler {
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface Blob {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] UIntPtr GetBufferSize();
    }
    [DllImport("d3dcompiler_47.dll", CallingConvention=CallingConvention.StdCall, CharSet=CharSet.Ansi)]
    private static extern int D3DCompile(byte[] source, UIntPtr size, string name, IntPtr defines, IntPtr include,
        string entry, string profile, uint flags, uint flags2, out Blob code, out Blob errors);
    public static void Compile(string input, string output) {
        byte[] source = File.ReadAllBytes(input);
        Blob code = null, errors = null;
        try {
            int hr = D3DCompile(source, (UIntPtr)source.Length, input, IntPtr.Zero, IntPtr.Zero,
                "main", "ps_2_0", 1u << 15, 0, out code, out errors);
            string message = errors == null ? "" : Marshal.PtrToStringAnsi(errors.GetBufferPointer());
            if (hr < 0) throw new InvalidOperationException(message);
            byte[] bytes = new byte[(int)code.GetBufferSize().ToUInt64()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            File.WriteAllBytes(output, bytes);
            if (!String.IsNullOrWhiteSpace(message)) Console.WriteLine(message);
        } finally {
            if (errors != null) Marshal.ReleaseComObject(errors);
            if (code != null) Marshal.ReleaseComObject(code);
        }
    }
}
'@
}
$shaderSource = Join-Path $SourceRoot 'Plain Craft Launcher 2/Resources/LiquidGlass.hlsl'
$shaderOutput = Join-Path $SourceRoot 'Plain Craft Launcher 2/Resources/LiquidGlass.ps'
[CaumcShaderCompiler]::Compile($shaderSource, $shaderOutput)
Write-Output ('Compiled WPF ps_2_0: ' + $shaderOutput)
