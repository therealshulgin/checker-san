using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

/// <summary>How a listed entry may be used. Only <see cref="Normal"/> entries are read or traversed.</summary>
internal enum EntryAccess { Normal, Link, OnlineOnly }

internal enum StorageKind { Unknown, SolidState, Rotational, Network }

/// <summary>The physical device behind a folder: folders with the same key compete for the same disk.</summary>
internal sealed record StorageDevice(string Key, StorageKind Kind);

internal sealed record OpenedFile(DateTime CreationUtc, DateTime LastWriteUtc, FileIdentity? Identity);
internal sealed record PathMetadata(FileAttributes Attributes, long Size, DateTime CreationUtc, DateTime LastWriteUtc);

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
        var search = FindFirstFileExW(Native(path), 1, out var data, 0, IntPtr.Zero, 0);
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
        using var handle = CreateFileW(Native(path), 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
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

    /// <summary>
    /// Renames an open file into a directory held by handle, never replacing an existing name. Returns the
    /// Win32 error, 0 on success. The handles fix both objects; callers check where they really are
    /// (<see cref="FinalPath"/>), since a handle opened by path may already have been led elsewhere by a link.
    /// </summary>
    internal static int RenameByHandle(SafeFileHandle file, SafeFileHandle destinationDirectory, string name)
    {
        var bytes = Encoding.Unicode.GetBytes(name);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(uint);
        // Windows rejects a buffer shorter than sizeof(FILE_RENAME_INFORMATION) (24 bytes on x64), which a
        // one-character name would otherwise produce (error 24).
        var length = Math.Max(nameOffset + bytes.Length, IntPtr.Size == 8 ? 24 : 16);
        var memory = Marshal.AllocHGlobal(length);
        try
        {
            // FILE_RENAME_INFORMATION: ReplaceIfExists stays false; the directory handle binds the
            // destination to the folder checked by the caller. The Win32 wrapper normalizes relative
            // names against the process directory on some Windows versions. The native API preserves
            // the supplied RootDirectory, avoiding a fallback to a path-based move.
            for (var i = 0; i < length; i++) Marshal.WriteByte(memory, i, 0);
            Marshal.WriteIntPtr(memory, rootOffset, destinationDirectory.DangerousGetHandle());
            Marshal.WriteInt32(memory, lengthOffset, bytes.Length);
            Marshal.Copy(bytes, 0, memory + nameOffset, bytes.Length);
            var status = NtSetInformationFile(file, out _, memory, (uint)length, 10);
            GC.KeepAlive(destinationDirectory);
            return status == 0 ? 0 : unchecked((int)RtlNtStatusToDosError(status));
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    /// <summary>
    /// Identity of a path queried on a handle opened for attributes only. Directory entries can lag behind a
    /// write made through another hard link; the file's own record cannot, and every write moves its change time.
    /// </summary>
    internal static FileIdentity? CurrentIdentity(string path)
    {
        using var handle = CreateFileW(Native(path), 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Identity(handle);
    }

    /// <summary>
    /// Volume and file identifier of a folder, following links: two spellings of one folder, such as a NAS by
    /// name and by address, share them even when no path text matches. Null where the file system has none.
    /// </summary>
    internal static (ulong Volume, ulong Low, ulong High)? ObjectId(string path)
    {
        using var handle = CreateFileW(Native(path), 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        return GetFileInformationByHandleEx(handle, 18, out IdInfo id, (uint)Marshal.SizeOf<IdInfo>()) && (id.Low | id.High) != 0
            ? (id.Volume, id.Low, id.High) : null;
    }

    /// <summary>
    /// The path as Win32 should take it, unrewritten: \\?\ lifts the 260-character limit whatever the system setting
    /// (the app manifest alone is not enough), and keeps a final dot or space that Windows would otherwise strip.
    /// A path not fully qualified, with "/" or with "." or ".." parts, is left to the usual rules.
    /// </summary>
    internal static string Native(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(path) || path.Contains('/') || path.Split('\\').Skip(1).Any(part => part is "." or ".."))
            return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    /// <summary>The part of a listed path below the root, exactly as listed: GetRelativePath would strip a final dot or space.</summary>
    internal static string RelativeTo(string root, string fullName)
    {
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return fullName.Length > prefix.Length && fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fullName[prefix.Length..] : Path.GetRelativePath(root, fullName);
    }

    /// <summary>Whether a folder distinguishes names by case (WSL, per-folder NTFS setting): Readme and README are two files there.</summary>
    internal static bool CaseSensitive(string directory)
    {
        using var handle = CreateFileW(Native(directory), 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        return !handle.IsInvalid && GetFileInformationByHandleEx(handle, 23, out uint flags, sizeof(uint)) && (flags & 1) != 0;
    }

    /// <summary>
    /// Whether the volume keeps a change time that every write moves: NTFS and ReFS, a share included. On FAT32 or
    /// exFAT a write that restores the file's date would look unchanged, so saved hashes must not be trusted there.
    /// </summary>
    internal static bool KeepsChangeTime(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var root = new StringBuilder(32768);
        var name = new StringBuilder(64);
        try
        {
            return GetVolumePathNameW(Path.GetFullPath(path), root, (uint)root.Capacity) &&
                GetVolumeInformationW(root.ToString(), null, 0, out _, out _, out _, name, (uint)name.Capacity) &&
                name.ToString() is "NTFS" or "ReFS";
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Where an open file or folder really is, after every link: the same form as <see cref="CanonicalDirectory"/>.</summary>
    internal static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        var value = buffer.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) value = @"\\" + value[8..];
        else if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value[4..];
        return Path.TrimEndingDirectorySeparator(value);
    }

    /// <summary>Same place, compared the way Windows names it: case-insensitive, normalized separators.</summary>
    internal static bool SamePlace(string actual, string expected) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase);

    /// <summary>Deletes the file behind a handle opened with delete access; no path is looked up again.</summary>
    internal static bool DeleteByHandle(SafeFileHandle handle)
    {
        byte remove = 1;
        return SetFileInformationByHandle(handle, 4, ref remove, 1);
    }

    /// <summary>
    /// A folder opened for its attributes and dates without following a link: the metadata applied through it
    /// lands on that folder, whatever happens to the path meanwhile.
    /// </summary>
    internal static SafeFileHandle OpenForMetadata(string path)
    {
        var handle = CreateFileW(Native(path), 0x80 | 0x100, FileShare.ReadWrite, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        if (Access(handle) == EntryAccess.Link)
        {
            handle.Dispose();
            throw new IOException(L.T("Un lien ou une jonction remplace le dossier de destination.", "A link or junction has replaced the destination folder."));
        }
        return handle;
    }

    /// <summary>A directory handle that does not follow links, for renames bound to that exact folder.</summary>
    internal static SafeFileHandle OpenDirectory(string path)
    {
        var handle = CreateFileW(Native(path), 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        if (Access(handle) == EntryAccess.Link)
        {
            handle.Dispose();
            throw new IOException(L.T("Un lien ou une jonction remplace le dossier de destination.", "A link or junction has replaced the destination folder."));
        }
        return handle;
    }

    /// <summary>
    /// Identifies the disk behind a path and whether it is an internal SSD, without administrator rights.
    /// Anything uncertain is reported as Unknown, which is read one file at a time like a hard disk. USB disks
    /// are never treated as SSDs: some USB bridges report no seek penalty for spinning disks.
    /// </summary>
    internal static StorageDevice Storage(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) full = @"\\" + full[8..];
        else if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal)) full = full[4..];
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return new StorageDevice("net:" + full.Split('\\', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant(), StorageKind.Network);
        var volume = new StringBuilder(32768);
        if (!OperatingSystem.IsWindows() || !GetVolumePathNameW(full, volume, (uint)volume.Capacity))
            return new StorageDevice("path:" + Path.GetPathRoot(full), StorageKind.Unknown);
        var root = volume.ToString();
        if (GetDriveTypeW(root) == 4) return new StorageDevice("net:" + root.ToUpperInvariant(), StorageKind.Network);
        var name = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPointW(root, name, (uint)name.Capacity))
            return new StorageDevice("vol:" + root.ToUpperInvariant(), StorageKind.Unknown);
        using var device = CreateFileW(@"\\.\" + name.ToString()[4..].TrimEnd('\\'), 0, FileShare.ReadWrite, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (device.IsInvalid) return new StorageDevice("vol:" + name, StorageKind.Unknown);
        var extents = new byte[256];
        var key = DeviceIoControl(device, 0x00560000, IntPtr.Zero, 0, extents, (uint)extents.Length, out _, IntPtr.Zero) && BitConverter.ToUInt32(extents, 0) > 0
            ? "disk:" + string.Join("+", Enumerable.Range(0, (int)Math.Min(BitConverter.ToUInt32(extents, 0), 10)).Select(i => BitConverter.ToUInt32(extents, 8 + i * 24)).Distinct())
            : "vol:" + name;
        // STORAGE_PROPERTY_QUERY: StorageDeviceProperty (0) gives the bus, StorageDeviceSeekPenaltyProperty (7) the spinning.
        var descriptor = new byte[1024];
        if (!QueryProperty(device, 0, descriptor) || descriptor[28] is 7 or 0) return new StorageDevice(key, StorageKind.Unknown);
        var penalty = new byte[12];
        if (!QueryProperty(device, 7, penalty)) return new StorageDevice(key, StorageKind.Unknown);
        return new StorageDevice(key, penalty[8] == 0 ? StorageKind.SolidState : StorageKind.Rotational);
    }

    private static bool QueryProperty(SafeFileHandle device, uint property, byte[] output)
    {
        var query = new byte[12];
        BitConverter.TryWriteBytes(query.AsSpan(0, 4), property);
        return DeviceIoControl(device, 0x002D1400, query, (uint)query.Length, output, (uint)output.Length, out _, IntPtr.Zero);
    }

    /// <summary>Dates and identity of an open file in two queries, instead of one query per value.</summary>
    internal static OpenedFile Describe(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 0, out BasicInfo basic, (uint)Marshal.SizeOf<BasicInfo>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        FileIdentity? identity = GetFileInformationByHandleEx(handle, 18, out IdInfo id, (uint)Marshal.SizeOf<IdInfo>())
            ? new FileIdentity(id.Volume, id.Low, id.High, basic.ChangeTime) : null;
        return new OpenedFile(DateTime.FromFileTimeUtc(basic.CreationTime), DateTime.FromFileTimeUtc(basic.LastWriteTime), identity);
    }

    /// <summary>
    /// Attributes, size and dates of a path in one query. Each separate .NET property used to cost its own
    /// round trip, which dominates on network shares. Errors keep their native code for the retry policy.
    /// </summary>
    internal static PathMetadata Stat(string path)
    {
        if (!GetFileAttributesExW(Native(path), 0, out var data)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new PathMetadata((FileAttributes)data.Attributes, ((long)data.SizeHigh << 32) | data.SizeLow,
            DateTime.FromFileTimeUtc(data.CreationTime), DateTime.FromFileTimeUtc(data.LastWriteTime));
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct IdInfo { public ulong Volume, Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public uint Attributes, ReparseTag; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct AttributeData { public uint Attributes; public long CreationTime, LastAccessTime, LastWriteTime; public uint SizeHigh, SizeLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern bool GetFileAttributesExW(string name, int level, out AttributeData data);
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
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out uint info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern IntPtr FindFirstFileExW(string name, int infoLevel, out FindData data, int searchOp, IntPtr filter, int flags);
    [DllImport("kernel32.dll")] private static extern bool FindClose(IntPtr search);
    [DllImport("ntdll.dll")] private static extern sbyte RtlSetProcessPlaceholderCompatibilityMode(sbyte mode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern bool GetVolumePathNameW(string path, StringBuilder volume, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? volumeName, uint volumeNameSize, out uint serial, out uint maxComponent, out uint flags, StringBuilder fileSystem, uint fileSystemSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder name, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputSize, byte[] output, uint outputSize, out uint returned, IntPtr overlapped);
    [StructLayout(LayoutKind.Sequential)] private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }
    [DllImport("ntdll.dll", ExactSpelling = true)] private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatusBlock ioStatus, IntPtr information, uint length, int informationClass);
    [DllImport("ntdll.dll", ExactSpelling = true)] private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref byte info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
}
