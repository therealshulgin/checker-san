using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

/// <summary>How a listed entry may be used. Only <see cref="Normal"/> entries are read or traversed.</summary>
internal enum EntryAccess { Normal, Link, OnlineOnly }

internal static class FileSafety
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000, RecallOnDataAccess = (FileAttributes)0x400000;
    private const uint NameSurrogate = 0x20000000;

    // .NET processes may see cloud placeholders disguised as plain files, and reading an online-only file
    // downloads it. Exposing them makes reparse tags and the offline bit visible to every engine.
    static FileSafety()
    {
        if (!OperatingSystem.IsWindows()) return;
        try { RtlSetProcessPlaceholderCompatibilityMode(2); }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>Forces placeholder exposure before an engine reads its first attribute.</summary>
    internal static void Initialize() { }

    /// <summary>
    /// Symbolic links, junctions and mount points are links: never followed. Cloud or other data reparse
    /// points (OneDrive, deduplication, compression) are read normally unless their data is not local.
    /// </summary>
    internal static EntryAccess Access(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) != 0 && (ReparseTag(path) & NameSurrogate) != 0) return EntryAccess.Link;
        return (attributes & FileAttributes.Directory) == 0 && (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0
            ? EntryAccess.OnlineOnly : EntryAccess.Normal;
    }

    /// <summary>Same classification for an open handle, without following the path again.</summary>
    internal static EntryAccess Access(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out AttributeTagInfo info, (uint)Marshal.SizeOf<AttributeTagInfo>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var attributes = (FileAttributes)info.Attributes;
        if ((attributes & FileAttributes.ReparsePoint) != 0 && (info.ReparseTag & NameSurrogate) != 0) return EntryAccess.Link;
        return (attributes & FileAttributes.Directory) == 0 && (attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0
            ? EntryAccess.OnlineOnly : EntryAccess.Normal;
    }

    private static uint ReparseTag(string path)
    {
        // Querying the parent directory never opens, follows or downloads the entry itself.
        var search = FindFirstFileExW(path, 1, out var data, 0, IntPtr.Zero, 0);
        if (search == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        FindClose(search);
        return (data.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.Reserved0 : 0;
    }

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
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public uint Attributes, ReparseTag; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct FindData
    {
        public uint Attributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint SizeHigh, SizeLow, Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateFileName;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out BasicInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out IdInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out AttributeTagInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern IntPtr FindFirstFileExW(string name, int infoLevel, out FindData data, int searchOp, IntPtr filter, int flags);
    [DllImport("kernel32.dll")] private static extern bool FindClose(IntPtr search);
    [DllImport("ntdll.dll")] private static extern sbyte RtlSetProcessPlaceholderCompatibilityMode(sbyte mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
}
