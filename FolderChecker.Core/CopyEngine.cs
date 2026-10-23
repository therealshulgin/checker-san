using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public List<CopyItem> Items { get; set; } = [];
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
    /// A copy needs a comparison without errors. Online-only files present on both sides stay unverified,
    /// but they are never copied, so they do not prevent completing the folders.
    /// </summary>
    public static bool CanCreatePlan(ComparisonResult comparison) =>
        !comparison.Cancelled && comparison.SnapshotVerified && comparison.Errors.Count == 0 &&
        comparison.Rows.All(row => row.Status != ComparisonStatus.Unverified || row.OnlineOnly);

    public static CopyPlan CreatePlan(ComparisonResult comparison, bool sourceToTarget)
    {
        ValidateComparison(comparison);
        var source = sourceToTarget ? comparison.SourceRoot : comparison.TargetRoot;
        var target = sourceToTarget ? comparison.TargetRoot : comparison.SourceRoot;
        var plan = new CopyPlan { SourceRoot = source, TargetRoot = target, SourceCanonical = FileSafety.CanonicalDirectory(source), TargetCanonical = FileSafety.CanonicalDirectory(target) };
        ValidateRoots(plan);
        AddMissingItems(plan, comparison, sourceToTarget, reverseDirection: false);
        FinishPlan(plan);
        return plan;
    }

    public static CopyPlan CreateBidirectionalPlan(ComparisonResult comparison)
    {
        ValidateComparison(comparison);
        var plan = new CopyPlan
        {
            Bidirectional = true,
            SourceRoot = comparison.SourceRoot,
            TargetRoot = comparison.TargetRoot,
            SourceCanonical = FileSafety.CanonicalDirectory(comparison.SourceRoot),
            TargetCanonical = FileSafety.CanonicalDirectory(comparison.TargetRoot)
        };
        ValidateRoots(plan);
        AddMissingItems(plan, comparison, sourceToTarget: true, reverseDirection: false);
        AddMissingItems(plan, comparison, sourceToTarget: false, reverseDirection: true);
        FinishPlan(plan);
        return plan;
    }

    private static void ValidateComparison(ComparisonResult comparison)
    {
        if (!CanCreatePlan(comparison))
            throw new InvalidOperationException(L.T("Terminez une comparaison sans erreur avant de préparer une copie.", "Complete a comparison without errors before preparing a copy."));
    }

    private static void AddMissingItems(CopyPlan plan, ComparisonResult comparison, bool sourceToTarget, bool reverseDirection)
    {
        var source = reverseDirection ? plan.TargetRoot : plan.SourceRoot;
        var blocked = comparison.Rows
            .Where(r => r.Status is ComparisonStatus.Conflict or ComparisonStatus.Unverified or ComparisonStatus.Skipped)
            .Select(r => r.RelativePath.Replace('/', '\\').TrimEnd('\\') + "\\")
            .ToArray();
        // Copying an online-only file would download it, and its changing state would fail the source checks.
        foreach (var row in comparison.Rows.Where(r => !r.OnlineOnly && (sourceToTarget
                     ? r.Status is ComparisonStatus.OnlySource or ComparisonStatus.Moved
                     : r.Status == ComparisonStatus.OnlyTarget)))
        {
            // An absent descendant cannot be created below a file/directory conflict or through a link.
            var relative = row.RelativePath.Replace('/', '\\');
            if (blocked.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) continue;
            var path = SafePath(source, row.RelativePath);
            AssertNoLinks(source, path);
            var item = new CopyItem { RelativePath = row.RelativePath, Kind = row.Kind, ReverseDirection = reverseDirection };
            if (row.Kind == EntryKind.File)
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                item.Size = file.Length;
                item.LastWriteUtc = File.GetLastWriteTimeUtc(file.SafeFileHandle);
                item.CreationUtc = File.GetCreationTimeUtc(file.SafeFileHandle);
                item.Attributes = File.GetAttributes(file.SafeFileHandle) & CopiedAttributes;
                item.SourceIdentity = IdentityText(FileSafety.Identity(file.SafeFileHandle));
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
        foreach (var item in plan.Items.Where(i => i.Kind == EntryKind.File && !i.Completed))
        {
            var root = item.ReverseDirection ? plan.SourceRoot : plan.TargetRoot;
            var remaining = item.Size;
            if (item.PartialIdentity is not null)
            {
                var partial = new FileInfo(Path.Combine(Path.GetDirectoryName(SafePath(root, item.RelativePath))!, item.PartialName));
                if (partial.Exists) remaining = Math.Max(0, remaining - partial.Length);
            }
            var volume = volumes[root];
            needs[volume] = (needs[volume].Root, needs[volume].Required + (remaining + 4095) / 4096 * 4096);
        }
        return needs.Values.Select(need => new SpaceNeed(need.Root, need.Required, FreeSpace(need.Root))).ToArray();
    }

    private static string VolumeOf(string root)
    {
        var volume = new StringBuilder(32768);
        if (!GetVolumePathNameW(root, volume, (uint)volume.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return volume.ToString();
    }

    private static long FreeSpace(string root)
    {
        // Free space available to this user, after quotas; works for drive letters, mount points and shares.
        if (!GetDiskFreeSpaceExW(root, out var available, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return available > long.MaxValue ? long.MaxValue : (long)available;
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
            using var cached = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
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
        byte[] buffer = new byte[1024 * 1024], prior = new byte[buffer.Length];
        var checkBuffer = AlignedBuffer(1024 * 1024);
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
                    await Retry(async () =>
                    {
                        ValidateRoots(plan);
                        var source = SafePath(sourceRoot, item.RelativePath);
                        var target = SafePath(targetRoot, item.RelativePath);
                        AssertNoLinks(sourceRoot, source);
                        if (item.Kind == EntryKind.Directory)
                        {
                            if ((File.GetAttributes(source) & FileAttributes.Directory) == 0) throw new IOException(L.T("Le dossier source a changé de type.", "The source folder has changed type."));
                            EnsureDirectory(targetRoot, target);
                            item.Completed = true; journal.Record(item);
                            return true;
                        }
                        EnsureDirectory(targetRoot, Path.GetDirectoryName(target)!);
                        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        ValidateSource(item, input);
                        if (Exists(target))
                        {
                            AssertNoLinks(targetRoot, target);
                            if (item.VerifiedHash is null) throw new IOException(L.T("La destination existe déjà : elle n’a pas été remplacée.", "The destination already exists and has not been replaced."));
                            using var existing = File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.Read, NoBuffering);
                            var identity = IdentityText(FileSafety.Identity(existing));
                            var length = RandomAccess.GetLength(existing);
                            if (!(item.Completed && identity is not null && identity == item.DestinationIdentity && length == item.Size))
                            {
                                var phase = L.T("Vérification SHA-256 de la copie", "Verifying the copy with SHA-256");
                                var hash = HashFromDisk(existing, target, checkBuffer, () => Report(phase, displayPath), ct);
                                if (length != item.Size || hash != item.VerifiedHash) throw new IOException(L.T("La destination existante a un contenu différent : aucun écrasement.", "The existing destination has different contents and will not be overwritten."));
                            }
                            item.DestinationIdentity = identity; item.Completed = true;
                            journal.Record(item);
                            return true;
                        }
                        item.Completed = false;
                        var partial = Path.Combine(Path.GetDirectoryName(target)!, item.PartialName);
                        AssertNoLinks(targetRoot, partial);
                        var existed = Exists(partial);
                        string expectedHash;
                        using (var output = new FileStream(partial, existed ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            var partialId = IdentityText(FileSafety.Identity(output.SafeFileHandle), includeChangeTime: false);
                            if (existed && (item.PartialIdentity is null || partialId != item.PartialIdentity))
                                throw new IOException(L.T("Le fichier temporaire ne peut plus être identifié. Conservez-le et démarrez une nouvelle copie depuis un nouveau scan.", "The temporary file can no longer be identified. Keep it and start a new copy after scanning again."));
                            item.PartialIdentity = partialId;
                            journal.Record(item);
                            using var sourceHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            long offset = output.Length;
                            if (offset > input.Length) throw new IOException(L.T("Le fichier temporaire a une taille incohérente.", "The temporary file has an inconsistent size."));
                            long checkedBytes = 0;
                            while (checkedBytes < offset)
                            {
                                ct.ThrowIfCancellationRequested();
                                int wanted = (int)Math.Min(buffer.Length, offset - checkedBytes);
                                await input.ReadExactlyAsync(buffer.AsMemory(0, wanted), ct);
                                await output.ReadExactlyAsync(prior.AsMemory(0, wanted), ct);
                                if (!buffer.AsSpan(0, wanted).SequenceEqual(prior.AsSpan(0, wanted)))
                                    throw new IOException(L.T("La partie déjà copiée diffère de la source. Aucun contenu existant n’a été écrasé ; relancez une nouvelle copie après comparaison.", "The previously copied portion differs from the source. Existing contents have not been overwritten; compare the folders again before starting a new copy."));
                                sourceHasher.AppendData(buffer, 0, wanted); checkedBytes += wanted;
                                Report(L.T("Vérification de la partie déjà copiée", "Checking the previously copied portion"), displayPath);
                            }
                            resumed += offset;
                            int read;
                            while ((read = await input.ReadAsync(buffer, ct)) != 0)
                            {
                                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                                sourceHasher.AppendData(buffer, 0, read); written += read;
                                Report(L.T("Copie", "Copying"), displayPath);
                            }
                            await output.FlushAsync(ct);
                            // Explicitly set dates are kept when the handle closes; they are flushed with the data.
                            File.SetCreationTimeUtc(output.SafeFileHandle, item.CreationUtc);
                            File.SetLastWriteTimeUtc(output.SafeFileHandle, item.LastWriteUtc);
                            output.Flush(true);
                            ValidateSource(item, input);
                            expectedHash = Convert.ToHexString(sourceHasher.GetHashAndReset());
                        }
                        var verifying = L.T("Vérification SHA-256 de la copie", "Verifying the copy with SHA-256");
                        Report(verifying, displayPath);
                        // Writers stay excluded until the final name exists; sharing deletion only allows that rename.
                        using var check = File.OpenHandle(partial, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, NoBuffering);
                        if (RandomAccess.GetLength(check) != item.Size || HashFromDisk(check, partial, checkBuffer, () => Report(verifying, displayPath), ct) != expectedHash)
                            throw new IOException(L.T("La vérification SHA-256 de la copie a échoué.", "SHA-256 verification of the copy failed."));
                        item.VerifiedHash = expectedHash;
                        journal.Record(item);
                        ct.ThrowIfCancellationRequested();
                        AssertNoLinks(targetRoot, Path.GetDirectoryName(target)!);
                        // Both handles have been validated; final move is no-overwrite and on the same volume/share.
                        File.Move(partial, target, overwrite: false);
                        // Applied after the rename: a read-only temporary file could not be resumed.
                        if (item.Attributes is { } attributes) File.SetAttributes(target, attributes == 0 ? FileAttributes.Normal : attributes);
                        item.DestinationIdentity = IdentityText(FileSafety.Identity(check));
                        item.Completed = true; journal.Record(item);
                        return true;
                    }, displayPath);
                    if (item.Kind == EntryKind.File) completed++;
                    Report(L.T("Élément terminé", "Item completed"), displayPath);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (FileSystemRetryPolicy.IsTransient(ex)) { errors.Add(L.F($"{displayPath} : connexion indisponible après trois tentatives. {ex.Message}", $"{displayPath}: connection unavailable after three retries. {ex.Message}")); interrupted = true; break; }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 39 or 112)
                { errors.Add(L.F($"{displayPath} : espace disque insuffisant. Copie arrêtée ; libérez de la place puis reprenez.", $"{displayPath}: not enough disk space. Copy stopped; free some space, then resume.")); interrupted = true; break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
                { errors.Add(L.F($"{displayPath} : {ex.Message}", $"{displayPath}: {ex.Message}")); }
            }
            // Adding files changes their folders' dates, so folders receive the source's dates last.
            if (!interrupted)
                foreach (var item in plan.Items.Where(i => i.Kind == EntryKind.Directory && i.Completed && i.LastWriteUtc != default))
                {
                    ct.ThrowIfCancellationRequested();
                    var targetRoot = item.ReverseDirection ? plan.SourceRoot : plan.TargetRoot;
                    try
                    {
                        var target = SafePath(targetRoot, item.RelativePath);
                        AssertNoLinks(targetRoot, target);
                        if (item.Attributes is { } attributes) File.SetAttributes(target, attributes == 0 ? FileAttributes.Normal : attributes);
                        Directory.SetCreationTimeUtc(target, item.CreationUtc);
                        Directory.SetLastWriteTimeUtc(target, item.LastWriteUtc);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { errors.Add(L.F($"{item.RelativePath} : dates du dossier non appliquées. {ex.Message}", $"{item.RelativePath}: folder dates not applied. {ex.Message}")); }
                }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { cancelled = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { errors.Add(ex.Message); }
        return new(cancelled, completed, totalFiles, written, resumed, errors);
    }

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
    private static extern bool GetVolumePathNameW(string path, StringBuilder volume, uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong availableToCaller, out ulong total, out ulong free);

    private sealed class ItemState
    {
        public string? PartialName { get; set; }
        public bool Completed { get; set; }
        public string? VerifiedHash { get; set; }
        public string? PartialIdentity { get; set; }
        public string? DestinationIdentity { get; set; }
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
                PartialIdentity = item.PartialIdentity, DestinationIdentity = item.DestinationIdentity
            });
            _stream!.Write(state); _stream.WriteByte((byte)'\n');
            // Reaching the system cache survives an application crash. Forcing it to disk at most once per second
            // means a power loss can only lose recent states; resume then refuses those items instead of trusting them.
            _stream.Flush();
            if (_stream.Length - _compactedLength > Math.Max(MinimumCompaction, _compactedLength / 4))
            {
                try { Compact(); }
                // The appended state is already saved; postpone compaction rather than failing this item.
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _compactedLength = _stream.Length; }
            }
            else if (_sinceDurable.ElapsedMilliseconds >= 1000) { _stream.Flush(true); _sinceDurable.Restart(); }
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
                if (_stream is null && File.Exists(_path))
                {
                    _stream = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);
                    _stream.Seek(0, SeekOrigin.End);
                }
            }
            _compactedLength = _stream!.Length;
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
