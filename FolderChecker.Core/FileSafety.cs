using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

internal static class FileSafety
{
    internal static bool IsWithin(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.GetFullPath(path);
        return string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    internal static string CanonicalDirectory(string path)
    {
        using var handle = CreateFileW(path, 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768);
        if (GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var value = buffer.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) value = @"\\" + value[8..];
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value[4..];
        return Path.TrimEndingDirectorySeparator(value);
    }
    internal static FileIdentity? Identity(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!GetFileInformationByHandleEx(handle, 0, out BasicInfo basic, (uint)Marshal.SizeOf<BasicInfo>()) ||
            !GetFileInformationByHandleEx(handle, 18, out IdInfo id, (uint)Marshal.SizeOf<IdInfo>())) return null;
        return new FileIdentity(id.Volume, id.Low, id.High, basic.ChangeTime);
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct IdInfo { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out BasicInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out IdInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
}
