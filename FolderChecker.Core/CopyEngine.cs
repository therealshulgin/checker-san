using System.Collections.Concurrent;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

public sealed record CopyProgress(string Phase, string CurrentPath, int CompletedFiles, int TotalFiles, long BytesWritten, long BytesResumed, int RetryAttempt = 0);
public sealed record CopyResult(bool Cancelled, int CompletedFiles, int TotalFiles, long BytesWritten, long BytesResumed, IReadOnlyList<string> Errors)
{
    public bool Complete => !Cancelled && Errors.Count == 0 && CompletedFiles == TotalFiles;
}
public sealed class CopyPlan
{
    public int Schema { get; set; } = 2;
    public bool Bidirectional { get; set; }
    public string SourceRoot { get; set; } = "";
    public string TargetRoot { get; set; } = "";
    public string SourceCanonical { get; set; } = "";
    public string TargetCanonical { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>Volume and identifier of each root when the plan was made: a backup disk swapped under the same letter is refused. Null in earlier journals.</summary>
    public string? SourceRootId { get; set; }
    public string? TargetRootId { get; set; }
    public List<CopyItem> Items { get; set; } = [];
    /// <summary>Temporary files of earlier interrupted copies found among the missing paths: left out, never copied over.</summary>
    [JsonIgnore] public int AbandonedTemporaries { get; set; }
    /// <summary>Missing paths whose name Windows would rewrite (final dot or space, reserved or malformed name): left out.</summary>
    [JsonIgnore] public int UnsupportedNames { get; set; }
    /// <summary>Files gone or refused since the comparison, whose attributes could not be read: left out.</summary>
    [JsonIgnore] public int UnreadableFiles { get; set; }
    public int FileCount => Items.Count(i => i.Kind == EntryKind.File);
    public long TotalBytes => Items.Where(i => i.Kind == EntryKind.File).Sum(i => i.Size);
}
public sealed class CopyItem
{
    public bool ReverseDirection { get; set; }
    public string RelativePath { get; set; } = "";
    public EntryKind Kind { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public DateTime CreationUtc { get; set; }
    /// <summary>Read-only, hidden, system, archive and not-indexed bits to reproduce; null in journals from earlier versions.</summary>
    public FileAttributes? Attributes { get; set; }
    /// <summary>
    /// Folders only: true once this copy created it. A folder that already existed keeps its own attributes and
    /// dates. Never reset by a resume; null in earlier journals, which keeps their former behaviour.
    /// </summary>
    public bool? CreatedByCopy { get; set; }
    public string PartialName { get; set; } = ".checkersan-" + Guid.NewGuid().ToString("N") + ".partial";
    public bool Completed { get; set; }
    public string? VerifiedHash { get; set; }
    public string? SourceIdentity { get; set; }
    public string? PartialIdentity { get; set; }
    public string? DestinationIdentity { get; set; }
}

/// <summary>Space a plan still needs on one destination volume, rounded up to whole 4 KiB clusters per file.</summary>
public sealed record SpaceNeed(string Root, long Required, long Available)
{
    public bool Sufficient => Available >= Required;
}

/// <summary>Copies only explicitly planned missing paths. Never overwrites a final destination.</summary>
public sealed class CopyEngine
{
    private const FileOptions NoBuffering = (FileOptions)0x20000000;
    private const FileAttributes CopiedAttributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
        FileAttributes.Archive | FileAttributes.NotContentIndexed;

    static CopyEngine() => FileSafety.Initialize();

    /// <summary>
    /// A copy needs a comparison that ran to its end with both roots readable. An item with an incident (unreadable,
    /// changed during the scan, conflict, link) is left out with everything below it, so it no longer holds back the
    /// other items; nor is an online-only file ever copied. Existing files are never overwritten in any case.
    /// </summary>
    public static bool CanCreatePlan(ComparisonResult comparison) =>
        comparison.Finished && !comparison.Cancelled &&
        !comparison.Rows.Any(row => row.RelativePath == "." && row.Status == ComparisonStatus.Unverified);

    public static CopyPlan CreatePlan(ComparisonResult comparison, bool sourceToTarget)
    {
        ValidateComparison(comparison);
        var plan = OneWayPlan(comparison, sourceToTarget);
        AddItems(plan, comparison, MissingRows(comparison, sourceToTarget), reverseDirection: false);
        FinishPlan(plan);
        return plan;
    }

    /// <summary>
    /// Gathers into one folder the files whose contents it lacks. Unlike <see cref="CreatePlan"/>, which rebuilds the
    /// other tree path for path, a file whose identical contents (SHA-256) lie elsewhere in the destination is not copied
    /// again. Files keep their relative path; a missing folder is created to hold them, or when it holds no file at all.
    /// </summary>
    public static CopyPlan CreateGatherPlan(ComparisonResult comparison, bool intoTarget)
    {
        ValidateComparison(comparison);
        var plan = OneWayPlan(comparison, intoTarget);
        AddItems(plan, comparison, GatherRows(comparison, intoTarget, StaleCopies(comparison, intoTarget)), reverseDirection: false);
        FinishPlan(plan);
        return plan;
    }

    /// <summary>
    /// Files kept out of a gathering by a copy elsewhere whose copy, or own contents, changed since the comparison: a copy
    /// deleted or edited, a file edited. Both are queried by handle against the identity the comparison read, several at
    /// once, since a share answers each query with a round trip. Such a file is gathered after all.
    /// </summary>
    private static HashSet<string> StaleCopies(ComparisonResult comparison, bool intoTarget)
    {
        var relying = comparison.Rows.Where(row => intoTarget
            ? row is { Status: ComparisonStatus.Moved, SourcePath: not null, TargetPath: not null } && !(row.SourceSize < NameOnlyBelow)
            : row is { Status: ComparisonStatus.OnlyTarget, TargetPath: not null, SourceCopyPath: not null } && !(row.TargetSize < NameOnlyBelow)).ToArray();
        var stale = new ConcurrentBag<string>();
        Parallel.ForEach(relying, new ParallelOptions { MaxDegreeOfParallelism = 8 }, row =>
        {
            var (own, copy, size) = intoTarget ? (row.SourcePath!, row.TargetPath!, row.SourceSize) : (row.TargetPath!, row.SourceCopyPath!, row.TargetSize);
            if (!Unchanged(comparison, own, size) || !Unchanged(comparison, copy, size)) stale.Add(row.RelativePath);
        });
        return stale.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool Unchanged(ComparisonResult comparison, string path, long? size)
    {
        try
        {
            if (comparison.ReadIdentities.TryGetValue(path, out var identity)) return FileSafety.CurrentIdentity(path) == identity;
            // A file system without identities leaves the size, at least, to check.
            return FileSafety.Stat(path).Size == size;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception) { return false; }
    }

    /// <summary>Files a gathering would copy, judged from the comparison alone: building the plan still checks each one on disk.</summary>
    public static int GatherFileCount(ComparisonResult comparison, bool intoTarget)
    {
        if (!CanCreatePlan(comparison)) return 0;
        var blocked = BlockedPaths(comparison);
        var source = intoTarget ? comparison.SourceRoot : comparison.TargetRoot;
        return GatherRows(comparison, intoTarget).Count(row => row.Kind == EntryKind.File && Exclusion(row, source, blocked) is null);
    }

    /// <summary>
    /// Items of the copying folder that a gathering leaves without a proven copy in the destination: another version at
    /// the same path, an incident, a link, an online-only file, a name Windows would alter. Temporaries are not counted.
    /// </summary>
    public static int GatherShortfall(ComparisonResult comparison, bool intoTarget)
    {
        var blocked = BlockedPaths(comparison);
        var source = intoTarget ? comparison.SourceRoot : comparison.TargetRoot;
        var left = GatherRows(comparison, intoTarget).Count(row => row.Kind == EntryKind.File && Exclusion(row, source, blocked) is { } reason && reason != LeftOut.Temporary);
        bool Present(ComparisonRow row) => intoTarget ? row.SourcePath is not null : row.TargetPath is not null;
        return left + comparison.Rows.Count(row => row.Status is ComparisonStatus.Different or ComparisonStatus.Conflict ||
            row.Status is ComparisonStatus.Unverified or ComparisonStatus.Skipped && row.RelativePath != "." && Present(row));
    }

    private enum LeftOut { OnlineOnly, Blocked, Temporary, UnsupportedName }

    /// <summary>Why a planned row cannot be copied, judged without reading the disk; null when it can. Shared by the plan and its count.</summary>
    private static LeftOut? Exclusion(ComparisonRow row, string sourceRoot, HashSet<string> blocked)
    {
        // Copying an online-only file would download it, and its changing state would fail the source checks.
        if (row.OnlineOnly) return LeftOut.OnlineOnly;
        // An absent descendant cannot be created below an incident, a file/directory conflict or through a link.
        var relative = row.RelativePath.Replace('/', '\\');
        if (IsBlocked(blocked, relative)) return LeftOut.Blocked;
        // A temporary left by an interrupted copy is a fragment, and its journal is gone: never copy it over.
        if (row.Kind == EntryKind.File && IsCopyTemporary(Path.GetFileName(relative))) return LeftOut.Temporary;
        // A name Windows paths cannot carry intact would be copied under another name, or not resumable from
        // the journal: it is left out with a count, never allowed to abort the whole plan.
        return PortableName(sourceRoot, relative) ? null : LeftOut.UnsupportedName;
    }

    private static CopyPlan OneWayPlan(ComparisonResult comparison, bool sourceToTarget)
    {
        var source = sourceToTarget ? comparison.SourceRoot : comparison.TargetRoot;
        var target = sourceToTarget ? comparison.TargetRoot : comparison.SourceRoot;
        var plan = new CopyPlan { SourceRoot = source, TargetRoot = target, SourceCanonical = FileSafety.CanonicalDirectory(source), TargetCanonical = FileSafety.CanonicalDirectory(target),
            SourceRootId = RootId(source), TargetRootId = RootId(target) };
        ValidateRoots(plan);
        return plan;
    }

    private static IEnumerable<ComparisonRow> MissingRows(ComparisonResult comparison, bool sourceToTarget) =>
        comparison.Rows.Where(r => sourceToTarget ? r.Status is ComparisonStatus.OnlySource or ComparisonStatus.Moved : r.Status == ComparisonStatus.OnlyTarget);

    /// <summary>Under this size a file is worth its name: empty markers (.gitkeep, __init__.py) or flags share their bytes with unrelated files.</summary>
    private const long NameOnlyBelow = 4096;

    /// <summary>
    /// Files only in the copying folder with no copy of their contents in the destination, and the missing folders that
    /// hold them. A missing folder holding only files already gathered elsewhere is not created; one holding no file is,
    /// with the missing folders above it.
    /// </summary>
    private static IEnumerable<ComparisonRow> GatherRows(ComparisonResult comparison, bool intoTarget, HashSet<string>? stale = null)
    {
        // A copy only counts if it can be relied on: not a temporary, a quarantined duplicate or a recycled file, and
        // unchanged when the plan is made.
        bool Unreliable(ComparisonRow row, string? copy, string root) => copy is null || SystemFolders.NeverEvidence(FileSafety.RelativeTo(root, copy)) ||
            stale?.Contains(row.RelativePath) == true;
        // A file found elsewhere is a Moved row on the folder 1 side, and an only-in-2 row naming its copy on the other.
        // A small file is copied anyway: that costs at most a cluster, and keeps its name.
        bool Lacking(ComparisonRow row) => intoTarget
            ? row.Status == ComparisonStatus.OnlySource ||
                row.Status == ComparisonStatus.Moved && (row.SourceSize < NameOnlyBelow || Unreliable(row, row.TargetPath, comparison.TargetRoot))
            : row.Status == ComparisonStatus.OnlyTarget &&
                (row.Kind == EntryKind.Directory || row.TargetSize < NameOnlyBelow || Unreliable(row, row.SourceCopyPath, comparison.SourceRoot));
        bool OnCopyingSide(ComparisonRow row) => intoTarget
            ? row.Status != ComparisonStatus.OnlyTarget
            : row.Status is not (ComparisonStatus.OnlySource or ComparisonStatus.Moved);
        var holdingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var holdingLacking = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in comparison.Rows.Where(row => row.Kind == EntryKind.File && OnCopyingSide(row)))
        {
            var lacking = Lacking(row) && !row.OnlineOnly;
            foreach (var folder in Ancestors(row.RelativePath))
            {
                holdingFiles.Add(folder);
                if (lacking) holdingLacking.Add(folder);
            }
        }
        // A folder holding no file is kept, and so are the missing folders above it, which would otherwise be created
        // on the way without their dates and attributes.
        foreach (var row in comparison.Rows.Where(row => row.Kind == EntryKind.Directory && Lacking(row) && !holdingFiles.Contains(row.RelativePath)))
            foreach (var folder in Ancestors(row.RelativePath)) holdingLacking.Add(folder);
        return comparison.Rows.Where(row => Lacking(row) && (row.Kind == EntryKind.File ||
            holdingLacking.Contains(row.RelativePath) || !holdingFiles.Contains(row.RelativePath)));
    }

    private static IEnumerable<string> Ancestors(string relative)
    {
        relative = relative.Replace('/', '\\');
        for (var end = relative.IndexOf('\\'); end > 0; end = relative.IndexOf('\\', end + 1))
            yield return relative[..end];
    }

    private static HashSet<string> BlockedPaths(ComparisonResult comparison) => comparison.Rows
        .Where(r => r.Status is ComparisonStatus.Conflict or ComparisonStatus.Unverified or ComparisonStatus.Skipped)
        .Select(r => r.RelativePath.Replace('/', '\\').TrimEnd('\\'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Below an incident, a conflict or a link. Looked up per ancestor: thousands of online-only files must not multiply the work.</summary>
    private static bool IsBlocked(HashSet<string> blocked, string relative) => blocked.Count > 0 && Ancestors(relative).Any(blocked.Contains);

    public static CopyPlan CreateBidirectionalPlan(ComparisonResult comparison)
    {
        ValidateComparison(comparison);
        var plan = new CopyPlan
        {
            Bidirectional = true,
            SourceRoot = comparison.SourceRoot,
            TargetRoot = comparison.TargetRoot,
            SourceCanonical = FileSafety.CanonicalDirectory(comparison.SourceRoot),
            TargetCanonical = FileSafety.CanonicalDirectory(comparison.TargetRoot),
            SourceRootId = RootId(comparison.SourceRoot),
            TargetRootId = RootId(comparison.TargetRoot)
        };
        ValidateRoots(plan);
        AddItems(plan, comparison, MissingRows(comparison, sourceToTarget: true), reverseDirection: false);
        AddItems(plan, comparison, MissingRows(comparison, sourceToTarget: false), reverseDirection: true);
        FinishPlan(plan);
        return plan;
    }

    private static void ValidateComparison(ComparisonResult comparison)
    {
        if (!CanCreatePlan(comparison))
            throw new InvalidOperationException(L.T("Terminez une comparaison, sans l’arrêter et avec les deux dossiers lisibles, avant de préparer une copie.", "Finish a comparison, without stopping it and with both folders readable, before preparing a copy."));
    }

    private static void AddItems(CopyPlan plan, ComparisonResult comparison, IEnumerable<ComparisonRow> rows, bool reverseDirection)
    {
        var source = reverseDirection ? plan.TargetRoot : plan.SourceRoot;
        var blocked = BlockedPaths(comparison);
        foreach (var row in rows)
        {
            switch (Exclusion(row, source, blocked))
            {
                case LeftOut.Temporary: plan.AbandonedTemporaries++; continue;
                case LeftOut.UnsupportedName: plan.UnsupportedNames++; continue;
                case not null: continue;
            }
            var path = SafePath(source, row.RelativePath);
            AssertNoLinks(source, path);
            var item = new CopyItem { RelativePath = row.RelativePath, Kind = row.Kind, ReverseDirection = reverseDirection,
                CreatedByCopy = row.Kind == EntryKind.Directory ? false : null };
            if (row.Kind == EntryKind.File)
            {
                // Opened for its attributes only, which other programs' sharing modes do not deny: a file held open by
                // one (a mailbox, a virtual disk) is still planned, and copying it later fails for that item alone. A file
                // gone or refused since the comparison is counted, never allowed to abort the whole plan.
                try
                {
                    using var file = FileSafety.OpenAttributes(path);
                    item.Size = RandomAccess.GetLength(file);
                    item.LastWriteUtc = File.GetLastWriteTimeUtc(file);
                    item.CreationUtc = File.GetCreationTimeUtc(file);
                    item.Attributes = File.GetAttributes(file) & CopiedAttributes;
                    item.SourceIdentity = IdentityText(FileSafety.Identity(file));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    plan.UnreadableFiles++;
                    continue;
                }
            }
            else
            {
                var folder = new DirectoryInfo(path);
                item.LastWriteUtc = folder.LastWriteTimeUtc;
                item.CreationUtc = folder.CreationTimeUtc;
                item.Attributes = folder.Attributes & CopiedAttributes;
            }
            plan.Items.Add(item);
        }
    }

    /// <summary>
    /// Space still needed per destination volume: unfinished files minus their saved partial data.
    /// Both directions of a two-way copy are added together when they share a volume.
    /// </summary>
    public static IReadOnlyList<SpaceNeed> SpaceNeeds(CopyPlan plan)
    {
        // Resolving a volume walks the whole path, so it is done once per destination root.
        var volumes = plan.Items.Select(i => i.ReverseDirection ? plan.SourceRoot : plan.TargetRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(root => root, VolumeOf, StringComparer.OrdinalIgnoreCase);
        var needs = new Dictionary<string, (string Root, long Required)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, volume) in volumes) needs.TryAdd(volume, (root, 0));
        // Each file takes whole clusters of its volume, 128 KiB by default on large exFAT drives; each new folder at
        // least one. Counting 4 KiB clusters let copies start that then filled such drives.
        var clusters = needs.Keys.ToDictionary(volume => volume, ClusterSize, StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items.Where(i => !i.Completed))
        {
            var root = item.ReverseDirection ? plan.SourceRoot : plan.TargetRoot;
            var volume = volumes[root];
            var remaining = item.Size;
            if (item.Kind == EntryKind.File && item.PartialIdentity is not null)
            {
                var partial = new FileInfo(Path.Combine(Path.GetDirectoryName(SafePath(root, item.RelativePath))!, item.PartialName));
                if (partial.Exists) remaining = Math.Max(0, remaining - partial.Length);
            }
            var cluster = clusters[volume];
            var required = item.Kind == EntryKind.Directory ? cluster : (remaining + cluster - 1) / cluster * cluster;
            needs[volume] = (needs[volume].Root, needs[volume].Required + required);
        }
        return needs.Values.Select(need => new SpaceNeed(need.Root, need.Required, FreeSpace(need.Root))).ToArray();
    }

    private static string VolumeOf(string root)
    {
        var volume = new StringBuilder(32768);
        if (!GetVolumePathNameW(root, volume, (uint)volume.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return volume.ToString();
    }

    private static long ClusterSize(string volume) =>
        GetDiskFreeSpaceW(volume, out var sectorsPerCluster, out var bytesPerSector, out _, out _) && sectorsPerCluster > 0 && bytesPerSector > 0
            ? (long)sectorsPerCluster * bytesPerSector : 4096;

    private static long FreeSpace(string root)
    {
        // Free space available to this user, after quotas; works for drive letters, mount points and shares.
        if (!GetDiskFreeSpaceExW(root, out var available, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return available > long.MaxValue ? long.MaxValue : (long)available;
    }

    /// <summary>
    /// Uncached writes need offsets and lengths aligned to the volume's sector size. Padding a small file to more
    /// than one sector would multiply its writes, so the real size is used, defaulting to 4 KiB (shares, older systems).
    /// </summary>
    private static int SectorSize(SafeFileHandle handle) =>
        GetFileInformationByHandleEx(handle, 16, out StorageInfo info, (uint)Marshal.SizeOf<StorageInfo>()) &&
        info.LogicalBytesPerSector is >= 512 and <= 64 * 1024 && BitOperations.IsPow2(info.LogicalBytesPerSector)
            ? (int)info.LogicalBytesPerSector : 4096;
    private static long RoundUp(long length, int alignment) => (length + alignment - 1) / alignment * alignment;

    private static SafeFileHandle OpenPartial(string path, bool existing)
    {
        // Read, write and delete access, shared for reading only: nobody else can change or rename the copy.
        var handle = CreateFileW(FileSafety.Native(path), 0x80000000 | 0x40000000 | 0x10000, FileShare.Read, IntPtr.Zero, existing ? 3u : 1u, 0x20000000 | 0x08000000, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        // Some third-party file systems refuse uncached I/O (error 87): the copy is then written through the cache,
        // and verified as the reads already are on such devices.
        if (error == 87)
        {
            handle = CreateFileW(FileSafety.Native(path), 0x80000000 | 0x40000000 | 0x10000, FileShare.Read, IntPtr.Zero, existing ? 3u : 1u, 0x08000000, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            error = Marshal.GetLastWin32Error();
            handle.Dispose();
        }
        throw Marshal.GetExceptionForHR(unchecked((int)0x80070000 | error)) ?? new Win32Exception(error);
    }

    /// <summary>A sector-aligned slice of a pinned array, as reads bypassing the Windows cache require.</summary>
    private static Memory<byte> AlignedBuffer(int size)
    {
        const int alignment = 64 * 1024;
        var array = GC.AllocateUninitializedArray<byte>(size + alignment, pinned: true);
        var offset = (int)((alignment - (long)Marshal.UnsafeAddrOfPinnedArrayElement(array, 0) % alignment) % alignment);
        return array.AsMemory(offset, size);
    }

    /// <summary>
    /// Hashes through a handle that bypasses the Windows cache, so verification reads what reached the disk
    /// or the share rather than the copy still held in memory. A device refusing such reads is read normally.
    /// </summary>
    private static string HashFromDisk(SafeFileHandle handle, string path, Memory<byte> buffer, Action progress, CancellationToken ct)
    {
        try { return HashHandle(handle, buffer.Span, progress, ct); }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) == 87)
        {
            // Shares everything the caller's handle may hold, including its own write access.
            using var cached = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return HashHandle(cached, buffer.Span, progress, ct);
        }
    }

    private static string HashHandle(SafeFileHandle handle, Span<byte> buffer, Action progress, CancellationToken ct)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = RandomAccess.GetLength(handle);
        // Stops at the length: an uncached read at an unaligned end offset would be rejected.
        for (long offset = 0; offset < length;)
        {
            ct.ThrowIfCancellationRequested();
            var read = RandomAccess.Read(handle, buffer, offset);
            if (read == 0) break;
            hasher.AppendData(buffer[..read]); offset += read;
            progress();
        }
        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static void FinishPlan(CopyPlan plan)
    {
        plan.Items = plan.Items.OrderBy(i => i.ReverseDirection)
            .ThenBy(i => i.Kind == EntryKind.Directory ? 0 : 1)
            .ThenBy(i => i.RelativePath.Length).ToList();
        ValidatePlanStructure(plan);
    }
    /// <summary>Reads the plan line, then applies the latest recorded state of each item. Earlier single-line journals remain valid.</summary>
    public static CopyPlan? ReadPlan(string path)
    {
        try
        {
            // A running copy keeps its journal open for appends; reading must not block it.
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (file.Length > 128L * 1024 * 1024) return null;
            using var reader = new StreamReader(file);
            var header = reader.ReadLine();
            var plan = header is null ? null : JsonSerializer.Deserialize<CopyPlan>(header);
            if (plan is null) return null;
            ValidatePlanStructure(plan);
            var items = plan.Items.ToDictionary(i => i.PartialName, StringComparer.OrdinalIgnoreCase);
            for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
            {
                // Only a crash can leave a torn or unreadable line. Skipping it keeps the item's previous
                // state, which resume revalidates or refuses; it can never mark unverified work complete.
                ItemState? state;
                try { state = JsonSerializer.Deserialize<ItemState>(line); }
                catch (JsonException) { continue; }
                if (state?.PartialName is null || !items.TryGetValue(state.PartialName, out var item) ||
                    (state.VerifiedHash is not null && (state.VerifiedHash.Length != 64 || !state.VerifiedHash.All(Uri.IsHexDigit)))) continue;
                item.Completed = state.Completed; item.VerifiedHash = state.VerifiedHash;
                item.PartialIdentity = state.PartialIdentity; item.DestinationIdentity = state.DestinationIdentity;
                if (state.CreatedByCopy is not null) item.CreatedByCopy = state.CreatedByCopy;
            }
            ValidatePlanStructure(plan);
            return plan;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return null; }
    }
    public Task<CopyResult> CopyAsync(CopyPlan plan, string checkpointPath, IProgress<CopyProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => RunAsync(plan, checkpointPath, progress, cancellationToken));

    private static async Task<CopyResult> RunAsync(CopyPlan plan, string checkpointPath, IProgress<CopyProgress>? progress, CancellationToken ct)
    {
        long written = 0, resumed = 0;
        int completed = 0, totalFiles = 0;
        bool cancelled = false;
        var errors = new List<string>();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string? reportedPhase = null, reportedPath = null;
        void Report(string phase, string path, int retry = 0)
        {
            // Phase and item changes are always reported; counters within one phase at most every 100 ms.
            if (retry == 0 && phase == reportedPhase && path == reportedPath && timer.ElapsedMilliseconds < 100) return;
            reportedPhase = phase; reportedPath = path;
            timer.Restart(); progress?.Report(new(phase, path, completed, totalFiles, written, resumed, retry));
        }
        Task<T> Retry<T>(Func<Task<T>> action, string path) => FileSystemRetryPolicy.ExecuteAsync(_ => action(), 3, 1000,
            (attempt, delay) => Report(L.F($"Reconnexion dans {delay.TotalSeconds:0} s", $"Reconnecting in {delay.TotalSeconds:0} s"), path, attempt), ct);
        Memory<byte> chunk = AlignedBuffer(1024 * 1024), spare = AlignedBuffer(1024 * 1024), checkBuffer = AlignedBuffer(1024 * 1024);
        var interrupted = false;
        try
        {
            ValidatePlanStructure(plan);
            totalFiles = plan.FileCount;
            await Retry(() => { ValidateRoots(plan); return Task.FromResult(true); }, plan.TargetRoot);
            var shortages = (await Retry(() => Task.FromResult(SpaceNeeds(plan)), plan.TargetRoot)).Where(need => !need.Sufficient).ToArray();
            if (shortages.Length > 0)
                throw new IOException(string.Join(" ", shortages.Select(need => L.F(
                    $"Espace insuffisant sur {need.Root} : {L.Size(need.Required)} nécessaires, {L.Size(need.Available)} disponibles. Aucune copie n’a commencé.",
                    $"Not enough space on {need.Root}: {L.Size(need.Required)} needed, {L.Size(need.Available)} available. No copy has started."))));
            using var journal = Journal.Open(plan, checkpointPath);
            foreach (var item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();
                var sourceRoot = item.ReverseDirection ? plan.TargetRoot : plan.SourceRoot;
                var targetRoot = item.ReverseDirection ? plan.SourceRoot : plan.TargetRoot;
                var displayPath = plan.Bidirectional
                    ? $"{(item.ReverseDirection ? "2 → 1" : "1 → 2")} · {item.RelativePath}"
                    : item.RelativePath;
                try
                {
                    if (!ValidPartialName(item.PartialName)) throw new ArgumentException(L.T("Nom de fichier temporaire invalide.", "Invalid temporary file name."));
                    await Retry(() => Task.FromResult(Transfer()), displayPath);
                    // Synchronous I/O on this worker thread: asynchronous FileStream reads halved throughput on an NVMe drive.
                    bool Transfer()
                    {
                        ValidateRoots(plan);
                        var source = SafePath(sourceRoot, item.RelativePath);
                        var target = SafePath(targetRoot, item.RelativePath);
                        // Where the destination must really be: every handle opened below is checked against it, so a
                        // folder swapped for a junction after the path checks can never receive or hide the copy.
                        var real = Path.Combine(item.ReverseDirection ? plan.SourceCanonical : plan.TargetCanonical, item.RelativePath);
                        AssertNoLinks(sourceRoot, source);
                        if (item.Kind == EntryKind.Directory)
                        {
                            if ((File.GetAttributes(source) & FileAttributes.Directory) == 0) throw new IOException(L.T("Le dossier source a changé de type.", "The source folder has changed type."));
                            // Sticky: a resume after this run must not mistake the folder it created for the user's own.
                            if (!Exists(target)) item.CreatedByCopy = true;
                            EnsureDirectory(targetRoot, target);
                            item.Completed = true; journal.Record(item);
                            return true;
                        }
                        EnsureDirectory(targetRoot, Path.GetDirectoryName(target)!);
                        // Opening is harmless, reading would download: a file that became online-only since the plan is refused.
                        if (FileSafety.Access(source, File.GetAttributes(source)) == EntryAccess.OnlineOnly)
                            throw new IOException(L.T("La source n’est plus disponible que dans le cloud : elle n’est pas téléchargée. Rendez-la disponible sur cet appareil, puis comparez à nouveau.", "The source is now online-only: it is not downloaded. Make it available on this device, then compare again."));
                        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                        ValidateSource(item, input);
                        if (Exists(target))
                        {
                            AssertNoLinks(targetRoot, target);
                            if (item.VerifiedHash is null) throw new IOException(L.T("La destination existe déjà : elle n’a pas été remplacée.", "The destination already exists and has not been replaced."));
                            using var existing = File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.Read, NoBuffering);
                            if (!FileSafety.SamePlace(FileSafety.FinalPath(existing), real)) throw new IOException(Moved);
                            var identity = IdentityText(FileSafety.Identity(existing));
                            var length = RandomAccess.GetLength(existing);
                            if (!(item.Completed && identity is not null && identity == item.DestinationIdentity && length == item.Size))
                            {
                                var phase = L.T("Vérification SHA-256 de la copie", "Verifying the copy with SHA-256");
                                var hash = HashFromDisk(existing, target, checkBuffer, () => Report(phase, displayPath), ct);
                                if (length != item.Size || hash != item.VerifiedHash) throw new IOException(L.T("La destination existante a un contenu différent : aucun écrasement.", "The existing destination has different contents and will not be overwritten."));
                                // Renamed but never marked complete (crash, or a network retry after the rename): its
                                // attributes may never have been applied. Dates were set before the rename.
                                if (!item.Completed && item.Attributes is { } missing)
                                {
                                    using (var metadata = FileSafety.OpenForMetadata(target))
                                        File.SetAttributes(metadata, missing == 0 ? FileAttributes.Normal : missing);
                                    identity = IdentityText(FileSafety.Identity(existing));
                                }
                            }
                            item.DestinationIdentity = identity; item.Completed = true;
                            journal.Record(item);
                            return true;
                        }
                        item.Completed = false;
                        var partial = Path.Combine(Path.GetDirectoryName(target)!, item.PartialName);
                        AssertNoLinks(targetRoot, partial);
                        var existed = Exists(partial);
                        // One uncached handle writes the copy, reads it back from the disk and renames it, while no other
                        // program may write, rename or delete it. Reopening a freshly written file waited for antivirus
                        // scanning: about 9 ms of the 14 ms spent per 64 KiB file on an NVMe drive.
                        using var output = OpenPartial(partial, existed);
                        // While this handle is open no folder above it can be renamed: checked once, its place holds
                        // until the rename. A temporary that landed elsewhere is removed through its handle.
                        if (!FileSafety.SamePlace(FileSafety.FinalPath(output), Path.Combine(Path.GetDirectoryName(real)!, item.PartialName)))
                        {
                            if (!existed) FileSafety.DeleteByHandle(output);
                            throw new IOException(Moved);
                        }
                        var partialId = IdentityText(FileSafety.Identity(output), includeChangeTime: false);
                        if (existed && (item.PartialIdentity is null || partialId != item.PartialIdentity))
                            throw new IOException(L.T("Le fichier temporaire ne peut plus être identifié. Conservez-le et démarrez une nouvelle copie depuis un nouveau scan.", "The temporary file can no longer be identified. Keep it and start a new copy after scanning again."));
                        item.PartialIdentity = partialId;
                        journal.Record(item);
                        using var sourceHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var alignment = SectorSize(output);
                        long offset = RandomAccess.GetLength(output);
                        if (offset > RoundUp(input.Length, alignment)) throw new IOException(L.T("Le fichier temporaire a une taille incohérente.", "The temporary file has an inconsistent size."));
                        // Uncached writes start at an aligned offset: a shorter or padded tail is checked again from there.
                        offset = Math.Min(offset, input.Length);
                        offset -= offset % alignment;
                        long checkedBytes = 0;
                        while (checkedBytes < offset)
                        {
                            ct.ThrowIfCancellationRequested();
                            var wanted = (int)Math.Min(chunk.Length, offset - checkedBytes);
                            input.ReadExactly(chunk.Span[..wanted]);
                            if (RandomAccess.Read(output, checkBuffer.Span[..wanted], checkedBytes) != wanted ||
                                !chunk.Span[..wanted].SequenceEqual(checkBuffer.Span[..wanted]))
                                throw new IOException(L.T("La partie déjà copiée diffère de la source. Aucun contenu existant n’a été écrasé ; relancez une nouvelle copie après comparaison.", "The previously copied portion differs from the source. Existing contents have not been overwritten; compare the folders again before starting a new copy."));
                            sourceHasher.AppendData(chunk.Span[..wanted]); checkedBytes += wanted;
                            Report(L.T("Vérification de la partie déjà copiée", "Checking the previously copied portion"), displayPath);
                        }
                        resumed += offset;
                        var position = offset;
                        // Uncached writes block, so each one runs while the next block is read into the other buffer.
                        var pending = Task.CompletedTask;
                        try
                        {
                            for (var block = chunk; ; block = block.Equals(chunk) ? spare : chunk)
                            {
                                var read = input.ReadAtLeast(block.Span, block.Length, throwOnEndOfStream: false);
                                if (read == 0) break;
                                ct.ThrowIfCancellationRequested();
                                sourceHasher.AppendData(block.Span[..read]);
                                // The last block is padded to the alignment; the length is cut back to the source size below.
                                var aligned = block[..(int)RoundUp(read, alignment)];
                                aligned.Span[read..].Clear();
                                pending.GetAwaiter().GetResult();
                                var at = position;
                                pending = Task.Run(() => RandomAccess.Write(output, aligned.Span, at));
                                position += read; written += read;
                                Report(L.T("Copie", "Copying"), displayPath);
                            }
                            pending.GetAwaiter().GetResult();
                        }
                        finally
                        {
                            // Never leave a write running when the handle closes; the first error is the one reported.
                            if (!pending.IsCompleted)
                                try { pending.Wait(); }
                                catch (AggregateException) { }
                        }
                        RandomAccess.SetLength(output, position);
                        // Before the dates: writing a stream moves the file's write time.
                        CopyMarkOfTheWeb(source, partial);
                        // Explicitly set dates are kept when the handle closes; they are flushed with the data.
                        File.SetCreationTimeUtc(output, item.CreationUtc);
                        File.SetLastWriteTimeUtc(output, item.LastWriteUtc);
                        RandomAccess.FlushToDisk(output);
                        ValidateSource(item, input);
                        var expectedHash = Convert.ToHexString(sourceHasher.GetHashAndReset());
                        var verifying = L.T("Vérification SHA-256 de la copie", "Verifying the copy with SHA-256");
                        Report(verifying, displayPath);
                        if (RandomAccess.GetLength(output) != item.Size || HashFromDisk(output, partial, checkBuffer, () => Report(verifying, displayPath), ct) != expectedHash)
                            throw new IOException(L.T("La vérification SHA-256 de la copie a échoué.", "SHA-256 verification of the copy failed."));
                        item.VerifiedHash = expectedHash;
                        journal.Record(item);
                        ct.ThrowIfCancellationRequested();
                        var folder = Path.GetDirectoryName(target)!;
                        AssertNoLinks(targetRoot, folder);
                        using (var directory = FileSafety.OpenDirectory(folder))
                        {
                            if (!FileSafety.SamePlace(FileSafety.FinalPath(directory), Path.GetDirectoryName(real)!)) throw new IOException(Moved);
                            // The verified handle itself takes the final name, which is never replaced.
                            var renamed = FileSafety.RenameByHandle(output, directory, Path.GetFileName(target));
                            if (renamed is 80 or 183) throw new IOException(L.T("La destination existe déjà : elle n’a pas été remplacée.", "The destination already exists and has not been replaced."));
                            if (renamed != 0) throw new Win32Exception(renamed);
                        }
                        // Applied after the rename: a read-only temporary file could not be resumed.
                        if (item.Attributes is { } attributes) File.SetAttributes(output, attributes == 0 ? FileAttributes.Normal : attributes);
                        item.DestinationIdentity = IdentityText(FileSafety.Identity(output));
                        item.Completed = true; journal.Record(item);
                        return true;
                    }
                    if (item.Kind == EntryKind.File) completed++;
                    Report(L.T("Élément terminé", "Item completed"), displayPath);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (FileSystemRetryPolicy.IsTransient(ex)) { errors.Add(L.F($"{displayPath} : connexion indisponible après trois tentatives. {ex.Message}", $"{displayPath}: connection unavailable after three retries. {ex.Message}")); interrupted = true; break; }
                catch (JournalUnavailable ex) { errors.Add(L.F($"{displayPath} : {ex.Message}", $"{displayPath}: {ex.Message}")); interrupted = true; break; }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 39 or 112)
                { errors.Add(L.F($"{displayPath} : espace disque insuffisant. Copie arrêtée ; libérez de la place puis reprenez.", $"{displayPath}: not enough disk space. Copy stopped; free some space, then resume.")); interrupted = true; break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
                { errors.Add(L.F($"{displayPath} : {ex.Message}", $"{displayPath}: {ex.Message}")); }
            }
            // Adding files changes their folders' dates, so folders receive the source's dates last: only folders
            // this copy created (null in earlier journals), each pinned by a handle checked to be in its real place.
            if (!interrupted)
            {
                var folders = plan.Items.Where(i => i.Kind == EntryKind.Directory && i.Completed && i.LastWriteUtc != default && i.CreatedByCopy != false).ToArray();
                if (folders.Length > 0) await Retry(() => { ValidateRoots(plan); return Task.FromResult(true); }, plan.TargetRoot);
                foreach (var item in folders)
                {
                    ct.ThrowIfCancellationRequested();
                    var targetRoot = item.ReverseDirection ? plan.SourceRoot : plan.TargetRoot;
                    try
                    {
                        var target = SafePath(targetRoot, item.RelativePath);
                        AssertNoLinks(targetRoot, target);
                        // Shared without deletion: neither this folder nor any above it can be renamed while it is open.
                        using var folder = FileSafety.OpenForMetadata(target);
                        if (!FileSafety.SamePlace(FileSafety.FinalPath(folder), Path.Combine(item.ReverseDirection ? plan.SourceCanonical : plan.TargetCanonical, item.RelativePath)))
                            throw new IOException(Moved);
                        File.SetCreationTimeUtc(folder, item.CreationUtc);
                        File.SetLastWriteTimeUtc(folder, item.LastWriteUtc);
                        if (item.Attributes is { } attributes) File.SetAttributes(target, attributes == 0 ? FileAttributes.Normal : attributes);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
                    { errors.Add(L.F($"{item.RelativePath} : dates du dossier non appliquées. {ex.Message}", $"{item.RelativePath}: folder dates not applied. {ex.Message}")); }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { cancelled = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { errors.Add(ex.Message); }
        return new(cancelled, completed, totalFiles, written, resumed, errors);
    }

    /// <summary>
    /// The mark of the Web (Zone.Identifier) is the one alternate stream copied: without it, a downloaded program or
    /// document copied here would lose SmartScreen and Protected View. Other streams are not copied, and a file system
    /// without streams (FAT, many shares) simply goes without it.
    /// </summary>
    private static void CopyMarkOfTheWeb(string source, string partial)
    {
        try
        {
            var mark = File.ReadAllBytes(source + ":Zone.Identifier");
            if (mark.Length is 0 or > 64 * 1024) return;
            // Shared like the open copy, which holds delete access for its rename: a stricter open is refused.
            using var stream = new FileStream(partial + ":Zone.Identifier", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            stream.Write(mark);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { }
    }

    private static string Moved => L.T("La destination a changé d’emplacement pendant la copie (lien ou jonction) : rien n’y est copié.",
        "The destination moved during the copy (link or junction): nothing is copied there.");

    private static bool Exists(string path)
    {
        try { File.GetAttributes(path); return true; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }
    private static void ValidateSource(CopyItem item, FileStream stream)
    {
        if (stream.Length != item.Size || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != item.LastWriteUtc || File.GetCreationTimeUtc(stream.SafeFileHandle) != item.CreationUtc ||
            (item.SourceIdentity is not null && IdentityText(FileSafety.Identity(stream.SafeFileHandle)) != item.SourceIdentity))
            throw new IOException(L.T("La source a changé depuis la préparation. Relancez une comparaison avant de copier cette nouvelle version.", "The source has changed since preparation. Compare the folders again before copying this new version."));
    }
    private static string? IdentityText(FileIdentity? identity, bool includeChangeTime = true) => identity is null ? null : $"{identity.Volume:X16}:{identity.IdLow:X16}:{identity.IdHigh:X16}" + (includeChangeTime ? $":{identity.ChangeTime:X16}" : "");
    private static bool ValidPartialName(string? name) => name is not null && name.StartsWith(".checkersan-", StringComparison.Ordinal) && name.EndsWith(".partial", StringComparison.Ordinal) && name.Length == 52 && name[12..44].All(Uri.IsHexDigit);
    /// <summary>The name of a temporary file this engine writes before the verified copy takes its final name.</summary>
    internal static bool IsCopyTemporary(string name) => ValidPartialName(name);
    private static bool PortableName(string root, string relative)
    {
        try { _ = StrictUtf8.GetByteCount(relative); }
        catch (EncoderFallbackException) { return false; }
        var joined = Path.Join(root, relative);
        try { return string.Equals(Path.GetFullPath(joined), joined, StringComparison.Ordinal); }
        catch (ArgumentException) { return false; }
    }
    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);
    private static string? RootId(string path) => FileSafety.ObjectId(path) is { } id ? $"{id.Volume:X16}:{id.Low:X16}:{id.High:X16}" : null;
    private static void ValidatePlanStructure(CopyPlan plan)
    {
        if (plan is null || plan.Schema is not (1 or 2) || plan.Items is null ||
            string.IsNullOrWhiteSpace(plan.SourceRoot) || string.IsNullOrWhiteSpace(plan.TargetRoot) ||
            string.IsNullOrWhiteSpace(plan.SourceCanonical) || string.IsNullOrWhiteSpace(plan.TargetCanonical) ||
            !Path.IsPathFullyQualified(plan.SourceRoot) || !Path.IsPathFullyQualified(plan.TargetRoot) ||
            !Path.IsPathFullyQualified(plan.SourceCanonical) || !Path.IsPathFullyQualified(plan.TargetCanonical))
            throw new ArgumentException(L.T("Journal de copie invalide ou version non prise en charge.", "Invalid copy journal or unsupported journal version."));
        if (plan.Schema == 1 && plan.Bidirectional)
            throw new ArgumentException(L.T("Un ancien journal ne peut pas contenir une copie bidirectionnelle.", "A legacy journal cannot contain a two-way copy."));
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Temporary names also identify items in the journal's state lines.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var item in plan.Items)
        {
            if (item is null || item.Size < 0 || !Enum.IsDefined(item.Kind) ||
                (item.Kind == EntryKind.Directory && item.Size != 0) || !ValidPartialName(item.PartialName) || !names.Add(item.PartialName))
                throw new ArgumentException(L.T("Un élément du journal de copie est invalide.", "An item in the copy journal is invalid."));
            if (item.ReverseDirection && (!plan.Bidirectional || plan.Schema == 1))
                throw new ArgumentException(L.T("Sens de copie ambigu dans le journal.", "The copy direction in the journal is ambiguous."));
            if (item.VerifiedHash is not null && (item.VerifiedHash.Length != 64 || !item.VerifiedHash.All(Uri.IsHexDigit)))
                throw new ArgumentException(L.T("Empreinte SHA-256 invalide dans le journal.", "Invalid SHA-256 digest in the journal."));
            SafePath(plan.SourceRoot, item.RelativePath);
            SafePath(plan.TargetRoot, item.RelativePath);
            var target = SafePath(item.ReverseDirection ? plan.SourceRoot : plan.TargetRoot, item.RelativePath);
            if (!destinations.Add(target))
                throw new ArgumentException(L.T("Le journal contient plusieurs opérations pour une même destination.", "The journal contains multiple operations for the same destination."));
            if (item.Kind == EntryKind.File)
            {
                var partial = Path.Combine(Path.GetDirectoryName(target)!, item.PartialName);
                if (!partials.Add(partial))
                    throw new ArgumentException(L.T("Le journal partage un fichier temporaire entre plusieurs copies.", "The journal uses the same temporary file for multiple copies."));
                if (item.Size > long.MaxValue - totalBytes)
                    throw new ArgumentException(L.T("La taille totale du journal est invalide.", "The journal has an invalid total size."));
                totalBytes += item.Size;
            }
        }
        if (partials.Overlaps(destinations))
            throw new ArgumentException(L.T("Un fichier temporaire entre en conflit avec une destination prévue.", "A temporary file conflicts with a planned destination."));
    }
    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split(['/', '\\']).Any(s => s is "" or "." or ".."))
            throw new ArgumentException(L.T("Chemin relatif de copie invalide.", "Invalid relative copy path."));
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!FileSafety.IsWithin(root, path)) throw new ArgumentException(L.T("Le chemin de copie sort du dossier choisi.", "The copy path is outside the selected folder."));
        return path;
    }
    private static bool IsFolder(string path)
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.Directory) != 0 && FileSafety.Access(path, attributes) != EntryAccess.Link;
    }
    private static void ValidateRoots(CopyPlan plan)
    {
        if (!IsFolder(plan.SourceRoot) || !IsFolder(plan.TargetRoot))
            throw new IOException(L.T("Les racines doivent être des dossiers réels accessibles.", "The roots must be accessible physical folders."));
        var source = FileSafety.CanonicalDirectory(plan.SourceRoot); var target = FileSafety.CanonicalDirectory(plan.TargetRoot);
        if (!source.Equals(plan.SourceCanonical, StringComparison.OrdinalIgnoreCase) || !target.Equals(plan.TargetCanonical, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.T("L’emplacement réel d’un dossier a changé.", "The physical location of a folder has changed."));
        // Same path, other folder: another backup disk under the same letter, or a folder deleted and recreated.
        if ((plan.SourceRootId is not null && RootId(plan.SourceRoot) != plan.SourceRootId) || (plan.TargetRootId is not null && RootId(plan.TargetRoot) != plan.TargetRootId))
            throw new IOException(L.T("Un dossier de la copie a été remplacé par un autre au même emplacement (autre disque, dossier recréé). Relancez une comparaison.", "A folder of the copy was replaced by another one at the same place (other disk, recreated folder). Compare again."));
        if (FileSafety.IsWithin(source, target) || FileSafety.IsWithin(target, source)) throw new ArgumentException(L.T("Les dossiers de copie doivent être distincts et non imbriqués.", "The copy folders must be separate and must not be nested."));
    }
    private static void AssertNoLinks(string root, string path)
    {
        if (!FileSafety.IsWithin(root, path)) throw new IOException(L.T("Chemin hors du dossier choisi.", "The path is outside the selected folder."));
        var current = root;
        foreach (var part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            if (!Exists(current)) continue;
            if (FileSafety.Access(current, File.GetAttributes(current)) == EntryAccess.Link) throw new IOException(L.T("Un lien ou une jonction bloque la copie : ", "A link or junction is blocking the copy: ") + current);
        }
    }
    private static void EnsureDirectory(string root, string directory)
    {
        AssertNoLinks(root, directory);
        var current = root;
        foreach (var part in Path.GetRelativePath(root, directory).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            if (!Exists(current)) Directory.CreateDirectory(current);
            if (!IsFolder(current)) throw new IOException(L.T("La destination contient un fichier ou un lien à la place d’un dossier : ", "The destination contains a file or link where a folder is required: ") + current);
        }
    }
    private static void ValidateCheckpoint(CopyPlan plan, string path)
    {
        path = Path.GetFullPath(path);
        if (FileSafety.IsWithin(plan.SourceRoot, path) || FileSafety.IsWithin(plan.TargetRoot, path)) throw new IOException(L.T("Le journal de copie doit être situé hors des dossiers comparés.", "The copy journal must be outside the compared folders."));
        var parent = Path.GetDirectoryName(path)!;
        var ancestor = parent;
        while (!Directory.Exists(ancestor)) ancestor = Path.GetDirectoryName(ancestor) ?? throw new IOException(L.T("Journal inaccessible.", "The journal is inaccessible."));
        var canonical = Path.Combine(FileSafety.CanonicalDirectory(ancestor), Path.GetRelativePath(ancestor, parent), Path.GetFileName(path));
        if (FileSafety.IsWithin(plan.SourceCanonical, canonical) || FileSafety.IsWithin(plan.TargetCanonical, canonical)) throw new IOException(L.T("Le journal de copie pointe dans un dossier comparé.", "The copy journal points inside a compared folder."));
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [StructLayout(LayoutKind.Sequential)]
    private struct StorageInfo
    {
        public uint LogicalBytesPerSector, PhysicalBytesPerSectorForAtomicity, PhysicalBytesPerSectorForPerformance,
            FileSystemEffectivePhysicalBytesPerSectorForAtomicity, Flags, ByteOffsetForSectorAlignment, ByteOffsetForPartitionAlignment;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out StorageInfo info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool GetVolumePathNameW(string path, StringBuilder volume, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong availableToCaller, out ulong total, out ulong free);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

    private sealed class JournalUnavailable(string message, Exception inner) : IOException(message, inner);

    private sealed class ItemState
    {
        public string? PartialName { get; set; }
        public bool Completed { get; set; }
        public string? VerifiedHash { get; set; }
        public string? PartialIdentity { get; set; }
        public string? DestinationIdentity { get; set; }
        public bool? CreatedByCopy { get; set; }
    }

    /// <summary>
    /// JSON Lines journal: the first line is the whole plan, each later line the latest state of one item.
    /// Rewriting the plan for every state made a copy quadratic in its item count; it is now compacted only
    /// when the appended states reach a quarter of its size, and once more when the run stops.
    /// </summary>
    private sealed class Journal : IDisposable
    {
        private const long MinimumCompaction = 1024 * 1024;
        private readonly CopyPlan _plan;
        private readonly string _path;
        private readonly System.Diagnostics.Stopwatch _sinceDurable = new();
        private FileStream? _stream;
        private long _compactedLength;
        private Journal(CopyPlan plan, string path) { _plan = plan; _path = path; }

        public static Journal Open(CopyPlan plan, string path)
        {
            var journal = new Journal(plan, Path.GetFullPath(path));
            journal.Compact();
            return journal;
        }

        public void Record(CopyItem item)
        {
            var state = JsonSerializer.SerializeToUtf8Bytes(new ItemState
            {
                PartialName = item.PartialName, Completed = item.Completed, VerifiedHash = item.VerifiedHash,
                PartialIdentity = item.PartialIdentity, DestinationIdentity = item.DestinationIdentity, CreatedByCopy = item.CreatedByCopy
            });
            // A journal that a failed compaction could not reopen is tried once more; without it no byte is written.
            if (_stream is null) Reopen();
            _stream!.Write(state); _stream.WriteByte((byte)'\n');
            // Reaching the system cache survives an application crash. Forcing it to disk at most once per second
            // means a power loss can only lose recent states; resume then refuses those items instead of trusting them.
            _stream.Flush();
            if (_stream.Length - _compactedLength > Math.Max(MinimumCompaction, _compactedLength / 4))
            {
                try { Compact(); }
                // The appended state is already saved; postpone compaction rather than failing this item.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (_stream is null) Reopen();
                    _compactedLength = _stream!.Length;
                }
            }
            else if (_sinceDurable.ElapsedMilliseconds >= 1000) { _stream.Flush(true); _sinceDurable.Restart(); }
        }

        /// <summary>Reopens the journal for appends, or reports it unavailable: no byte is then copied without a record.</summary>
        private void Reopen()
        {
            try
            {
                _stream = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);
                _stream.Seek(0, SeekOrigin.End);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _stream = null;
                throw new JournalUnavailable(L.F($"Journal de copie inaccessible ({ex.Message}). Copie arrêtée ; elle reprendra une fois le journal de nouveau accessible.",
                    $"Copy journal unavailable ({ex.Message}). Copy stopped; it resumes once the journal is accessible again."), ex);
            }
        }

        private void Compact()
        {
            ValidateCheckpoint(_plan, _path); Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, _plan); stream.WriteByte((byte)'\n'); stream.Flush(true); }
                _stream?.Dispose(); _stream = null;
                // Replace the directory entry, never truncate a possible pre-existing hard link.
                File.Move(temporary, _path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                // Appends continue the previous journal if it could not be replaced.
                // Left closed if that fails too: the next record tries again, then stops the copy.
                if (_stream is null && File.Exists(_path))
                    try { Reopen(); }
                    catch (JournalUnavailable) { }
            }
            _compactedLength = _stream?.Length ?? 0;
            _sinceDurable.Restart();
        }

        public void Dispose()
        {
            // A stopped run leaves a single plan line, as earlier versions wrote. If that fails, the appended states remain.
            try { Compact(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                try { _stream?.Flush(true); }
                catch (IOException) { }
            }
            _stream?.Dispose();
        }
    }
}
