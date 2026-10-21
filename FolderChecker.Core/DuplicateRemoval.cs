using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

public enum DuplicateRemovalMode { Quarantine, Permanent }

public sealed record DuplicateRemovalProgress
{
    public string Phase { get; init; } = "";
    public string CurrentPath { get; init; } = "";
    public int CompletedFiles { get; init; }
    public int TotalFiles { get; init; }
    public long BytesVerified { get; init; }
}

public sealed record DuplicateRemovalResult
{
    public IReadOnlyList<string> DeletedPaths { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public bool Cancelled { get; init; }
    public long BytesRemoved { get; init; }
    public long BytesMoved { get; init; }
    public string? QuarantinePath { get; init; }
    public bool Complete => !Cancelled && Errors.Count == 0;
}

/// <summary>
/// Mutations use already verified Windows handles. One unselected, fully hashed
/// copy remains locked against writing and deletion until its group is finished.
/// </summary>
public sealed class DuplicateRemoval
{
    public const string QuarantineDirectoryName = ".CheckerSan-Quarantine";
    private const string MarkerName = "checker-san-quarantine.json";
    private const uint GenericRead = 0x80000000, DeleteAccess = 0x10000;
    private const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    private const uint SequentialScan = 0x08000000;

    public Task<DuplicateRemovalResult> DeleteAsync(DuplicateResult scan,
        IReadOnlyCollection<string> selectedPaths, DuplicateRemovalMode mode,
        IProgress<DuplicateRemovalProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        var selection = selectedPaths.ToArray();
        var snapshot = scan with
        {
            Groups = scan.Groups.Select(group => group with { Files = group.Files.ToArray() }).ToArray(),
            Errors = scan.Errors.ToArray()
        };
        return Task.Run(() => Remove(snapshot, selection, mode, progress, cancellationToken));
    }

    internal static bool IsManagedQuarantine(string directory)
    {
        try
        {
            if (!string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)),
                    QuarantineDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return false;
            var path = Path.Combine(directory, MarkerName);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 4096) return false;
            var marker = JsonSerializer.Deserialize<QuarantineMarker>(file);
            return marker is { Schema: 1, Application: "Checker San", Kind: "DuplicateQuarantine" };
        }
        catch (Exception e) when (IsFilesystemError(e) || e is JsonException)
        {
            return false;
        }
    }

    private static DuplicateRemovalResult Remove(DuplicateResult scan,
        IReadOnlyCollection<string> selectedPaths, DuplicateRemovalMode mode,
        IProgress<DuplicateRemovalProgress>? progress, CancellationToken token)
    {
        var completed = new List<string>();
        var errors = new List<string>();
        long bytesRemoved = 0, bytesMoved = 0, bytesVerified = 0;
        string? quarantineSession = null;
        var cancelled = false;
        void Report(string phase, string path) => progress?.Report(new DuplicateRemovalProgress
        {
            Phase = phase, CurrentPath = path, CompletedFiles = completed.Count,
            TotalFiles = selectedPaths.Count, BytesVerified = bytesVerified
        });
        try
        {
            token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Cette opération nécessite Windows.");
            if (mode is not DuplicateRemovalMode.Quarantine and not DuplicateRemovalMode.Permanent)
                throw new InvalidOperationException("Mode de suppression inconnu.");
            if (scan.Cancelled || !scan.SnapshotVerified || scan.Errors.Count != 0)
                throw new InvalidOperationException("Une recherche de doublons complète et vérifiée est nécessaire.");
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scan.Root));
            var plan = BuildPlan(scan, root, selectedPaths);
            if (plan.Count == 0)
                return new DuplicateRemovalResult();
            using var directories = new DirectoryLocks(root);
            var canonicalRoot = FileSafety.CanonicalDirectory(root);
            if (!string.Equals(canonicalRoot, scan.CanonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Le dossier analysé a changé de destination. Relancez la recherche.");

            foreach (var group in plan)
            {
                token.ThrowIfCancellationRequested();
                // All selected files in a group are validated before its first mutation.
                var opened = new List<(DuplicateFile File, FileStream Stream)>();
                try
                {
                    directories.PinParent(group.Keeper.FullPath);
                    using var keeper = OpenVerified(group.Keeper, group.Hash, false, canonicalRoot, token,
                        count => { bytesVerified += count; Report("Vérification de la copie conservée", group.Keeper.FullPath); });
                    var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { PhysicalIdentity(FileSafety.Identity(keeper.SafeFileHandle)!) };
                    foreach (var file in group.Selected)
                    {
                        token.ThrowIfCancellationRequested();
                        directories.PinParent(file.FullPath);
                        var stream = OpenVerified(file, group.Hash, true, canonicalRoot, token,
                            count => { bytesVerified += count; Report("Vérification avant suppression", file.FullPath); });
                        opened.Add((file, stream));
                        if (!identities.Add(PhysicalIdentity(FileSafety.Identity(stream.SafeFileHandle)!)))
                            throw new IOException("Plusieurs chemins désignent le même fichier physique. Aucun fichier de ce groupe n’a été supprimé.");
                    }
                    token.ThrowIfCancellationRequested();
                    foreach (var item in opened)
                    {
                        token.ThrowIfCancellationRequested();
                        if (mode == DuplicateRemovalMode.Quarantine)
                        {
                            quarantineSession ??= CreateQuarantine(root, directories);
                            var destination = Path.Combine(quarantineSession, item.File.RelativePath);
                            directories.CreateAndPinParent(destination);
                            var parent = directories.Pin(Path.GetDirectoryName(destination)!);
                            RenameByHandle(item.Stream.SafeFileHandle, parent, Path.GetFileName(destination));
                            item.Stream.Dispose();
                            bytesMoved += item.File.Size;
                        }
                        else
                        {
                            byte remove = 1;
                            if (!SetFileInformationByHandle(item.Stream.SafeFileHandle, 4, ref remove, 1))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows a refusé la suppression du fichier vérifié.");
                            item.Stream.Dispose();
                            bytesRemoved += item.File.Size;
                        }
                        completed.Add(item.File.FullPath);
                        Report(mode == DuplicateRemovalMode.Quarantine ? "Doublon mis à l’écart" : "Doublon supprimé",
                            item.File.FullPath);
                    }
                }
                finally
                {
                    foreach (var item in opened) item.Stream.Dispose();
                }
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception e) when (IsFilesystemError(e))
        {
            errors.Add(e.Message);
        }
        return new DuplicateRemovalResult
        {
            DeletedPaths = completed, Errors = errors, Cancelled = cancelled,
            BytesMoved = bytesMoved, BytesRemoved = bytesRemoved, QuarantinePath = quarantineSession
        };
    }

    private static List<RemovalGroup> BuildPlan(DuplicateResult scan, string root,
        IReadOnlyCollection<string> selectedPaths)
    {
        var selection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in selectedPaths)
        {
            if (!Path.IsPathFullyQualified(selected) || !selection.Add(Path.GetFullPath(selected)))
                throw new InvalidOperationException("La sélection contient un chemin invalide ou répété.");
        }
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<RemovalGroup>();
        foreach (var group in scan.Groups)
        {
            if (group.Hash.Length != 64 || !group.Hash.All(Uri.IsHexDigit) || group.Files.Count < 2)
                throw new InvalidOperationException("Un groupe de doublons est invalide.");
            foreach (var file in group.Files)
            {
                ValidatePath(root, file);
                if (file.Size != group.Size || !known.Add(Path.GetFullPath(file.FullPath)))
                    throw new InvalidOperationException("Un fichier apparaît dans plusieurs groupes ou possède une taille incohérente.");
            }
            var selected = group.Files.Where(file => selection.Contains(Path.GetFullPath(file.FullPath))).ToList();
            if (selected.Count == 0) continue;
            var keeper = group.Files.FirstOrDefault(file => !selection.Contains(Path.GetFullPath(file.FullPath)));
            if (keeper is null)
                throw new InvalidOperationException("Conservez au moins un fichier de chaque groupe de doublons.");
            groups.Add(new RemovalGroup(group.Hash, keeper, selected));
        }
        if (selection.Any(path => !known.Contains(path)))
            throw new InvalidOperationException("La sélection contient un fichier absent des doublons vérifiés.");
        return groups;
    }

    private static void ValidatePath(string root, DuplicateFile file)
    {
        if (string.IsNullOrWhiteSpace(file.RelativePath) || Path.IsPathRooted(file.RelativePath) ||
            file.RelativePath.Split(['/', '\\']).Any(part => part is "" or "." or ".." || part.Contains(':')) ||
            !Path.IsPathFullyQualified(file.FullPath) || file.Size < 0 ||
            !FileSafety.IsWithin(root, file.FullPath) ||
            !string.Equals(Path.GetFullPath(Path.Combine(root, file.RelativePath)), Path.GetFullPath(file.FullPath),
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(file.RelativePath.Split(['/', '\\'])[0], QuarantineDirectoryName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le chemin d’un doublon est invalide ou sort du dossier analysé.");
    }

    private static FileStream OpenVerified(DuplicateFile file, string hash, bool forRemoval,
        string canonicalRoot, CancellationToken token, Action<int> onBytes)
    {
        var handle = CreateFileW(file.FullPath, GenericRead | (forRemoval ? DeleteAccess : 0),
            FileShare.Read, IntPtr.Zero, 3, OpenReparsePoint | SequentialScan, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Impossible de verrouiller « {file.RelativePath} ».");
        }
        FileStream? stream = null;
        try
        {
            if (!GetFileInformationByHandleEx(handle, 0, out BasicInfo info, (uint)Marshal.SizeOf<BasicInfo>()) ||
                !GetFileSizeEx(handle, out long length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var identity = FileSafety.Identity(handle);
            if (DuplicateEngine.HardLinkCount(handle) != 1)
                throw new IOException($"« {file.RelativePath} » possède des liens physiques, ou leur nombre ne peut pas être vérifié. Aucun fichier de ce groupe ne sera supprimé.");
            if (((FileAttributes)info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                length != file.Size || DateTime.FromFileTimeUtc(info.LastWriteTime) != file.LastWriteUtc ||
                DateTime.FromFileTimeUtc(info.CreationTime) != file.CreationUtc ||
                (FileAttributes)info.Attributes != file.Attributes || !SameIdentity(identity, file.Identity))
                throw new IOException($"« {file.RelativePath} » a changé depuis le scan, ou son identité ne peut pas être vérifiée.");
            var canonicalFile = FinalPath(handle);
            if (!string.Equals(canonicalFile, Path.Combine(canonicalRoot, file.RelativePath), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Le chemin de « {file.RelativePath} » a changé de destination.");
            stream = new FileStream(handle, FileAccess.Read, 1, false);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var count = stream.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    digest.AppendData(buffer, 0, count);
                    onBytes(count);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            token.ThrowIfCancellationRequested();
            if (!string.Equals(Convert.ToHexString(digest.GetHashAndReset()), hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Le contenu de « {file.RelativePath} » a changé. Aucun autre fichier de ce groupe ne sera supprimé.");
            return stream;
        }
        catch
        {
            stream?.Dispose();
            handle.Dispose();
            throw;
        }
    }

    private static bool SameIdentity(FileIdentity? current, string? expected)
    {
        var parts = expected?.Split(':');
        if (current is null || parts is not { Length: 4 }) return false;
        var values = new ulong[4];
        for (var i = 0; i < parts.Length; i++)
            if (!ulong.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out values[i])) return false;
        return current.Volume == values[0] && current.IdLow == values[1] && current.IdHigh == values[2] &&
            unchecked((ulong)current.ChangeTime) == values[3];
    }

    private static string PhysicalIdentity(FileIdentity identity) => $"{identity.Volume:X}:{identity.IdLow:X}:{identity.IdHigh:X}";

    private static string CreateQuarantine(string root, DirectoryLocks directories)
    {
        var quarantine = Path.Combine(root, QuarantineDirectoryName);
        if (Directory.Exists(quarantine))
        {
            directories.Pin(quarantine);
            if (!IsManagedQuarantine(quarantine))
                throw new IOException($"Le dossier « {QuarantineDirectoryName} » existe déjà et n’est pas une quarantaine Checker San.");
        }
        else
        {
            if (!CreateDirectoryW(quarantine, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible de créer une quarantaine réservée à Checker San.");
            directories.Pin(quarantine);
            using var marker = new FileStream(Path.Combine(quarantine, MarkerName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(marker, new QuarantineMarker
                { Schema = 1, Application = "Checker San", Kind = "DuplicateQuarantine" });
            marker.Flush(true);
        }
        var session = Path.Combine(quarantine, Guid.NewGuid().ToString("N"));
        if (!CreateDirectoryW(session, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible de créer une session de quarantaine.");
        directories.Pin(session);
        return session;
    }

    private static void RenameByHandle(SafeFileHandle file, SafeFileHandle destinationDirectory, string name)
    {
        var bytes = Encoding.Unicode.GetBytes(name);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(uint);
        var memory = Marshal.AllocHGlobal(nameOffset + bytes.Length);
        try
        {
            // FILE_RENAME_INFORMATION: ReplaceIfExists stays false; the pinned
            // directory handle binds the destination to the folder checked above.
            // The Win32 wrapper normalizes relative names against the process
            // directory on some Windows versions. The native API preserves the
            // supplied RootDirectory, avoiding a fallback to a path-based move.
            for (var i = 0; i < nameOffset; i++) Marshal.WriteByte(memory, i, 0);
            Marshal.WriteIntPtr(memory, rootOffset, destinationDirectory.DangerousGetHandle());
            Marshal.WriteInt32(memory, lengthOffset, bytes.Length);
            Marshal.Copy(bytes, 0, memory + nameOffset, bytes.Length);
            var status = NtSetInformationFile(file, out _, memory, (uint)(nameOffset + bytes.Length), 10);
            if (status != 0)
            {
                var error = unchecked((int)RtlNtStatusToDosError(status));
                throw new Win32Exception(error, $"Windows a refusé la mise à l’écart (code {error}) ; aucune suppression définitive n’a été tentée.");
            }
            GC.KeepAlive(destinationDirectory);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    private static string FinalPath(SafeFileHandle handle)
    {
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        var path = buffer.ToString();
        return path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    private static bool IsFilesystemError(Exception e) => e is IOException or UnauthorizedAccessException
        or Win32Exception or ArgumentException or InvalidOperationException or NotSupportedException;

    private sealed record RemovalGroup(string Hash, DuplicateFile Keeper, List<DuplicateFile> Selected);
    private sealed record QuarantineMarker
    {
        public int Schema { get; init; }
        public string Application { get; init; } = "";
        public string Kind { get; init; } = "";
    }

    private sealed class DirectoryLocks : IDisposable
    {
        private readonly string _root;
        private readonly Dictionary<string, SafeFileHandle> _handles = new(StringComparer.OrdinalIgnoreCase);
        internal DirectoryLocks(string root) { _root = root; Pin(root); }
        internal SafeFileHandle Pin(string directory)
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!FileSafety.IsWithin(_root, directory)) throw new IOException("Le dossier sort de la racine analysée.");
            if (_handles.TryGetValue(directory, out var existing)) return existing;
            if (!string.Equals(directory, _root, StringComparison.OrdinalIgnoreCase))
                Pin(Path.GetDirectoryName(directory)!);
            var handle = CreateFileW(directory, 0x80, FileShare.ReadWrite, IntPtr.Zero, 3,
                BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, $"Impossible de verrouiller le dossier « {directory} ».");
            }
            if (!GetFileInformationByHandleEx(handle, 0, out BasicInfo info, (uint)Marshal.SizeOf<BasicInfo>()) ||
                ((FileAttributes)info.Attributes & FileAttributes.Directory) == 0 ||
                ((FileAttributes)info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                handle.Dispose();
                throw new IOException("Une jonction ou un lien ne peut pas être utilisé pour supprimer des doublons.");
            }
            _handles.Add(directory, handle);
            return handle;
        }
        internal void PinParent(string file) => Pin(Path.GetDirectoryName(file)!);
        internal void CreateAndPinParent(string file)
        {
            var parent = Path.GetDirectoryName(file)!;
            if (Directory.Exists(parent)) { Pin(parent); return; }
            CreateAndPinParent(parent);
            Directory.CreateDirectory(parent);
            Pin(parent);
        }
        public void Dispose() { foreach (var handle in _handles.Values.Reverse()) handle.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BasicInfo
    { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint Attributes; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out BasicInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileSizeEx(SafeFileHandle handle, out long size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref byte info, uint size);
    [StructLayout(LayoutKind.Sequential)] private struct IoStatusBlock
    { public IntPtr Status; public UIntPtr Information; }
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatusBlock ioStatus,
        IntPtr information, uint length, int informationClass);
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);
}
