using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SoftwareGamer.Core;

// No privileged token, path-based deletion, forced unlock, recursive delete or reparse traversal.
public sealed class WindowsFile : IDisposable
{
    private readonly List<SafeFileHandle> parents;
    private readonly SafeFileHandle handle;
    public FileIdentity Identity { get; }
    private WindowsFile(List<SafeFileHandle> parents, SafeFileHandle handle, FileIdentity identity)
        => (this.parents, this.handle, Identity) = (parents, handle, identity);

    public static WindowsFile Open(string root, string path, bool mutate)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A limpeza segura exige Windows.");
        path = Path.GetFullPath(path);
        root = Path.GetFullPath(root);
        if (!CleanupPolicy.IsWithin(root, path) || path.StartsWith(@"\\") || path[2..].Contains(':')) throw new IOException("Caminho não autorizado.");
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        if (drive.DriveType != DriveType.Fixed || drive.DriveFormat != "NTFS") throw new IOException("A limpeza exige volume local NTFS.");
        var parents = LockDirectory(Path.GetDirectoryName(path)!);
        SafeFileHandle? handle = null;
        try
        {
            handle = Native.CreateFileW(path, 0x80000000u | (mutate ? 0x10000u : 0u),
                mutate ? 0u : 3u, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            Check(handle);
            var info = Info(handle);
            if ((info.Attributes & (0x400u | 0x10u | 0x1u)) != 0 || info.Links != 1) throw new IOException("Link, diretório, arquivo somente leitura ou hardlink ignorado.");
            var final = new StringBuilder(32768);
            var n = Native.GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
            if (n == 0 || n >= final.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
            var resolved = final.ToString();
            if (resolved.StartsWith(@"\\?\")) resolved = resolved[4..];
            if (!string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase)) throw new IOException("O destino real difere do caminho analisado.");
            return new WindowsFile(parents, handle, new(info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow,
                ((long)info.SizeHigh << 32) | info.SizeLow, ((long)info.WriteHigh << 32) | info.WriteLow, info.Links));
        }
        catch { handle?.Dispose(); parents.ForEach(h => h.Dispose()); throw; }
    }

    public static List<SafeFileHandle> LockDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        directory = Path.GetFullPath(directory);
        if (directory.StartsWith(@"\\")) throw new IOException("Diretórios de rede não são permitidos.");
        var parts = new Stack<string>();
        for (var p = directory; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p)) parts.Push(p);
        var handles = new List<SafeFileHandle>();
        try
        {
            while (parts.TryPop(out var p))
            {
                // Hold every ancestor against writes and renames while checking and operating.
                var h = Native.CreateFileW(p, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                handles.Add(h); Check(h);
                var info = Info(h);
                if ((info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0) throw new IOException("Diretório com link/reparse point ignorado.");
            }
            return handles;
        }
        catch { handles.ForEach(h => h.Dispose()); throw; }
    }

    public void Rename(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (destination.StartsWith(@"\\") || destination[2..].Contains(':')) throw new IOException("Destino inválido.");
        var locked = LockDirectory(Path.GetDirectoryName(destination)!);
        try
        {
            var name = Encoding.Unicode.GetBytes(destination);
            var offset = IntPtr.Size == 8 ? 20 : 12;
            var bytes = new byte[offset + name.Length]; // ReplaceIfExists=false, RootDirectory=NULL
            BitConverter.GetBytes(name.Length).CopyTo(bytes, IntPtr.Size == 8 ? 16 : 8);
            name.CopyTo(bytes, offset);
            if (!Native.SetFileInformationByHandle(handle, 3, bytes, (uint)bytes.Length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { locked.ForEach(h => h.Dispose()); }
    }
    public void Delete()
    {
        if (!Native.SetFileInformationByHandle(handle, 4, BitConverter.GetBytes(1), 4)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() { handle.Dispose(); parents.ForEach(h => h.Dispose()); }
    private static void Check(SafeFileHandle h) { if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private static Native.FileInfo Info(SafeFileHandle h)
    {
        if (!Native.GetFileInformationByHandle(h, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct FileInfo
        {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandle(SafeFileHandle h, out FileInfo info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder path, uint length, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetFileInformationByHandle(SafeFileHandle h, int infoClass, byte[] data, uint size);
    }
}
