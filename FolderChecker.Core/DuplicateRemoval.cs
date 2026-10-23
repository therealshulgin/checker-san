using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

public enum DuplicateRemovalMode { Quarantine, Permanent, RecycleBin }

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
    /// <summary>Outcomes that are not errors but differ from the request, such as Windows deleting a file too large to recycle.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool Cancelled { get; init; }
    public long BytesRemoved { get; init; }
    public long BytesMoved { get; init; }
    /// <summary>Bytes now in the Recycle Bin; the space is freed when it is emptied.</summary>
    public long BytesRecycled { get; init; }
    public string? QuarantinePath { get; init; }
    public bool Complete => !Cancelled && Errors.Count == 0;
}

/// <summary>A quarantine that can still be restored: its manifest lists the files moved there.</summary>
public sealed record QuarantineSession(string Path, DateTimeOffset CreatedAt, int Files, int Waiting);

public sealed record RestoreResult(int Restored, IReadOnlyList<string> Errors)
{
    public bool Complete => Errors.Count == 0;
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
    private const string ManifestSuffix = ".manifest.jsonl";

    static DuplicateRemoval() => FileSafety.Initialize();

    /// <summary>CSV journal receiving one line per removal or restoration. Nothing is removed if it cannot be written.</summary>
    public string? LogPath { get; init; }

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
        var log = LogPath;
        // The shell may show its own dialog (file too large to recycle), which needs a single-threaded apartment.
        return mode == DuplicateRemovalMode.RecycleBin
            ? OnStaThread(() => Remove(snapshot, selection, mode, log, progress, cancellationToken))
            : Task.Run(() => Remove(snapshot, selection, mode, log, progress, cancellationToken));
    }

    /// <summary>Only local fixed drives have a Recycle Bin; elsewhere Windows would delete permanently.</summary>
    public static bool RecycleBinAvailable(string root)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(root)) return false;
        var full = Path.GetFullPath(root);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var drive = Path.GetPathRoot(full);
        return drive is not null && GetDriveTypeW(drive) == 3;
    }

    private static Task<T> OnStaThread<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception exception) { completion.SetException(exception); }
        }) { IsBackground = true, Name = "Checker San Recycle Bin" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static bool IsManagedQuarantine(string directory)
    {
        try
        {
            if (!string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)),
                    QuarantineDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                FileSafety.Access(directory, File.GetAttributes(directory)) == EntryAccess.Link)
                return false;
            var path = Path.Combine(directory, MarkerName);
            if (FileSafety.Access(path, File.GetAttributes(path)) == EntryAccess.Link) return false;
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
        IReadOnlyCollection<string> selectedPaths, DuplicateRemovalMode mode, string? logPath,
        IProgress<DuplicateRemovalProgress>? progress, CancellationToken token)
    {
        var completed = new List<string>();
        var errors = new List<string>();
        var warnings = new List<string>();
        long bytesRemoved = 0, bytesMoved = 0, bytesRecycled = 0, bytesVerified = 0;
        string? quarantineSession = null;
        FileStream? manifest = null;
        ActionLog? log = null;
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
                throw new PlatformNotSupportedException(L.T("Cette opération nécessite Windows.", "This operation requires Windows."));
            if (!Enum.IsDefined(mode))
                throw new InvalidOperationException(L.T("Mode de suppression inconnu.", "Unknown removal mode."));
            if (scan.Cancelled || !scan.SnapshotVerified || scan.Errors.Count != 0)
                throw new InvalidOperationException(L.T("Une recherche de doublons complète et vérifiée est nécessaire.", "A complete and verified duplicate search is required."));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scan.Root));
            if (mode == DuplicateRemovalMode.RecycleBin && !RecycleBinAvailable(root))
                throw new InvalidOperationException(L.T("Ce dossier n’a pas de Corbeille (partage réseau ou lecteur amovible) : choisissez la mise à l’écart.", "This folder has no Recycle Bin (network share or removable drive): choose to move files aside."));
            var plan = BuildPlan(scan, root, selectedPaths);
            if (plan.Count == 0)
                return new DuplicateRemovalResult();
            // Opened before any change: a removal that cannot be recorded does not happen.
            log = ActionLog.Open(logPath);
            using var directories = new DirectoryLocks(root);
            var canonicalRoot = FileSafety.CanonicalDirectory(root);
            if (!string.Equals(canonicalRoot, scan.CanonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.T("Le dossier analysé a changé de destination. Relancez la recherche.", "The scanned folder's physical location has changed. Run the search again."));

            foreach (var group in plan)
            {
                token.ThrowIfCancellationRequested();
                // All selected files in a group are validated before its first mutation.
                var opened = new List<(DuplicateFile File, FileStream Stream)>();
                try
                {
                    directories.PinParent(group.Keeper.FullPath);
                    using var keeper = OpenVerified(group.Keeper, group.Hash, false, canonicalRoot, token,
                        count => { bytesVerified += count; Report(L.T("Vérification de la copie conservée", "Verifying the copy to keep"), group.Keeper.FullPath); });
                    var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { PhysicalIdentity(FileSafety.Identity(keeper.SafeFileHandle)!) };
                    foreach (var file in group.Selected)
                    {
                        token.ThrowIfCancellationRequested();
                        directories.PinParent(file.FullPath);
                        var stream = OpenVerified(file, group.Hash, true, canonicalRoot, token,
                            count => { bytesVerified += count; Report(L.T("Vérification avant suppression", "Verifying before removal"), file.FullPath); },
                            shellMayMove: mode == DuplicateRemovalMode.RecycleBin);
                        opened.Add((file, stream));
                        if (!identities.Add(PhysicalIdentity(FileSafety.Identity(stream.SafeFileHandle)!)))
                            throw new IOException(L.T("Plusieurs chemins désignent le même fichier physique. Aucun fichier de ce groupe n’a été supprimé.", "Several paths refer to the same physical file. No file in this group has been removed."));
                    }
                    token.ThrowIfCancellationRequested();
                    foreach (var item in opened)
                    {
                        token.ThrowIfCancellationRequested();
                        if (mode == DuplicateRemovalMode.Quarantine)
                        {
                            if (quarantineSession is null)
                            {
                                quarantineSession = CreateQuarantine(root, directories);
                                manifest = CreateManifest(quarantineSession, root);
                            }
                            var destination = Path.Combine(quarantineSession, item.File.RelativePath);
                            directories.CreateAndPinParent(destination);
                            var parent = directories.Pin(Path.GetDirectoryName(destination)!);
                            // Recorded before the move: restoration skips an entry whose file never arrived.
                            AppendManifest(manifest!, new ManifestEntry { RelativePath = item.File.RelativePath, Hash = group.Hash, Size = item.File.Size });
                            RenameByHandle(item.Stream.SafeFileHandle, parent, Path.GetFileName(destination));
                            item.Stream.Dispose();
                            bytesMoved += item.File.Size;
                            log.Write(L.T("Mis à l’écart", "Moved aside"), item.File.FullPath, group.Hash, item.File.Size, group.Keeper.FullPath, destination);
                        }
                        else if (mode == DuplicateRemovalMode.RecycleBin)
                        {
                            Recycle(item.File.FullPath);
                            var outcome = RecycledOutcome(item.Stream.SafeFileHandle, Path.Combine(canonicalRoot, item.File.RelativePath));
                            if (outcome is null)
                                throw new OperationCanceledException(L.T("Mise à la Corbeille annulée dans la fenêtre de Windows.", "Moving to the Recycle Bin was cancelled in the Windows dialog."));
                            item.Stream.Dispose();
                            if (outcome.Length > 0)
                            {
                                bytesRecycled += item.File.Size;
                                log.Write(L.T("Mis à la Corbeille", "Moved to the Recycle Bin"), item.File.FullPath, group.Hash, item.File.Size, group.Keeper.FullPath, outcome);
                            }
                            else
                            {
                                bytesRemoved += item.File.Size;
                                warnings.Add(L.F($"Trop volumineux pour la Corbeille, supprimé définitivement après confirmation dans Windows : {item.File.FullPath}", $"Too large for the Recycle Bin, deleted permanently after confirmation in Windows: {item.File.FullPath}"));
                                log.Write(L.T("Supprimé définitivement (trop volumineux pour la Corbeille)", "Deleted permanently (too large for the Recycle Bin)"), item.File.FullPath, group.Hash, item.File.Size, group.Keeper.FullPath, "");
                            }
                        }
                        else
                        {
                            byte remove = 1;
                            if (!SetFileInformationByHandle(item.Stream.SafeFileHandle, 4, ref remove, 1))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Windows a refusé la suppression du fichier vérifié.", "Windows refused to delete the verified file."));
                            item.Stream.Dispose();
                            bytesRemoved += item.File.Size;
                            log.Write(L.T("Supprimé définitivement", "Deleted permanently"), item.File.FullPath, group.Hash, item.File.Size, group.Keeper.FullPath, "");
                        }
                        completed.Add(item.File.FullPath);
                        Report(mode switch
                        {
                            DuplicateRemovalMode.Quarantine => L.T("Doublon mis à l’écart", "Duplicate quarantined"),
                            DuplicateRemovalMode.RecycleBin => L.T("Doublon mis à la Corbeille", "Duplicate moved to the Recycle Bin"),
                            _ => L.T("Doublon supprimé", "Duplicate deleted")
                        }, item.File.FullPath);
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
        finally
        {
            manifest?.Dispose();
            log?.Dispose();
        }
        return new DuplicateRemovalResult
        {
            DeletedPaths = completed, Errors = errors, Warnings = warnings, Cancelled = cancelled,
            BytesMoved = bytesMoved, BytesRemoved = bytesRemoved, BytesRecycled = bytesRecycled, QuarantinePath = quarantineSession
        };
    }

    private static List<RemovalGroup> BuildPlan(DuplicateResult scan, string root,
        IReadOnlyCollection<string> selectedPaths)
    {
        var selection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in selectedPaths)
        {
            if (!Path.IsPathFullyQualified(selected) || !selection.Add(Path.GetFullPath(selected)))
                throw new InvalidOperationException(L.T("La sélection contient un chemin invalide ou répété.", "The selection contains an invalid or repeated path."));
        }
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<RemovalGroup>();
        foreach (var group in scan.Groups)
        {
            if (group.Hash.Length != 64 || !group.Hash.All(Uri.IsHexDigit) || group.Files.Count < 2)
                throw new InvalidOperationException(L.T("Un groupe de doublons est invalide.", "A duplicate group is invalid."));
            foreach (var file in group.Files)
            {
                ValidatePath(root, file);
                if (file.Size != group.Size || !known.Add(Path.GetFullPath(file.FullPath)))
                    throw new InvalidOperationException(L.T("Un fichier apparaît dans plusieurs groupes ou possède une taille incohérente.", "A file appears in multiple groups or has an inconsistent size."));
            }
            var selected = group.Files.Where(file => selection.Contains(Path.GetFullPath(file.FullPath))).ToList();
            if (selected.Count == 0) continue;
            var keeper = group.Files.FirstOrDefault(file => !selection.Contains(Path.GetFullPath(file.FullPath)));
            if (keeper is null)
                throw new InvalidOperationException(L.T("Conservez au moins un fichier de chaque groupe de doublons.", "Keep at least one file in each duplicate group."));
            groups.Add(new RemovalGroup(group.Hash, keeper, selected));
        }
        if (selection.Any(path => !known.Contains(path)))
            throw new InvalidOperationException(L.T("La sélection contient un fichier absent des doublons vérifiés.", "The selection contains a file that is not among the verified duplicates."));
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
            throw new InvalidOperationException(L.T("Le chemin d’un doublon est invalide ou sort du dossier analysé.", "A duplicate's path is invalid or outside the scanned folder."));
    }

    private static FileStream OpenVerified(DuplicateFile file, string hash, bool forRemoval,
        string canonicalRoot, CancellationToken token, Action<int> onBytes, bool shellMayMove = false)
    {
        // Writers are always denied. Only the Recycle Bin mode lets the shell move the verified file;
        // the outcome is then checked through this same handle.
        var handle = CreateFileW(file.FullPath, GenericRead | (forRemoval && !shellMayMove ? DeleteAccess : 0),
            shellMayMove ? FileShare.Read | FileShare.Delete : FileShare.Read, IntPtr.Zero, 3, OpenReparsePoint | SequentialScan, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, L.F($"Impossible de verrouiller « {file.RelativePath} ».", $"Unable to lock ‘{file.RelativePath}’."));
        }
        FileStream? stream = null;
        try
        {
            if (!GetFileInformationByHandleEx(handle, 0, out BasicInfo info, (uint)Marshal.SizeOf<BasicInfo>()) ||
                !GetFileSizeEx(handle, out long length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var identity = FileSafety.Identity(handle);
            if (DuplicateEngine.HardLinkCount(handle) != 1)
                throw new IOException(L.F($"« {file.RelativePath} » possède des liens physiques, ou leur nombre ne peut pas être vérifié. Aucun fichier de ce groupe ne sera supprimé.", $"‘{file.RelativePath}’ has hard links, or their count cannot be verified. No file in this group will be removed."));
            // The handle was opened without following links: a link is refused, a local cloud file is read as is.
            if (((FileAttributes)info.Attributes & FileAttributes.Directory) != 0 || FileSafety.Access(handle) != EntryAccess.Normal ||
                length != file.Size || DateTime.FromFileTimeUtc(info.LastWriteTime) != file.LastWriteUtc ||
                DateTime.FromFileTimeUtc(info.CreationTime) != file.CreationUtc ||
                (FileAttributes)info.Attributes != file.Attributes || !SameIdentity(identity, file.Identity))
                throw new IOException(L.F($"« {file.RelativePath} » a changé depuis le scan, ou son identité ne peut pas être vérifiée.", $"‘{file.RelativePath}’ has changed since the scan, or its identity cannot be verified."));
            var canonicalFile = FinalPath(handle);
            if (!string.Equals(canonicalFile, Path.Combine(canonicalRoot, file.RelativePath), StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.F($"Le chemin de « {file.RelativePath} » a changé de destination.", $"The physical location of ‘{file.RelativePath}’ has changed."));
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
                throw new IOException(L.F($"Le contenu de « {file.RelativePath} » a changé. Aucun autre fichier de ce groupe ne sera supprimé.", $"The contents of ‘{file.RelativePath}’ have changed. No other file in this group will be removed."));
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
                throw new IOException(L.F($"Le dossier « {QuarantineDirectoryName} » existe déjà et n’est pas une quarantaine Checker San.", $"The folder ‘{QuarantineDirectoryName}’ already exists and is not a Checker San quarantine."));
        }
        else
        {
            if (!CreateDirectoryW(quarantine, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Impossible de créer une quarantaine réservée à Checker San.", "Unable to create a quarantine reserved for Checker San."));
            directories.Pin(quarantine);
            using var marker = new FileStream(Path.Combine(quarantine, MarkerName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(marker, new QuarantineMarker
                { Schema = 1, Application = "Checker San", Kind = "DuplicateQuarantine" });
            marker.Flush(true);
        }
        var session = Path.Combine(quarantine, Guid.NewGuid().ToString("N"));
        if (!CreateDirectoryW(session, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Impossible de créer une session de quarantaine.", "Unable to create a quarantine session."));
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
                throw new Win32Exception(error, L.F($"Windows a refusé la mise à l’écart (code {error}) ; aucune suppression définitive n’a été tentée.", $"Windows refused to quarantine the file (code {error}); permanent deletion was not attempted."));
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

    private static void Recycle(string path)
    {
        // The nuke warning lets Windows ask before deleting a file it cannot recycle, such as one too large.
        var operation = new ShellFileOperation { Function = 3, From = path + "\0", Flags = 0x4454 };
        var status = SHFileOperationW(ref operation);
        if (status != 0 && !operation.AnyOperationsAborted)
            throw new IOException(L.F($"Windows n’a pas pu mettre « {path} » à la Corbeille (code {status}).", $"Windows could not move ‘{path}’ to the Recycle Bin (code {status})."));
    }

    /// <summary>The Recycle Bin path of the verified file, "" if Windows deleted it instead, null if it was left in place.</summary>
    private static string? RecycledOutcome(SafeFileHandle handle, string expectedPath)
    {
        if (DuplicateEngine.DeletePending(handle) == true) return "";
        var current = FinalPath(handle);
        if (current.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase)) return current;
        if (string.Equals(current, expectedPath, StringComparison.OrdinalIgnoreCase)) return null;
        throw new IOException(L.F($"Le fichier vérifié a été déplacé vers un emplacement inattendu : {current}", $"The verified file was moved to an unexpected location: {current}"));
    }

    /// <summary>Quarantines that still hold files listed in their manifest, newest first.</summary>
    public static IReadOnlyList<QuarantineSession> QuarantineSessions(string root)
    {
        var quarantine = Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), QuarantineDirectoryName);
        var sessions = new List<QuarantineSession>();
        try
        {
            if (!Directory.Exists(quarantine) || !IsManagedQuarantine(quarantine)) return sessions;
            foreach (var manifestPath in Directory.EnumerateFiles(quarantine, "*" + ManifestSuffix))
            {
                var session = manifestPath[..^ManifestSuffix.Length];
                if (ReadManifest(manifestPath) is not { } manifest) continue;
                var waiting = manifest.Entries.Count(entry => File.Exists(Path.Combine(session, entry.RelativePath)));
                if (waiting > 0) sessions.Add(new QuarantineSession(session, manifest.Header.CreatedAt, manifest.Entries.Count, waiting));
            }
        }
        catch (Exception e) when (IsFilesystemError(e)) { }
        return sessions.OrderByDescending(session => session.CreatedAt).ToArray();
    }

    /// <summary>
    /// Moves quarantined files back to their original paths. A file is restored only if its contents still match
    /// the manifest and nothing occupies its original path; nothing is ever overwritten.
    /// </summary>
    public Task<RestoreResult> RestoreAsync(string sessionPath, CancellationToken cancellationToken = default)
    {
        var log = LogPath;
        return Task.Run(() => Restore(sessionPath, log, cancellationToken));
    }

    private static RestoreResult Restore(string sessionPath, string? logPath, CancellationToken token)
    {
        var errors = new List<string>();
        var restored = 0;
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionPath));
        var quarantine = Path.GetDirectoryName(session)!;
        var root = Path.GetDirectoryName(quarantine)!;
        if (!IsManagedQuarantine(quarantine) || ReadManifest(session + ManifestSuffix) is not { } manifest ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(manifest.Header.Root)), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("Cette mise à l’écart ne peut pas être restaurée : son manifeste est absent ou concerne un autre dossier.", "These files cannot be restored: the manifest is missing or belongs to another folder."));
        using var log = ActionLog.Open(logPath);
        foreach (var entry in manifest.Entries)
        {
            token.ThrowIfCancellationRequested();
            var stored = Path.Combine(session, entry.RelativePath);
            var original = Path.Combine(root, entry.RelativePath);
            if (!File.Exists(stored)) continue;
            try
            {
                if (File.Exists(original) || Directory.Exists(original))
                    throw new IOException(L.F($"Un élément occupe déjà « {entry.RelativePath} » : le fichier reste dans la mise à l’écart.", $"Something already occupies ‘{entry.RelativePath}’: the file stays aside."));
                using (var check = new FileStream(stored, FileMode.Open, FileAccess.Read, FileShare.Read))
                    if (check.Length != entry.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(check)), entry.Hash, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(L.F($"« {entry.RelativePath} » a changé depuis sa mise à l’écart : il n’est pas restauré.", $"‘{entry.RelativePath}’ changed after being moved aside: it is not restored."));
                CreateFoldersWithoutLinks(root, Path.GetDirectoryName(original)!);
                File.Move(stored, original, overwrite: false);
                restored++;
                log.Write(L.T("Restauré", "Restored"), stored, entry.Hash, entry.Size, "", original);
            }
            catch (Exception e) when (IsFilesystemError(e)) { errors.Add(e.Message); }
        }
        RemoveEmptyFolders(session);
        if (!Directory.Exists(session))
        {
            try { File.Delete(session + ManifestSuffix); }
            catch (Exception e) when (IsFilesystemError(e)) { }
        }
        return new RestoreResult(restored, errors);
    }

    private static void CreateFoldersWithoutLinks(string root, string directory)
    {
        var current = root;
        foreach (var part in Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            if (!Directory.Exists(current) && !File.Exists(current)) Directory.CreateDirectory(current);
            var attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == 0 || FileSafety.Access(current, attributes) == EntryAccess.Link)
                throw new IOException(L.F($"Un fichier ou un lien occupe le dossier d’origine : {current}", $"A file or link occupies the original folder: {current}"));
        }
    }

    private static void RemoveEmptyFolders(string directory)
    {
        try
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) RemoveEmptyFolders(child);
            // A non-recursive delete fails on anything left inside.
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch (Exception e) when (IsFilesystemError(e)) { }
    }

    private static FileStream CreateManifest(string session, string root)
    {
        var stream = new FileStream(session + ManifestSuffix, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        AppendLine(stream, JsonSerializer.SerializeToUtf8Bytes(new ManifestHeader { Root = root, CreatedAt = DateTimeOffset.Now }));
        return stream;
    }

    private static void AppendManifest(FileStream manifest, ManifestEntry entry) => AppendLine(manifest, JsonSerializer.SerializeToUtf8Bytes(entry));

    private static void AppendLine(FileStream stream, byte[] json)
    {
        stream.Write(json);
        stream.WriteByte((byte)'\n');
        stream.Flush(true);
    }

    private static (ManifestHeader Header, List<ManifestEntry> Entries)? ReadManifest(string path)
    {
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            if (reader.ReadLine() is not { } first || JsonSerializer.Deserialize<ManifestHeader>(first) is not { Schema: 1 } header ||
                string.IsNullOrWhiteSpace(header.Root) || !Path.IsPathFullyQualified(header.Root)) return null;
            var entries = new List<ManifestEntry>();
            for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
            {
                // A crash can only tear the last line; it describes a file that was never moved.
                ManifestEntry? entry;
                try { entry = JsonSerializer.Deserialize<ManifestEntry>(line); }
                catch (JsonException) { continue; }
                if (entry is null || !ValidRelative(entry.RelativePath) || entry.Size < 0 ||
                    entry.Hash.Length != 64 || !entry.Hash.All(Uri.IsHexDigit)) continue;
                entries.Add(entry);
            }
            return (header, entries);
        }
        catch (Exception e) when (IsFilesystemError(e) || e is JsonException) { return null; }
    }

    private static bool ValidRelative(string relative) =>
        !string.IsNullOrWhiteSpace(relative) && !Path.IsPathRooted(relative) &&
        !relative.Split(['/', '\\']).Any(part => part is "" or "." or ".." || part.Contains(':'));

    private sealed record ManifestHeader
    {
        public int Schema { get; init; } = 1;
        public string Root { get; init; } = "";
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed record ManifestEntry
    {
        public string RelativePath { get; init; } = "";
        public string Hash { get; init; } = "";
        public long Size { get; init; }
    }

    /// <summary>Journal of removals and restorations: one CSV line per action, written as soon as it happens.</summary>
    private sealed class ActionLog : IDisposable
    {
        private readonly StreamWriter? _writer;
        private ActionLog(StreamWriter? writer) => _writer = writer;

        internal static ActionLog Open(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return new ActionLog(null);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            // A BOM is written only at the start of a new file, so Excel reads accents correctly.
            var writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(true));
            if (writer.BaseStream.Length == 0)
                writer.WriteLine(Csv.Line([L.T("Date", "Date"), L.T("Action", "Action"), L.T("Chemin", "Path"), "SHA-256",
                    L.T("Taille (octets)", "Size (bytes)"), L.T("Copie conservée", "Kept copy"), L.T("Destination", "Destination")]));
            writer.Flush();
            return new ActionLog(writer);
        }

        internal void Write(string action, string path, string hash, long size, string keeper, string destination)
        {
            if (_writer is null) return;
            _writer.WriteLine(Csv.Line([DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), action, path, hash,
                size.ToString(CultureInfo.InvariantCulture), keeper, destination]));
            _writer.Flush();
        }

        public void Dispose() => _writer?.Dispose();
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
            if (!FileSafety.IsWithin(_root, directory)) throw new IOException(L.T("Le dossier sort de la racine analysée.", "The folder is outside the scanned root."));
            if (_handles.TryGetValue(directory, out var existing)) return existing;
            if (!string.Equals(directory, _root, StringComparison.OrdinalIgnoreCase))
                Pin(Path.GetDirectoryName(directory)!);
            var handle = CreateFileW(directory, 0x80, FileShare.ReadWrite, IntPtr.Zero, 3,
                BackupSemantics | OpenReparsePoint, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, L.F($"Impossible de verrouiller le dossier « {directory} ».", $"Unable to lock folder ‘{directory}’."));
            }
            if (!GetFileInformationByHandleEx(handle, 0, out BasicInfo info, (uint)Marshal.SizeOf<BasicInfo>()) ||
                ((FileAttributes)info.Attributes & FileAttributes.Directory) == 0 ||
                FileSafety.Access(handle) == EntryAccess.Link)
            {
                handle.Dispose();
                throw new IOException(L.T("Une jonction ou un lien ne peut pas être utilisé pour supprimer des doublons.", "A junction or link cannot be used to remove duplicates."));
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string root);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHFileOperationW(ref ShellFileOperation operation);
}
