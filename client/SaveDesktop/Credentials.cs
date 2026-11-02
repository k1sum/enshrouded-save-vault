using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Enshrouded.SaveDesktop;

// Windows Credential Manager: one generic credential per server, never written to the settings file.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class Credentials
{
    private static string Target(string server) => "EnshroudedSaveManager:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(server.TrimEnd('/').ToLowerInvariant())));
    public static string Read(string server)
    {
        if (string.IsNullOrEmpty(server)) return "";
        if (!CredRead(Target(server), 1, 0, out var ptr)) { var code = Marshal.GetLastWin32Error(); if (code == 1168) return ""; throw new Win32Exception(code, "无法读取 Windows 凭据。"); }
        try { var c = Marshal.PtrToStructure<Credential>(ptr); if (c.BlobSize > 5120) throw new IOException("Windows 凭据大小异常。"); var bytes = new byte[c.BlobSize]; Marshal.Copy(c.Blob, bytes, 0, bytes.Length); try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); } }
        finally { CredFree(ptr); }
    }
    public static void Save(string server, string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token); var ptr = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, ptr, bytes.Length); var c = new Credential { Type = 1, TargetName = Target(server), Blob = ptr, BlobSize = (uint)bytes.Length, Persist = 2, UserName = "EnshroudedSaveManager" }; if (!CredWrite(ref c, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法保存 Windows 凭据。"); }
        finally { for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(ptr, i, 0); Marshal.FreeHGlobal(ptr); CryptographicOperations.ZeroMemory(bytes); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Credential
    { public uint Flags; public uint Type; public string? TargetName; public string? Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint BlobSize; public IntPtr Blob; public uint Persist; public uint AttributeCount; public IntPtr Attributes; public string? TargetAlias; public string? UserName; }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}
