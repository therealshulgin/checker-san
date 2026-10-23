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

    /// <summary>
    /// Only local fixed drives have a Recycle Bin; elsewhere Windows would delete permanently. The volume that
    /// really holds the folder decides, not the letter typed: a junction, a substituted drive or a folder used
    /// as a mount point can lead to a removable drive.
    /// </summary>
    public static bool RecycleBinAvailable(string root)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(root)) return false;
        var full = Path.GetFullPath(root);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        string real;
        // A folder not reachable yet (still being typed) is judged by its letter; a removal always runs on a real one.
        try { real = FileSafety.CanonicalDirectory(full); }
        catch (Exception e) when (IsFilesystemError(e)) { real = full; }
        if (real.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var volume = new StringBuilder(32768);
        return GetVolumePathNameW(Path.EndsInDirectorySeparator(real) ? real : real + Path.DirectorySeparatorChar, volume, (uint)volume.Capacity) &&
            GetDriveTypeW(volume.ToString()) == 3;
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

    /// <summary>A path inside a quarantine folder at any depth: such a copy is set aside, never kept in place of a live one.</summary>
    public static bool IsInQuarantine(string relativePath) =>
        relativePath.Split(['/', '\\']).SkipLast(1).Any(part => string.Equals(part, QuarantineDirectoryName, StringComparison.OrdinalIgnoreCase));

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
            var plan = BuildPlan(scan, root, selectedPaths, errors);
            if (plan.Count == 0)
                return new DuplicateRemovalResult { Errors = errors };
            // Opened before any change: a removal that cannot be recorded does not happen.
            log = ActionLog.Open(logPath);
            using var directories = new DirectoryLocks(root);
            var canonicalRoot = FileSafety.CanonicalDirectory(root);
            if (!string.Equals(canonicalRoot, scan.CanonicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.T("Le dossier analysé a changé de destination. Relancez la recherche.", "The scanned folder's physical location has changed. Run the search again."));

            // The file is already acted on when its line is written: it is counted first, so a journal that cannot
            // be written (full disk) loses no trace in the result, and stops every later action.
            void Record(string action, DuplicateFile file, string hash, string keeperPath, string destination)
            {
                completed.Add(file.FullPath);
                try { log!.Write(action, file.FullPath, hash, file.Size, keeperPath, destination); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new StopAll(L.F($"Retiré sans ligne au journal des actions : {file.FullPath} ({e.Message}). Les actions suivantes n’ont pas été faites.",
                        $"Removed without a line in the action journal: {file.FullPath} ({e.Message}). The following actions were not done."), e);
                }
            }
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
                        // No mode shares deletion while the group is verified: a verified file cannot be renamed
                        // away and replaced during the minutes the other files of the group take to read.
                        var stream = OpenVerified(file, group.Hash, true, canonicalRoot, token,
                            count => { bytesVerified += count; Report(L.T("Vérification avant suppression", "Verifying before removal"), file.FullPath); });
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
                                // Without a quarantine no group can be moved aside: that stops everything, once.
                                try
                                {
                                    quarantineSession = CreateQuarantine(root, directories);
                                    manifest = CreateManifest(quarantineSession, root, canonicalRoot);
                                }
                                catch (Exception e) when (IsFilesystemError(e)) { throw new StopAll(e.Message, e); }
                            }
                            var destination = Path.Combine(quarantineSession, item.File.RelativePath);
                            directories.CreateAndPinParent(destination);
                            var parent = directories.Pin(Path.GetDirectoryName(destination)!);
                            // Recorded before the move: restoration skips an entry whose file never arrived.
                            AppendManifest(manifest!, new ManifestEntry { RelativePath = item.File.RelativePath, Hash = group.Hash, Size = item.File.Size });
                            RenameByHandle(item.Stream.SafeFileHandle, parent, Path.GetFileName(destination));
                            item.Stream.Dispose();
                            bytesMoved += item.File.Size;
                            Record(L.T("Mis à l’écart", "Moved aside"), item.File, group.Hash, group.Keeper.FullPath, destination);
                        }
                        else if (mode == DuplicateRemovalMode.RecycleBin)
                        {
                            // The shell works on a path. A second handle, sharing deletion, follows the verified file:
                            // it must be the same file at the same place, and only then is the locked handle released,
                            // microseconds before the shell opens the path.
                            var expected = Path.Combine(canonicalRoot, item.File.RelativePath);
                            using var follower = OpenFollower(item.File.FullPath);
                            if (FileSafety.Identity(follower) is not { } followed || PhysicalIdentity(followed) != PhysicalIdentity(FileSafety.Identity(item.Stream.SafeFileHandle)!) ||
                                !string.Equals(FinalPath(follower), expected, StringComparison.OrdinalIgnoreCase))
                                throw new IOException(L.F($"« {item.File.RelativePath} » a changé entre sa vérification et la mise à la Corbeille : rien n’a été retiré de ce groupe.", $"‘{item.File.RelativePath}’ changed between its verification and the Recycle Bin: nothing was removed from this group."));
                            item.Stream.Dispose();
                            Recycle(item.File.FullPath);
                            string? outcome;
                            try { outcome = RecycledOutcome(follower, expected); }
                            catch (IOException incident)
                            {
                                // Something replaced the file in the last microseconds: say so, and where the verified file went.
                                log.Write(L.T("Incident : autre élément peut-être mis à la Corbeille", "Incident: another item may have gone to the Recycle Bin"), item.File.FullPath, group.Hash, item.File.Size, group.Keeper.FullPath, incident.Message);
                                throw;
                            }
                            if (outcome is null)
                                throw new OperationCanceledException(L.T("Mise à la Corbeille annulée dans la fenêtre de Windows.", "Moving to the Recycle Bin was cancelled in the Windows dialog."));
                            if (outcome.Length > 0)
                            {
                                bytesRecycled += item.File.Size;
                                Record(L.T("Mis à la Corbeille", "Moved to the Recycle Bin"), item.File, group.Hash, group.Keeper.FullPath, outcome);
                            }
                            else
                            {
                                bytesRemoved += item.File.Size;
                                warnings.Add(L.F($"Trop volumineux pour la Corbeille, supprimé définitivement après confirmation dans Windows : {item.File.FullPath}", $"Too large for the Recycle Bin, deleted permanently after confirmation in Windows: {item.File.FullPath}"));
                                Record(L.T("Supprimé définitivement (trop volumineux pour la Corbeille)", "Deleted permanently (too large for the Recycle Bin)"), item.File, group.Hash, group.Keeper.FullPath, "");
                            }
                        }
                        else
                        {
                            if (!DeleteVerified(item.Stream.SafeFileHandle))
                                throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Windows a refusé la suppression du fichier vérifié.", "Windows refused to delete the verified file."));
                            item.Stream.Dispose();
                            bytesRemoved += item.File.Size;
                            Record(L.T("Supprimé définitivement", "Deleted permanently"), item.File, group.Hash, group.Keeper.FullPath, "");
                        }
                        Report(mode switch
                        {
                            DuplicateRemovalMode.Quarantine => L.T("Doublon mis à l’écart", "Duplicate quarantined"),
                            DuplicateRemovalMode.RecycleBin => L.T("Doublon mis à la Corbeille", "Duplicate moved to the Recycle Bin"),
                            _ => L.T("Doublon supprimé", "Duplicate deleted")
                        }, item.File.FullPath);
                    }
                }
                // Refused for this group only (contents changed, hard links, a link on the way, Windows refusing):
                // the other groups are still processed. A journal or quarantine failure stops everything.
                catch (Exception e) when (IsFilesystemError(e) && e is not StopAll) { errors.Add(e.Message); }
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
            // Each closes on its own: a failure of the first never leaves the second open or hides the result.
            try { manifest?.Dispose(); }
            catch (Exception e) when (IsFilesystemError(e)) { errors.Add(L.F($"Manifeste de la mise à l’écart incomplet : {e.Message}", $"Quarantine manifest incomplete: {e.Message}")); }
            try { log?.Dispose(); }
            catch (Exception e) when (IsFilesystemError(e)) { errors.Add(L.F($"Journal des actions incomplet : {e.Message}", $"Action journal incomplete: {e.Message}")); }
        }
        return new DuplicateRemovalResult
        {
            DeletedPaths = completed, Errors = errors, Warnings = warnings, Cancelled = cancelled,
            BytesMoved = bytesMoved, BytesRemoved = bytesRemoved, BytesRecycled = bytesRecycled, QuarantinePath = quarantineSession
        };
    }

    /// <summary>Groups to process. A group whose selection or only kept copy is in a quarantine is skipped with a reason.</summary>
    private static List<RemovalGroup> BuildPlan(DuplicateResult scan, string root,
        IReadOnlyCollection<string> selectedPaths, List<string> refusals)
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
            var kept = group.Files.Where(file => !selection.Contains(Path.GetFullPath(file.FullPath))).ToList();
            if (kept.Count == 0)
                throw new InvalidOperationException(L.T("Conservez au moins un fichier de chaque groupe de doublons.", "Keep at least one file in each duplicate group."));
            if (selected.FirstOrDefault(file => IsInQuarantine(file.RelativePath)) is { } aside)
            {
                refusals.Add(L.F($"Groupe ignoré : « {aside.RelativePath} » est dans une mise à l’écart. Utilisez « Restaurer » plutôt que de le retirer.", $"Group skipped: ‘{aside.RelativePath}’ is in a quarantine. Use Restore rather than removing it."));
                continue;
            }
            if (kept.FirstOrDefault(file => !IsInQuarantine(file.RelativePath)) is not { } keeper)
            {
                refusals.Add(L.F($"Groupe ignoré : la seule copie conservée serait dans une mise à l’écart (« {kept[0].RelativePath} »).", $"Group skipped: the only kept copy would be in a quarantine (‘{kept[0].RelativePath}’)."));
                continue;
            }
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
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("Le chemin d’un doublon est invalide ou sort du dossier analysé.", "A duplicate's path is invalid or outside the scanned folder."));
    }

    /// <summary>An attributes-only handle that lets the shell move or delete the file, then tells where it went.</summary>
    private static SafeFileHandle OpenFollower(string path)
    {
        var handle = CreateFileW(FileSafety.Native(path), 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, L.F($"Impossible de suivre « {path} » pendant la mise à la Corbeille.", $"Unable to follow ‘{path}’ while moving it to the Recycle Bin."));
        }
        return handle;
    }

    private static FileStream OpenVerified(DuplicateFile file, string hash, bool forRemoval,
        string canonicalRoot, CancellationToken token, Action<int> onBytes)
    {
        // Writers, renames and deletions by others are denied while the file is verified and until it is acted on.
        var handle = CreateFileW(FileSafety.Native(file.FullPath), GenericRead | (forRemoval ? DeleteAccess : 0),
            FileShare.Read, IntPtr.Zero, 3, OpenReparsePoint | SequentialScan, IntPtr.Zero);
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
            if (!CreateDirectoryW(FileSafety.Native(quarantine), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Impossible de créer une quarantaine réservée à Checker San.", "Unable to create a quarantine reserved for Checker San."));
            directories.Pin(quarantine);
            using var marker = new FileStream(Path.Combine(quarantine, MarkerName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(marker, new QuarantineMarker
                { Schema = 1, Application = "Checker San", Kind = "DuplicateQuarantine" });
            marker.Flush(true);
        }
        var session = Path.Combine(quarantine, Guid.NewGuid().ToString("N"));
        if (!CreateDirectoryW(FileSafety.Native(session), IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Impossible de créer une session de quarantaine.", "Unable to create a quarantine session."));
        directories.Pin(session);
        return session;
    }

    private static void RenameByHandle(SafeFileHandle file, SafeFileHandle destinationDirectory, string name)
    {
        var error = FileSafety.RenameByHandle(file, destinationDirectory, name);
        if (error != 0)
            throw new Win32Exception(error, L.F($"Windows a refusé la mise à l’écart (code {error}) ; aucune suppression définitive n’a été tentée.", $"Windows refused to quarantine the file (code {error}); permanent deletion was not attempted."));
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
                // A session or manifest replaced by a link is not one Checker San made: it is never offered.
                // Only sessions that Restore would accept: offering one it then refuses would mislead.
                if (IsLink(manifestPath) || !Directory.Exists(session) || IsLink(session) || ReadManifest(manifestPath) is not { } manifest ||
                    !Belongs(manifest.Header, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))) continue;
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
        if (!IsManagedQuarantine(quarantine) || IsLink(session) || IsLink(session + ManifestSuffix) || ReadManifest(session + ManifestSuffix) is not { } manifest)
            throw new InvalidOperationException(L.T("Cette mise à l’écart ne peut pas être restaurée : son manifeste est absent ou illisible.", "These files cannot be restored: the manifest is missing or unreadable."));
        if (!Belongs(manifest.Header, root))
            throw new InvalidOperationException(L.F($"Cette mise à l’écart concerne « {manifest.Header.Root} » : rouvrez ce dossier pour la restaurer.", $"These files were moved aside from ‘{manifest.Header.Root}’: open that folder to restore them."));
        var log = ActionLog.Open(logPath);
        try
        {
            using (var directories = new DirectoryLocks(root))
            {
                // Every folder used is pinned without following links and cannot be renamed or replaced meanwhile,
                // so a junction planted in the quarantine can never lead a file in from outside the scanned folder.
                directories.Pin(session);
                var canonicalSession = FileSafety.CanonicalDirectory(session);
                foreach (var entry in manifest.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var stored = Path.Combine(session, entry.RelativePath);
                    var original = Path.Combine(root, entry.RelativePath);
                    if (!File.Exists(stored)) continue;
                    try
                    {
                        directories.PinParent(stored);
                        if (File.Exists(original) || Directory.Exists(original))
                            throw new IOException(L.F($"Un élément occupe déjà « {entry.RelativePath} » : le fichier reste dans la mise à l’écart.", $"Something already occupies ‘{entry.RelativePath}’: the file stays aside."));
                        // The stream owns the handle: it stays open until the rename, so the file checked is the file moved.
                        using var file = new FileStream(OpenStored(stored, Path.Combine(canonicalSession, entry.RelativePath)), FileAccess.Read, 1024 * 1024, false);
                        if (file.Length != entry.Size || !string.Equals(Convert.ToHexString(SHA256.HashData(file)), entry.Hash, StringComparison.OrdinalIgnoreCase))
                            throw new IOException(L.F($"« {entry.RelativePath} » a changé depuis sa mise à l’écart : il n’est pas restauré.", $"‘{entry.RelativePath}’ changed after being moved aside: it is not restored."));
                        directories.CreateAndPinParent(original);
                        var error = FileSafety.RenameByHandle(file.SafeFileHandle, directories.Pin(Path.GetDirectoryName(original)!), Path.GetFileName(original));
                        if (error != 0) throw new Win32Exception(error, L.F($"Windows a refusé de restaurer « {entry.RelativePath} » (code {error}).", $"Windows refused to restore ‘{entry.RelativePath}’ (code {error})."));
                        restored++;
                        log.Write(L.T("Restauré", "Restored"), stored, entry.Hash, entry.Size, "", original);
                    }
                    catch (Exception e) when (IsFilesystemError(e)) { errors.Add(e.Message); }
                }
            }
        }
        finally
        {
            // A journal that cannot be closed never hides what was restored.
            try { log.Dispose(); }
            catch (Exception e) when (IsFilesystemError(e)) { errors.Add(L.F($"Journal des actions incomplet : {e.Message}", $"Action journal incomplete: {e.Message}")); }
        }
        RemoveEmptyFolders(session);
        if (!Directory.Exists(session))
        {
            try { File.Delete(session + ManifestSuffix); }
            catch (Exception e) when (IsFilesystemError(e)) { }
        }
        return new RestoreResult(restored, errors);
    }

    /// <summary>A failure that ends the whole removal: the journal cannot be written, or no quarantine can be made.</summary>
    private sealed class StopAll(string message, Exception inner) : IOException(message, inner);

    /// <summary>
    /// Deletes through the verified handle. FileDispositionInfoEx ignores the read-only attribute, which a copy from a
    /// CD or a camera often carries; older file systems fall back to the plain request.
    /// </summary>
    private static bool DeleteVerified(SafeFileHandle handle)
    {
        uint flags = 0x1 | 0x10; // FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE
        if (SetFileInformationByHandle(handle, 21, ref flags, sizeof(uint))) return true;
        if (Marshal.GetLastWin32Error() is not (87 or 50 or 1)) return false;
        byte remove = 1;
        return SetFileInformationByHandle(handle, 4, ref remove, 1);
    }

    private static bool IsLink(string path)
    {
        try { return FileSafety.Access(path, File.GetAttributes(path)) == EntryAccess.Link; }
        catch (Exception e) when (IsFilesystemError(e)) { return false; }
    }

    /// <summary>
    /// A quarantined file opened without following links, with the right to rename it: it must be a plain file,
    /// with no other name, at exactly its place inside the session.
    /// </summary>
    private static SafeFileHandle OpenStored(string stored, string expected)
    {
        var handle = CreateFileW(FileSafety.Native(stored), GenericRead | DeleteAccess, FileShare.Read, IntPtr.Zero, 3, OpenReparsePoint | SequentialScan, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, L.F($"Impossible d’ouvrir « {stored} ».", $"Unable to open ‘{stored}’."));
        }
        try
        {
            if (!GetFileInformationByHandleEx(handle, 0, out BasicInfo info, (uint)Marshal.SizeOf<BasicInfo>()) ||
                ((FileAttributes)info.Attributes & FileAttributes.Directory) != 0 || FileSafety.Access(handle) != EntryAccess.Normal ||
                DuplicateEngine.HardLinkCount(handle) != 1 ||
                !string.Equals(FinalPath(handle), expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.F($"« {stored} » n’est plus le fichier mis à l’écart (lien, autre nom ou autre emplacement) : il n’est pas restauré.", $"‘{stored}’ is no longer the file moved aside (link, other name or other place): it is not restored."));
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void RemoveEmptyFolders(string directory)
    {
        try
        {
            // The session itself is checked too: a link put in its place is never walked into.
            if (IsLink(directory)) return;
            foreach (var child in Directory.EnumerateDirectories(directory))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) RemoveEmptyFolders(child);
            // A non-recursive delete fails on anything left inside.
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch (Exception e) when (IsFilesystemError(e)) { }
    }

    private static FileStream CreateManifest(string session, string root, string canonicalRoot)
    {
        var stream = new FileStream(session + ManifestSuffix, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        AppendLine(stream, JsonSerializer.SerializeToUtf8Bytes(new ManifestHeader { Root = root, CanonicalRoot = canonicalRoot, CreatedAt = DateTimeOffset.Now }));
        return stream;
    }

    /// <summary>
    /// Whether a manifest belongs to the folder opened now: same spelling, or same real folder reached another way
    /// (mapped drive and share, junction, short name). Manifests without a real path compare their spelling only.
    /// </summary>
    private static bool Belongs(ManifestHeader header, string root)
    {
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(header.Root)), root, StringComparison.OrdinalIgnoreCase)) return true;
        if (header.CanonicalRoot is null) return false;
        try { return string.Equals(FileSafety.CanonicalDirectory(root), header.CanonicalRoot, StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (IsFilesystemError(e)) { return false; }
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
        /// <summary>The real path of the root when the files were moved aside; absent from earlier manifests.</summary>
        public string? CanonicalRoot { get; init; }
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
            var handle = CreateFileW(FileSafety.Native(directory), 0x80, FileShare.ReadWrite, IntPtr.Zero, 3,
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
                throw new IOException(L.F($"Un lien ou une jonction se trouve sur le chemin, et il n’est jamais suivi : {directory}", $"A link or junction is on the way, and it is never followed: {directory}"));
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
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref uint info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool CreateDirectoryW(string path, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool GetVolumePathNameW(string path, StringBuilder volume, uint length);
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
