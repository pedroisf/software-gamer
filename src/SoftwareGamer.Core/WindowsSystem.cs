using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SoftwareGamer.Core;

public static class WindowsSystem
{
    public static string CurrentOwner => $"{Environment.ProcessId}:{Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks}";
    public static IReadOnlyList<PowerScheme> PowerSchemes()
    {
        var result = new List<PowerScheme>();
        for (uint index = 0; index < 100; index++)
        {
            var bytes = new byte[16]; uint size = 16;
            var error = Native.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, bytes, ref size);
            if (error == 259) break;
            if (error != 0) throw new Win32Exception((int)error);
            var id = new Guid(bytes); uint nameSize = 0;
            Native.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref nameSize);
            var name = new byte[Math.Min(nameSize, 65536)];
            var code = Native.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, name, ref nameSize);
            result.Add(new(id.ToString(), code == 0 ? Encoding.Unicode.GetString(name).TrimEnd('\0') : id.ToString()));
        }
        return result;
    }
    public static string ActivePowerScheme()
    {
        var code = Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr);
        if (code != 0) throw new Win32Exception((int)code);
        try { return Marshal.PtrToStructure<Guid>(ptr).ToString(); }
        finally { Native.LocalFree(ptr); }
    }
    public static void SetPowerScheme(string id)
    {
        var guid = Guid.Parse(id);
        var code = Native.PowerSetActiveScheme(IntPtr.Zero, ref guid);
        if (code != 0) throw new Win32Exception((int)code);
    }
    public static bool OnAcPower() => Native.GetSystemPowerStatus(out var p) && p.AcLine == 1;
    public static string PowerSource() => Native.GetSystemPowerStatus(out var p) ? p.AcLine switch { 0 => "Bateria", 1 => "Tomada (AC)", _ => "Indisponível" } : "Indisponível";
    public static string ExecutableHash(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s));
    }
    public static IReadOnlyList<GameProcess> Processes()
    {
        var result = new List<GameProcess>();
        using var self = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == Environment.ProcessId || p.SessionId != self.SessionId) continue;
                    var path = p.MainModule?.FileName;
                    if (path is not null) result.Add(new(p.Id, p.StartTime.ToUniversalTime().Ticks, p.ProcessName, path));
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return result.OrderBy(x => x.Name).ToArray();
    }
    public static Process? OpenInstance(string identity)
    {
        var parts = identity.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var pid) || !long.TryParse(parts[1], out var ticks)) throw new InvalidDataException("Identidade de processo inválida.");
        try
        {
            var p = Process.GetProcessById(pid);
            if (p.StartTime.ToUniversalTime().Ticks != ticks) { p.Dispose(); return null; }
            return p;
        }
        catch (ArgumentException) { return null; }
    }
    public static (long RamUsed, long RamTotal) Memory()
    {
        var s = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        if (!Native.GlobalMemoryStatusEx(ref s)) return (0, 0);
        return ((long)(s.TotalPhysical - s.AvailablePhysical), (long)s.TotalPhysical);
    }
    private static class Native
    {
        [DllImport("powrprof.dll")] internal static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll")] internal static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("powrprof.dll")] internal static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, byte[] buffer, ref uint size);
        [DllImport("powrprof.dll")] internal static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
        [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr memory);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemPowerStatus(out PowerStatus status);
        [StructLayout(LayoutKind.Sequential)] internal struct PowerStatus { public byte AcLine, BatteryFlag, BatteryLife, Reserved; public uint Seconds, FullSeconds; }
        [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}
