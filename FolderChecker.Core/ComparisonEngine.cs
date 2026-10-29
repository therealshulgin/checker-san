using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

/// <summary>Read-only input comparison, with optional resumable SHA-256 checkpoints outside the input trees.</summary>
public sealed class ComparisonEngine
{
    static ComparisonEngine() => FileSafety.Initialize();

    public static CheckpointInfo? ReadCheckpointInfo(string path)
    {
        var data = CheckpointData.Read(path);
        return data is null ? null : new CheckpointInfo
        {
            SourceRoot = data.SourceRoot, TargetRoot = data.TargetRoot, FindMovedFiles = data.FindMovedFiles,
            SavedAt = data.SavedAt, CompletedFileCount = data.Files.Count
        };
    }

    /// <summary>
    /// Scans and hashes on a worker thread. Cancellation returns a partial result with all
    /// global verdicts false. Invalid, identical, or nested roots throw ArgumentException.
    /// Persistent hashes are reused only when explicitly requested and the file's handle identity,
    /// change time, size, and timestamps still match the saved checkpoint.
    /// </summary>
    public Task<ComparisonResult> CompareAsync(string source, string target, ComparisonOptions options,
        IProgress<ComparisonProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxRetries);
        ArgumentOutOfRangeException.ThrowIfNegative(options.InitialRetryDelayMs);
        return Task.Run(() => new ComparisonRun(source, target, options, progress, cancellationToken).ExecuteAsync());
    }

    private sealed class ComparisonRun(string source, string target, ComparisonOptions options,
        IProgress<ComparisonProgress>? progress, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, ComparisonRow> _rows = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _hashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _samples = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _coveredSourceFiles = new(StringComparer.OrdinalIgnoreCase);
        // Full path of a file named by rows at another path than its own (a moved file's copy, a copy found in folder 1) → those rows.
        private readonly Dictionary<string, List<string>> _referencedBy = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _errors = new(StringComparer.Ordinal);
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CheckpointFile> _checkpointFiles = new(StringComparer.OrdinalIgnoreCase);
        // Identity of each file when its hash was proven, checked again by handle at the final check.
        private readonly Dictionary<string, (Entry Entry, FileIdentity Identity)> _readIdentities = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _progressTimer = Stopwatch.StartNew();
        private readonly Stopwatch _checkpointTimer = Stopwatch.StartNew();
        private long _filesDiscovered, _filesHashed, _bytesHashed, _sampleBytesRead, _totalHashBytes, _filesReused, _lastSaveMilliseconds;
        private int _savedEntries;
        private const long SampleThreshold = 4 * 1024 * 1024;
        private const int SampleBlockSize = 64 * 1024;
        private int _retryAttempt;
        private string? _checkpointPath;
        private string _sourceRoot = "", _targetRoot = "", _sourceCanonical = "", _targetCanonical = "";
        private bool _hasCheckpoint, _checkpointSaved, _checkpointDirty;
        private string _phase = L.T("Préparation", "Preparing");
        private string _currentPath = "";
        // Guards the shared caches, counters and checkpoint while several files are read at once.
        private readonly object _gate = new();
        private bool _saving;
        private DeviceReaders? _readers;

        public async Task<ComparisonResult> ExecuteAsync()
        {
            var started = DateTimeOffset.Now;
            var sourceRoot = source;
            var targetRoot = target;
            var sourceSnapshot = new Snapshot(sourceRoot);
            var targetSnapshot = new Snapshot(targetRoot);
            var scansComplete = false;
            var cancelled = false;
            var snapshotVerified = false;
            var finished = false;

            try
            {
                sourceRoot = await RetryIoAsync(() => NormalizeRoot(source, L.T("dossier 1", "folder 1")), source);
                targetRoot = await RetryIoAsync(() => NormalizeRoot(target, L.T("dossier 2", "folder 2")), target);
                sourceSnapshot = new Snapshot(sourceRoot);
                targetSnapshot = new Snapshot(targetRoot);
                await RetryIoAsync(() => { ValidateSeparateRoots(sourceRoot, targetRoot); return true; }, sourceRoot);
                _readers = new DeviceReaders([sourceRoot, targetRoot], options.ParallelReads);
                await PrepareCheckpointAsync(sourceRoot, targetRoot);
                SetPhase(L.T("Inventaire du dossier 1", "Listing folder 1"));
                await ScanAsync(sourceSnapshot, true);
                SetPhase(L.T("Inventaire du dossier 2", "Listing folder 2"));
                await ScanAsync(targetSnapshot, true);
                scansComplete = true;
                BuildInitialRows(sourceSnapshot, targetSnapshot);
                PlanHashBytes(sourceSnapshot, targetSnapshot);
                SetPhase(L.T("Vérification SHA-256 des fichiers correspondants", "Verifying matching files with SHA-256"));
                await PrefetchPairsAsync(sourceSnapshot, targetSnapshot);

                foreach (var item in sourceSnapshot.Entries.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Readable(item) || !targetSnapshot.Entries.TryGetValue(item.RelativePath, out var other) ||
                        !Readable(other) || item.Size != other.Size)
                        continue;

                    // A reusable completed SHA takes priority over sampling: resumed identical
                    // files should require metadata/identity checks only, with no content reads.
                    if (ShouldSample(item) && !_hashes.ContainsKey(item.FullPath) && !_hashes.ContainsKey(other.FullPath) &&
                        !_checkpointFiles.ContainsKey(item.FullPath) && !_checkpointFiles.ContainsKey(other.FullPath))
                    {
                        var sourceSample = await SampleAsync(item);
                        var targetSample = await SampleAsync(other);
                        if (sourceSample is null || targetSample is null)
                        {
                            _rows[item.RelativePath] = _rows[item.RelativePath] with
                            {
                                Status = ComparisonStatus.Unverified,
                                Detail = L.T("Présélection impossible : consultez les erreurs.", "Candidate filtering failed: see the errors.")
                            };
                            continue;
                        }
                        if (sourceSample != targetSample)
                        {
                            _rows[item.RelativePath] = _rows[item.RelativePath] with
                            {
                                Status = ComparisonStatus.Different,
                                Detail = L.T("Même chemin et même taille, mais des octets diffèrent dans les échantillons lus.", "Same path and size, but the sampled bytes differ.")
                            };
                            continue;
                        }
                    }

                    var sourceHash = await HashAsync(item);
                    var targetHash = await HashAsync(other);
                    var valid = sourceHash is not null && targetHash is not null;
                    var identical = valid && sourceHash == targetHash;
                    _rows[item.RelativePath] = _rows[item.RelativePath] with
                    {
                        Status = !valid ? ComparisonStatus.Unverified : identical ? ComparisonStatus.Identical : ComparisonStatus.Different,
                        Detail = !valid ? L.T("Vérification impossible : consultez les erreurs.", "Verification failed: see the errors.") : identical
                            ? L.T("Même chemin et contenu identique (SHA-256 complet).", "Same path and identical contents (complete SHA-256).")
                            : L.T("Même chemin et même taille, mais contenu différent (SHA-256).", "Same path and size, but different contents (SHA-256)."),
                        SourceHash = sourceHash,
                        TargetHash = targetHash
                    };
                    if (identical) _coveredSourceFiles.Add(item.RelativePath);
                }

                if (options.FindMovedFiles)
                {
                    await FindMovedFilesAsync(sourceSnapshot, targetSnapshot);
                    await FindTargetCopiesAsync(sourceSnapshot, targetSnapshot);
                }
                MarkCaseDifferences(sourceSnapshot, targetSnapshot);

                cancellationToken.ThrowIfCancellationRequested();
                SetPhase(L.T("Contrôle final des modifications", "Final check for changes"));
                var finalSource = new Snapshot(sourceRoot);
                var finalTarget = new Snapshot(targetRoot);
                await ScanAsync(finalSource, false);
                await ScanAsync(finalTarget, false);
                var sourceStable = VerifySnapshot(sourceSnapshot, finalSource, true);
                var targetStable = VerifySnapshot(targetSnapshot, finalTarget, false);
                var contentsStable = await VerifyReadIdentitiesAsync();
                snapshotVerified = sourceStable && targetStable && contentsStable && _errors.Count == 0;
                cancellationToken.ThrowIfCancellationRequested();
                finished = true;
                SetPhase(L.T("Comparaison terminée", "Comparison completed"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                if (!scansComplete)
                {
                    BuildInitialRows(sourceSnapshot, targetSnapshot);
                    foreach (var key in _rows.Keys.ToArray())
                        _rows[key] = _rows[key] with { Status = ComparisonStatus.Unverified, Detail = L.T("Inventaire interrompu : présence non confirmée.", "Listing interrupted: presence has not been confirmed.") };
                }
                SetPhase(L.T("Comparaison annulée", "Comparison cancelled"));
            }
            catch (Exception exception) when (FileSystemRetryPolicy.IsTransient(exception))
            {
                var problem = L.F($"Connexion indisponible après {options.MaxRetries} nouvelle(s) tentative(s) : {exception.Message} Reprenez la comparaison lorsque le dossier est de nouveau accessible.", $"Connection unavailable after {options.MaxRetries} retries: {exception.Message} Resume the comparison when the folder is accessible again.");
                _errors.Add(problem);
                if (!scansComplete) BuildInitialRows(sourceSnapshot, targetSnapshot);
                foreach (var key in _rows.Keys.ToArray())
                    if (!scansComplete || _rows[key].Status == ComparisonStatus.Unverified)
                        _rows[key] = _rows[key] with { Status = ComparisonStatus.Unverified, Detail = problem };
                SetPhase(L.T("Connexion interrompue — reprise disponible après reconnexion", "Connection lost — resume after reconnecting"));
            }
            finally
            {
                _checkpointSaved = SaveCheckpoint(true);
            }

            var orderedRows = _rows.Values.OrderBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            var certain = !cancelled && snapshotVerified && _errors.Count == 0;
            var sourceContained = certain && IsStrictlyContained(sourceSnapshot);
            var targetContained = certain && IsStrictlyContained(targetSnapshot);
            var sourceFileCount = sourceSnapshot.Entries.Values.LongCount(item => item.Kind == EntryKind.File && item.Access != EntryAccess.Link);
            return new ComparisonResult
            {
                SourceRoot = sourceRoot,
                TargetRoot = targetRoot,
                Rows = orderedRows,
                Errors = _errors.ToArray(),
                StartedAt = started,
                CompletedAt = DateTimeOffset.Now,
                Cancelled = cancelled,
                Finished = finished && !cancelled,
                MovedFilesSearched = options.FindMovedFiles,
                SnapshotVerified = snapshotVerified && !cancelled,
                SourceFullyContained = sourceContained,
                TargetFullyContained = targetContained,
                TreesIdentical = sourceContained && targetContained,
                ContentCovered = certain && _coveredSourceFiles.Count == sourceFileCount,
                TargetContentCovered = certain && targetSnapshot.Entries.Values
                    .Where(item => item.Kind == EntryKind.File && item.Access != EntryAccess.Link)
                    .All(item => _rows.TryGetValue(item.RelativePath, out var row) &&
                        (row.Status == ComparisonStatus.Identical || row is { Status: ComparisonStatus.OnlyTarget, SourceCopyPath: not null })),
                SourceFileCount = sourceFileCount,
                TargetFileCount = targetSnapshot.Entries.Values.LongCount(item => item.Kind == EntryKind.File && item.Access != EntryAccess.Link),
                FilesVerified = _filesHashed,
                BytesRead = _bytesHashed,
                SampleBytesRead = _sampleBytesRead,
                FilesReused = _filesReused,
                HasCheckpoint = _hasCheckpoint,
                CheckpointSaved = _checkpointSaved,
                CheckpointPath = _checkpointPath,
                Warnings = _warnings.ToArray(),
                ReadIdentities = _readIdentities.ToDictionary(pair => pair.Key, pair => pair.Value.Identity, StringComparer.OrdinalIgnoreCase)
            };
        }

        /// <summary>
        /// Reads same-path pairs ahead of the verdict loop, as many at once as their devices allow. The loop then
        /// finds every sample and digest already computed, so its decisions and their order do not change. With a
        /// single reader nothing is gained and the loop reads in its usual order.
        /// </summary>
        private async Task PrefetchPairsAsync(Snapshot sourceSnapshot, Snapshot targetSnapshot)
        {
            if (_readers is not { Slots: > 1 } readers) return;
            var pairs = sourceSnapshot.Entries.Values
                .Select(item => (Source: item, Target: targetSnapshot.Entries.GetValueOrDefault(item.RelativePath)))
                .Where(pair => Readable(pair.Source) && pair.Target is not null && Readable(pair.Target) && pair.Source.Size == pair.Target.Size)
                .ToArray();
            await Parallel.ForEachAsync(pairs, new ParallelOptions { MaxDegreeOfParallelism = readers.Slots, CancellationToken = cancellationToken }, async (pair, token) =>
            {
                var (item, other) = (pair.Source, pair.Target!);
                bool known;
                lock (_gate)
                    known = _hashes.ContainsKey(item.FullPath) || _hashes.ContainsKey(other.FullPath) ||
                        _checkpointFiles.ContainsKey(item.FullPath) || _checkpointFiles.ContainsKey(other.FullPath);
                if (ShouldSample(item) && !known)
                {
                    var samples = await Task.WhenAll(readers.Run(item.FullPath, () => SampleAsync(item), token), readers.Run(other.FullPath, () => SampleAsync(other), token));
                    if (samples[0] is null || samples[1] is null || samples[0] != samples[1]) return;
                }
                await Task.WhenAll(readers.Run(item.FullPath, () => HashAsync(item), token), readers.Run(other.FullPath, () => HashAsync(other), token));
            });
        }

        private bool IsStrictlyContained(Snapshot snapshot) =>
            // Moved rows can point at a target file under another relative path. Inventory keys
            // preserve which entries actually belong to each tree and require a same-path match.
            // A link is not content of its own tree, but a real entry facing a link still fails.
            snapshot.Entries.All(pair => pair.Value.Access == EntryAccess.Link || (_rows.TryGetValue(pair.Key, out var row) &&
                row.Status is ComparisonStatus.Identical or ComparisonStatus.CommonDirectory));

        private Task<T> RetryIoAsync<T>(Func<T> operation, string path) => RetryOperationAsync(() => Task.FromResult(operation()), path);

        private async Task<T> RetryOperationAsync<T>(Func<Task<T>> operation, string path)
        {
            var previousPhase = _phase;
            try
            {
                return await FileSystemRetryPolicy.ExecuteAsync(_ => operation(), options.MaxRetries, options.InitialRetryDelayMs,
                    (attempt, delay) =>
                    {
                        _retryAttempt = attempt;
                        _phase = L.F($"Connexion interrompue — tentative {attempt}/{options.MaxRetries} dans {delay.TotalSeconds:0.#} s", $"Connection lost — retry {attempt}/{options.MaxRetries} in {delay.TotalSeconds:0.#} s");
                        _currentPath = path;
                        ReportProgress(true);
                    }, cancellationToken);
            }
            finally
            {
                _retryAttempt = 0;
                _phase = previousPhase;
                ReportProgress(true);
            }
        }

        private async Task PrepareCheckpointAsync(string sourceRoot, string targetRoot)
        {
            if (string.IsNullOrWhiteSpace(options.CheckpointPath)) return;
            _sourceRoot = sourceRoot;
            _targetRoot = targetRoot;
            _sourceCanonical = await RetryIoAsync(() => CanonicalRoot(sourceRoot), sourceRoot);
            _targetCanonical = await RetryIoAsync(() => CanonicalRoot(targetRoot), targetRoot);
            var path = Path.GetFullPath(options.CheckpointPath);
            ValidateCheckpointPath(path);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                ValidateCheckpointPath(path);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                _warnings.Add(L.F($"La session de reprise ne peut pas être enregistrée : {exception.Message}", $"The resume session could not be saved: {exception.Message}"));
                return;
            }
            _checkpointPath = path;
            if (!options.ResumeFromCheckpoint) return;
            var data = CheckpointData.Read(path);
            if (data is null)
            {
                if (File.Exists(path)) _warnings.Add(L.T("Le fichier de reprise est illisible ou invalide. Les fichiers seront vérifiés à nouveau.", "The resume file is unreadable or invalid. Files will be verified again."));
                return;
            }
            if (!string.Equals(data.SourceRoot, sourceRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(data.TargetRoot, targetRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(data.SourceCanonicalRoot, _sourceCanonical, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(data.TargetCanonicalRoot, _targetCanonical, StringComparison.OrdinalIgnoreCase) ||
                data.FindMovedFiles != options.FindMovedFiles)
            {
                _warnings.Add(L.T("La session enregistrée concerne d’autres dossiers ou options. Une nouvelle comparaison a été démarrée.", "The saved session uses different folders or options. A new comparison has been started."));
                return;
            }
            // A saved hash is trusted only where every write moves the file's change time (NTFS, ReFS): on FAT32 or
            // exFAT an edit that restores its date would look unchanged, so those files are read again.
            var sourceKeeps = FileSafety.KeepsChangeTime(sourceRoot);
            var targetKeeps = FileSafety.KeepsChangeTime(targetRoot);
            if (!sourceKeeps || !targetKeeps)
                _warnings.Add(L.T("Un des disques ne tient pas de date de changement fiable (FAT32, exFAT…) : ses fichiers sont relus, sans réutiliser la vérification précédente.", "One of the drives keeps no reliable change time (FAT32, exFAT…): its files are read again rather than trusting the previous check."));
            foreach (var file in data.Files)
            {
                if (!Path.IsPathFullyQualified(file.FullPath)) continue;
                if (!(IsWithin(sourceRoot, file.FullPath) ? sourceKeeps : IsWithin(targetRoot, file.FullPath) && targetKeeps)) continue;
                _checkpointFiles[file.FullPath] = file;
            }
            _hasCheckpoint = true;
        }

        private void ValidateCheckpointPath(string path)
        {
            // "folder:resume.json" is a hidden stream of the folder itself: writing it would change a compared root.
            if (Path.GetFileName(path).Contains(':'))
                throw new ArgumentException(L.T("Chemin de reprise invalide.", "Invalid resume path."), nameof(options.CheckpointPath));
            if (IsWithin(_sourceRoot, path) || IsWithin(_targetRoot, path))
                throw new ArgumentException(L.T("Le fichier de reprise doit être enregistré hors des deux dossiers comparés.", "The resume file must be saved outside both compared folders."), nameof(options.CheckpointPath));
            var parent = Path.GetDirectoryName(path) ?? throw new ArgumentException(L.T("Chemin de reprise invalide.", "Invalid resume path."));
            var ancestor = parent;
            while (true)
            {
                try
                {
                    if ((File.GetAttributes(ancestor) & FileAttributes.Directory) == 0)
                        throw new ArgumentException(L.T("Le parent du fichier de reprise n’est pas un dossier.", "The parent of the resume file is not a folder."));
                    break;
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    ancestor = Path.GetDirectoryName(ancestor) ?? throw new ArgumentException(L.T("Le dossier de reprise n’est pas accessible.", "The resume folder is inaccessible."));
                }
            }
            var canonicalParent = Path.GetFullPath(Path.Combine(CanonicalRoot(ancestor), Path.GetRelativePath(ancestor, parent)));
            var canonicalPath = Path.Combine(canonicalParent, Path.GetFileName(path));
            if (IsWithin(_sourceCanonical, canonicalPath) || IsWithin(_targetCanonical, canonicalPath))
                throw new ArgumentException(L.T("Le fichier de reprise pointe vers un des dossiers comparés via un lien ou une jonction.", "The resume file points inside a compared folder through a link or junction."), nameof(options.CheckpointPath));
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new ArgumentException(L.T("Le fichier de reprise ne doit pas être un lien, une jonction ou un dossier.", "The resume file must not be a link, junction or folder."));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { }
        }

        private bool SaveCheckpoint(bool force = false)
        {
            if (_checkpointPath is null) return false;
            List<CheckpointFile> files;
            lock (_gate)
            {
                // A reader finding another one saving skips its turn: the next save includes its file. Each save rewrites
                // every hash, so a save waits until the hashes have grown by a quarter (at most five minutes of work
                // lost in a crash), and at least twenty times its own duration: the bytes written stay a few times the
                // final file instead of growing with the square of the file count. Stopping always saves at once.
                var elapsed = _checkpointTimer.ElapsedMilliseconds;
                var due = elapsed >= Math.Max(1000, 20 * _lastSaveMilliseconds) &&
                    (_checkpointFiles.Count >= _savedEntries + Math.Max(1, _savedEntries / 4) || elapsed >= 300_000);
                if (_saving || (!force && (!_checkpointDirty || (_hasCheckpoint && !due)))) return false;
                _saving = true;
                _checkpointDirty = false;
                files = _checkpointFiles.Values.ToList();
            }
            string? temporary = null;
            var clock = Stopwatch.StartNew();
            try
            {
                ValidateCheckpointPath(_checkpointPath);
                var data = new CheckpointData
                {
                    SourceRoot = _sourceRoot, TargetRoot = _targetRoot,
                    SourceCanonicalRoot = _sourceCanonical, TargetCanonicalRoot = _targetCanonical,
                    FindMovedFiles = options.FindMovedFiles, SavedAt = DateTimeOffset.Now,
                    Files = files
                };
                temporary = Path.Combine(Path.GetDirectoryName(_checkpointPath)!, $".{Path.GetFileName(_checkpointPath)}.{Guid.NewGuid():N}.tmp");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, data);
                    stream.Flush(true);
                }
                // Replace the directory entry, never truncate a possible pre-existing hard link.
                // The antivirus may still hold the previous copy it is scanning: the save that ends a run, the one a
                // resume relies on, tries again briefly rather than losing every hash computed since the last save.
                for (var attempt = 1; ; attempt++)
                {
                    try { File.Move(temporary, _checkpointPath, true); break; }
                    catch (Exception exception) when (force && attempt < 5 && exception is IOException or UnauthorizedAccessException) { Thread.Sleep(100 * attempt); }
                }
                temporary = null;
                lock (_gate)
                {
                    _hasCheckpoint = true;
                    _lastSaveMilliseconds = clock.ElapsedMilliseconds;
                    _savedEntries = files.Count;
                    _checkpointTimer.Restart();
                }
                return true;
            }
            catch (Exception exception) when (IsFileSystemException(exception) || exception is ArgumentException or JsonException)
            {
                lock (_gate)
                {
                    _checkpointDirty = true;
                    _warnings.Add(L.F($"Enregistrement de la reprise impossible : {exception.Message}", $"Unable to save resume data: {exception.Message}"));
                }
                return false;
            }
            finally
            {
                lock (_gate) _saving = false;
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception exception) when (IsFileSystemException(exception)) { }
                }
            }
        }

        private async Task ScanAsync(Snapshot snapshot, bool countDiscovery)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var root = await RetryIoAsync(() =>
                {
                    return (Entry: ReadEntry(snapshot.Root, "."), Canonical: CanonicalRoot(snapshot.Root));
                }, snapshot.Root);
                snapshot.RootEntry = root.Entry;
                snapshot.CanonicalRoot = root.Canonical;
                if (snapshot.RootEntry.Problem is not null)
                {
                    _errors.Add(snapshot.RootEntry.Problem);
                    return;
                }
            }
            catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
            {
                var problem = L.F($"Impossible de lire la racine « {snapshot.Root} » : {exception.Message}", $"Unable to read root ‘{snapshot.Root}’: {exception.Message}");
                _errors.Add(problem);
                if (snapshot.RootEntry is not null) snapshot.RootEntry = snapshot.RootEntry with { Problem = problem };
                return;
            }

            // At the root of a volume, the recycle bin and restore points are the system's, not the user's: their
            // contents would count as copies that a purge then removes, and their other users' folders are unreadable.
            var volumeRoot = options.TreatRootsAsVolumes || FileSafety.IsVolumeRoot(snapshot.Root);
            var pending = new Stack<string>();
            pending.Push(snapshot.Root);
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _currentPath = directory;
                ReportProgress();
                try
                {
                    // Each attempt stays private until enumeration completes. A failed network
                    // attempt cannot leave duplicate entries or queued descendants behind.
                    var batch = await RetryIoAsync(() => ReadDirectoryBatch(snapshot.Root, directory), directory);
                    if (volumeRoot && directory == snapshot.Root && batch is not null)
                    {
                        var system = batch.Where(entry => entry.Kind == EntryKind.Directory && SystemFolders.IsVolumeSystemFolder(entry.RelativePath)).ToArray();
                        if (system.Length > 0)
                        {
                            batch = batch.Except(system).ToList();
                            _warnings.Add(L.F($"Racine de volume « {snapshot.Root} » : {string.Join(", ", system.Select(entry => entry.RelativePath))} écarté(s) de la comparaison (corbeille et dossiers système).",
                                $"Volume root ‘{snapshot.Root}’: {string.Join(", ", system.Select(entry => entry.RelativePath))} left out of the comparison (recycle bin and system folders)."));
                        }
                    }
                    if (batch is null)
                    {
                        MarkDirectoryProblem(snapshot, directory, L.T("Lien symbolique ou point de jonction ignoré.", "Symbolic link or junction skipped."));
                        continue;
                    }
                    foreach (var entry in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = entry.RelativePath;
                        if (snapshot.Entries.TryGetValue(relative, out var collision))
                        {
                            var problem = L.F($"Chemins ambigus, distincts seulement par la casse : « {collision.FullPath} » et « {entry.FullPath} ».", $"Ambiguous paths differing only by case: ‘{collision.FullPath}’ and ‘{entry.FullPath}’.");
                            _errors.Add(problem);
                            snapshot.Entries[relative] = collision with { Problem = problem };
                            continue;
                        }
                        snapshot.Entries.Add(relative, entry);
                        if (entry.Problem is not null) _errors.Add(entry.Problem);
                        if (entry.Kind == EntryKind.File)
                        {
                            if (countDiscovery && entry.Access != EntryAccess.Link) _filesDiscovered++;
                        }
                        else if (entry.Problem is null && entry.Access != EntryAccess.Link) pending.Push(entry.FullPath);
                        ReportProgress();
                    }
                }
                catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
                {
                    MarkDirectoryProblem(snapshot, directory, L.F($"Dossier non entièrement lisible : {exception.Message}", $"The folder could not be read completely: {exception.Message}"));
                }
            }
        }

        private List<Entry>? ReadDirectoryBatch(string root, string directory)
        {
            if (FileSafety.Access(directory, File.GetAttributes(directory)) == EntryAccess.Link) return null;
            var result = new List<Entry>();
            // A larger listing buffer means fewer round trips per folder on network shares.
            var enumeration = new EnumerationOptions
            {
                RecurseSubdirectories = false, IgnoreInaccessible = false,
                AttributesToSkip = 0, ReturnSpecialDirectories = false, BufferSize = 64 * 1024
            };
            foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", enumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = FileSafety.RelativeTo(root, info.FullName);
                try
                {
                    result.Add(ReadEnumeratedEntry(info, relative));
                }
                catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
                {
                    result.Add(new Entry(relative, info.FullName, info is DirectoryInfo ? EntryKind.Directory : EntryKind.File,
                        null, default, default, 0, L.F($"Impossible de lire « {info.FullName} » : {exception.Message}", $"Unable to read ‘{info.FullName}’: {exception.Message}")));
                }
                ReportProgress();
            }
            return result;
        }

        private static Entry ReadEnumeratedEntry(FileSystemInfo info, string relative)
        {
            // NTFS directory-index timestamps can lag behind child creation. Refresh
            // directories so our own traversal does not look like an external edit.
            // File entries retain the metadata obtained in the batched enumeration.
            if (info is DirectoryInfo) info.Refresh();
            // Directory enumeration already supplies these metadata on Windows. Reuse
            // that snapshot instead of issuing several additional SMB requests per entry.
            // The final inventory performs a new enumeration; reads still use ReadEntry
            // and handle checks to validate current metadata before/after content access.
            var attributes = info.Attributes;
            if (attributes == (FileAttributes)(-1))
                throw new IOException(L.T("Les attributs de l’élément énuméré sont indisponibles.", "The listed item's attributes are unavailable."));
            var kind = (attributes & FileAttributes.Directory) != 0 ? EntryKind.Directory : EntryKind.File;
            var access = FileSafety.Access(info.FullName, attributes);
            long? size = null;
            if (kind == EntryKind.File && access != EntryAccess.Link)
                size = info is FileInfo file ? file.Length
                    : throw new IOException(L.T("Le type de l’élément énuméré est incohérent.", "The listed item has an inconsistent type."));
            return new Entry(relative, info.FullName, kind, size, info.LastWriteTimeUtc, info.CreationTimeUtc, attributes, null, access);
        }

        private static Entry ReadEntry(string fullPath, string relative)
        {
            // Exists suppresses network errors, which would turn a transient disconnection
            // into a misleading 'missing file'. The native query preserves the error code.
            var metadata = FileSafety.Stat(fullPath);
            var kind = (metadata.Attributes & FileAttributes.Directory) != 0 ? EntryKind.Directory : EntryKind.File;
            var access = FileSafety.Access(fullPath, metadata.Attributes);
            long? size = kind == EntryKind.File && access != EntryAccess.Link ? metadata.Size : null;
            return new Entry(relative, fullPath, kind, size, metadata.LastWriteUtc, metadata.CreationUtc, metadata.Attributes, null, access);
        }

        private void MarkDirectoryProblem(Snapshot snapshot, string fullPath, string reason)
        {
            var problem = L.F($"« {fullPath} » : {reason}", $"‘{fullPath}’: {reason}");
            _errors.Add(problem);
            // The listed name, as the inventory keyed it: GetRelativePath would strip a final dot or space and miss it.
            var relative = FileSafety.RelativeTo(snapshot.Root, fullPath);
            if (relative != "." && snapshot.Entries.TryGetValue(relative, out var entry))
                snapshot.Entries[relative] = entry with { Problem = problem };
            // An error held by no row would hold nothing back, and copies are allowed despite incidents: one whose folder
            // cannot be found counts against the whole root, which refuses any copy.
            else if (snapshot.RootEntry is not null) snapshot.RootEntry = snapshot.RootEntry with { Problem = problem };
        }

        private void BuildInitialRows(Snapshot sourceSnapshot, Snapshot targetSnapshot)
        {
            foreach (var relative in sourceSnapshot.Entries.Keys.Union(targetSnapshot.Entries.Keys, StringComparer.OrdinalIgnoreCase))
            {
                sourceSnapshot.Entries.TryGetValue(relative, out var first);
                targetSnapshot.Entries.TryGetValue(relative, out var second);
                var status = ComparisonStatus.Unverified;
                var detail = L.T("Comparaison du contenu non effectuée.", "Contents have not been compared.");
                if (first?.Problem is not null || second?.Problem is not null)
                    detail = first?.Problem ?? second!.Problem!;
                else if (first is null && AncestorProblem(sourceSnapshot, relative) is { } sourceProblem)
                    detail = sourceProblem;
                else if (second is null && AncestorProblem(targetSnapshot, relative) is { } targetProblem)
                    detail = targetProblem;
                else if (first?.Access == EntryAccess.Link || second?.Access == EntryAccess.Link)
                {
                    status = ComparisonStatus.Skipped;
                    detail = first?.Access == EntryAccess.Link && second?.Access == EntryAccess.Link
                        ? L.T("Lien symbolique ou jonction dans les deux dossiers : non suivi, son contenu n’est pas comparé.", "Symbolic link or junction in both folders: not followed, its contents are not compared.")
                        : first?.Access == EntryAccess.Link
                            ? L.T("Lien symbolique ou jonction dans le dossier 1 : non suivi, son contenu n’est pas comparé.", "Symbolic link or junction in folder 1: not followed, its contents are not compared.")
                            : L.T("Lien symbolique ou jonction dans le dossier 2 : non suivi, son contenu n’est pas comparé.", "Symbolic link or junction in folder 2: not followed, its contents are not compared.");
                }
                else if (first is null && UnderLink(sourceSnapshot, relative))
                {
                    status = ComparisonStatus.Skipped;
                    detail = L.T("Le dossier 1 a un lien non suivi à cet emplacement : ce chemin n’y est pas comparé.", "Folder 1 has an unfollowed link at this location: this path is not compared there.");
                }
                else if (second is null && UnderLink(targetSnapshot, relative))
                {
                    status = ComparisonStatus.Skipped;
                    detail = L.T("Le dossier 2 a un lien non suivi à cet emplacement : ce chemin n’y est pas comparé.", "Folder 2 has an unfollowed link at this location: this path is not compared there.");
                }
                else if (first is null)
                {
                    status = ComparisonStatus.OnlyTarget;
                    detail = L.T("Ce chemin existe uniquement dans le dossier 2.", "This path exists only in folder 2.");
                }
                else if (second is null)
                {
                    status = ComparisonStatus.OnlySource;
                    detail = L.T("Ce chemin manque dans le dossier 2.", "This path is missing from folder 2.");
                }
                else if (first.Kind != second.Kind)
                {
                    status = ComparisonStatus.Conflict;
                    detail = L.T("Conflit de type : un fichier et un dossier occupent le même chemin.", "Type conflict: a file and a folder occupy the same path.");
                }
                else if (first.Kind == EntryKind.Directory)
                {
                    status = ComparisonStatus.CommonDirectory;
                    detail = L.T("Dossier présent au même chemin des deux côtés.", "Folder present at the same path on both sides.");
                }
                else if (first.Size != second.Size)
                {
                    status = ComparisonStatus.Different;
                    detail = L.T("Même chemin mais tailles différentes : contenus différents.", "Same path but different sizes: contents differ.");
                }
                else if (first.Access == EntryAccess.OnlineOnly || second.Access == EntryAccess.OnlineOnly)
                {
                    // Equal sizes prove nothing, and reading would download the file: report the metadata only.
                    detail = L.T("Fichier disponible uniquement en ligne (OneDrive ou autre service) : non téléchargé, contenu non vérifié. Même taille", "Online-only file (OneDrive or another service): not downloaded, contents not verified. Same size")
                        + (first.LastWriteUtc == second.LastWriteUtc ? L.T(" et même date de modification.", " and modification date.") : L.T(", date de modification différente.", ", different modification date."));
                }
                // Still a real file, reported as such, but a copy never carries it over to the other folder.
                if (status is ComparisonStatus.OnlySource or ComparisonStatus.OnlyTarget && (first ?? second)!.Kind == EntryKind.File &&
                    CopyEngine.IsCopyTemporary(Path.GetFileName(relative)))
                    detail += L.T(" Fichier temporaire d’une copie Checker San interrompue : il n’est jamais recopié.", " Temporary file of an interrupted Checker San copy: it is never copied over.");
                _rows[relative] = new ComparisonRow
                {
                    Kind = first?.Kind ?? second!.Kind,
                    Status = status,
                    RelativePath = first?.RelativePath ?? second!.RelativePath,
                    SourcePath = first?.FullPath,
                    TargetPath = second?.FullPath,
                    SourceSize = first?.Size,
                    TargetSize = second?.Size,
                    Detail = detail,
                    OnlineOnly = first?.Access == EntryAccess.OnlineOnly || second?.Access == EntryAccess.OnlineOnly
                };
            }
            if (RootProblem(sourceSnapshot) is { } firstProblem)
                AddRootProblemRow(sourceSnapshot.Root, true, firstProblem);
            if (RootProblem(targetSnapshot) is { } secondProblem)
                AddRootProblemRow(targetSnapshot.Root, false, secondProblem);
        }

        private static string? RootProblem(Snapshot snapshot) => snapshot.RootEntry is null
            ? L.F($"Racine non entièrement accessible : « {snapshot.Root} ».", $"Root not fully accessible: ‘{snapshot.Root}’.")
            : snapshot.RootEntry.Problem;

        private static bool UnderLink(Snapshot snapshot, string relative)
        {
            for (var parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                if (snapshot.Entries.TryGetValue(parent, out var entry) && entry.Access == EntryAccess.Link)
                    return true;
            return false;
        }

        private static string? AncestorProblem(Snapshot snapshot, string relative)
        {
            if (RootProblem(snapshot) is { } rootProblem) return rootProblem;
            for (var parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                if (snapshot.Entries.TryGetValue(parent, out var entry) && entry.Problem is not null)
                    return entry.Problem;
            return null;
        }

        private void AddRootProblemRow(string root, bool isSource, string problem)
        {
            if (!_rows.TryGetValue(".", out var row))
                row = new ComparisonRow { Kind = EntryKind.Directory, RelativePath = "." };
            _rows["."] = row with
            {
                Status = ComparisonStatus.Unverified,
                SourcePath = isSource ? root : row.SourcePath,
                TargetPath = isSource ? row.TargetPath : root,
                Detail = string.IsNullOrEmpty(row.Detail) ? problem : row.Detail + " " + problem
            };
        }

        private void PlanHashBytes(Snapshot sourceSnapshot, Snapshot targetSnapshot)
        {
            var planned = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (options.FindMovedFiles)
            {
                var sourceFiles = sourceSnapshot.Entries.Values.Where(Readable).ToArray();
                var targetFiles = targetSnapshot.Entries.Values.Where(Readable).ToArray();
                var commonSizes = sourceFiles.Select(item => item.Size!.Value).Intersect(targetFiles.Select(item => item.Size!.Value)).ToHashSet();
                foreach (var item in sourceFiles.Concat(targetFiles).Where(item => commonSizes.Contains(item.Size!.Value)))
                    planned[item.FullPath] = item;
            }
            else
            {
                foreach (var item in sourceSnapshot.Entries.Values)
                {
                    if (!Readable(item) || !targetSnapshot.Entries.TryGetValue(item.RelativePath, out var other) ||
                        !Readable(other) || item.Size != other.Size)
                        continue;
                    planned[item.FullPath] = item;
                    planned[other.FullPath] = other;
                }
            }
            // This is an upper bound: finding a matching digest can avoid reading later candidates.
            foreach (var item in planned.Values)
                _totalHashBytes = item.Size!.Value > long.MaxValue - _totalHashBytes ? long.MaxValue : _totalHashBytes + item.Size.Value;
        }

        private async Task FindMovedFilesAsync(Snapshot sourceSnapshot, Snapshot targetSnapshot)
        {
            SetPhase(L.T("Recherche des fichiers déplacés ou renommés", "Looking for moved or renamed files"));
            // A temporary, a quarantined duplicate or a recycled file is never the copy a source file is found as.
            var candidatesBySize = targetSnapshot.Entries.Values
                .Where(item => Readable(item) && !SystemFolders.NeverEvidence(item.RelativePath))
                .GroupBy(item => item.Size!.Value)
                .ToDictionary(group => group.Key, group => new CandidateBucket(
                    group.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray()));

            // Index already computed target digests once. Each remaining target is examined
            // at most once per bucket, instead of walking every candidate for every source.
            foreach (var bucket in candidatesBySize.Values)
                foreach (var candidate in bucket.Entries)
                    if (_hashes.TryGetValue(candidate.FullPath, out var digest) && digest is not null)
                        bucket.ByHash.TryAdd(digest, candidate);

            foreach (var item in sourceSnapshot.Entries.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Readable(item) || _coveredSourceFiles.Contains(item.RelativePath) ||
                    !candidatesBySize.TryGetValue(item.Size!.Value, out var sizeBucket))
                    continue;

                _hashes.TryGetValue(item.FullPath, out var sourceHash);
                Entry? match = null;
                if (sourceHash is not null) sizeBucket.ByHash.TryGetValue(sourceHash, out match);
                if (match is null)
                {
                    var candidateBucket = sizeBucket;
                    if (ShouldSample(item))
                    {
                        var sample = await SampleAsync(item);
                        if (sample is null) continue;
                        if (sizeBucket.BySample is null)
                        {
                            var groups = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
                            foreach (var candidate in sizeBucket.Entries)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var signature = await SampleAsync(candidate);
                                if (signature is null) continue;
                                if (!groups.TryGetValue(signature, out var group)) groups[signature] = group = [];
                                group.Add(candidate);
                            }
                            sizeBucket.BySample = groups.ToDictionary(pair => pair.Key,
                                pair => new CandidateBucket(pair.Value.ToArray()), StringComparer.Ordinal);
                        }
                        if (!sizeBucket.BySample.TryGetValue(sample, out candidateBucket)) continue;
                    }
                    sourceHash ??= await HashAsync(item);
                    if (sourceHash is null) continue;
                    match = await FindHashMatchAsync(sourceHash, candidateBucket);
                }
                if (sourceHash is null) continue;
                _rows[item.RelativePath] = _rows[item.RelativePath] with { SourceHash = sourceHash };
                if (match is null) continue;
                _coveredSourceFiles.Add(item.RelativePath);
                var row = _rows[item.RelativePath];
                if (row.Status == ComparisonStatus.OnlySource)
                {
                    _rows[item.RelativePath] = row with
                    {
                        Status = ComparisonStatus.Moved,
                        TargetPath = match.FullPath,
                        TargetSize = match.Size,
                        TargetHash = sourceHash,
                        Detail = L.F($"Même contenu SHA-256 trouvé ailleurs dans le dossier 2 : {match.RelativePath}", $"The same SHA-256 contents were found elsewhere in folder 2: {match.RelativePath}")
                    };
                    Reference(match.FullPath, item.RelativePath);
                }
                else
                {
                    _rows[item.RelativePath] = row with
                    {
                        Detail = row.Detail + L.F($" Une copie du contenu source existe aussi dans le dossier 2 : {match.RelativePath}", $" A copy of the source contents also exists in folder 2: {match.RelativePath}")
                    };
                }
            }
        }

        /// <summary>
        /// The other way round: a file only in folder 2 whose contents lie elsewhere in folder 1 keeps its status, and its
        /// row names that copy. Gathering into folder 1 then leaves it out. Every source file sharing a
        /// size with a target file was hashed by the search above, unless its sample matched none of them; so only targets
        /// of the same size and sample as a hashed source file, and not yet read, are hashed here.
        /// </summary>
        private async Task FindTargetCopiesAsync(Snapshot sourceSnapshot, Snapshot targetSnapshot)
        {
            var bySize = new Dictionary<long, List<Entry>>();
            var byHash = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var item in sourceSnapshot.Entries.Values.Where(item => Readable(item) && !SystemFolders.NeverEvidence(item.RelativePath))
                         .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                if (!_hashes.TryGetValue(item.FullPath, out var digest) || digest is null) continue;
                byHash.TryAdd(digest, item);
                if (!bySize.TryGetValue(item.Size!.Value, out var group)) bySize[item.Size.Value] = group = [];
                group.Add(item);
            }
            foreach (var item in targetSnapshot.Entries.Values.Where(Readable))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_rows.TryGetValue(item.RelativePath, out var row) || row.Status != ComparisonStatus.OnlyTarget ||
                    !bySize.TryGetValue(item.Size!.Value, out var sameSize))
                    continue;
                if (!_hashes.TryGetValue(item.FullPath, out var digest))
                {
                    if (ShouldSample(item))
                    {
                        var sample = await SampleAsync(item);
                        if (sample is null) continue;
                        bool Possible(Entry source) { lock (_gate) return !_samples.TryGetValue(source.FullPath, out var known) || known is null || known == sample; }
                        if (!sameSize.Any(Possible)) continue;
                    }
                    digest = await HashAsync(item);
                }
                if (digest is null || !byHash.TryGetValue(digest, out var copy)) continue;
                _rows[item.RelativePath] = row with
                {
                    SourceCopyPath = copy.FullPath,
                    TargetHash = digest,
                    Detail = row.Detail + L.F($" Même contenu SHA-256 présent ailleurs dans le dossier 1 : {copy.RelativePath}", $" The same SHA-256 contents are elsewhere in folder 1: {copy.RelativePath}")
                };
                Reference(copy.FullPath, item.RelativePath);
            }
        }

        private async Task<Entry?> FindHashMatchAsync(string sourceHash, CandidateBucket bucket)
        {
            if (bucket.ByHash.TryGetValue(sourceHash, out var match)) return match;
            while (bucket.NextIndex < bucket.Entries.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = bucket.Entries[bucket.NextIndex++];
                var targetHash = await HashAsync(candidate);
                if (targetHash is null) continue;
                bucket.ByHash.TryAdd(targetHash, candidate);
                if (targetHash == sourceHash) return candidate;
            }
            return null;
        }

        private sealed class CandidateBucket(Entry[] entries)
        {
            public Entry[] Entries { get; } = entries;
            public int NextIndex { get; set; }
            public Dictionary<string, Entry> ByHash { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, CandidateBucket>? BySample { get; set; }
        }

        private bool ShouldSample(Entry entry) => options.UseSamplePrefilter && entry.Size >= SampleThreshold;

        private async Task<string?> SampleAsync(Entry entry)
        {
            lock (_gate)
                if (_samples.TryGetValue(entry.FullPath, out var cached)) return cached;
            try { return await RetryIoAsync(() => ReadSample(entry), entry.FullPath); }
            catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
            {
                var reason = L.F($"Impossible de présélectionner « {entry.FullPath} » : {exception.Message}", $"Unable to filter candidate ‘{entry.FullPath}’: {exception.Message}");
                lock (_gate)
                {
                    _errors.Add(reason);
                    _samples[entry.FullPath] = null;
                    _checkpointFiles.Remove(entry.FullPath);
                }
                MarkUnverified(entry, reason);
                return null;
            }
        }

        // Synchronous reads on this worker thread: on an NVMe drive, asynchronous FileStream reads hashed
        // about 600 MiB/s against 1.2 to 1.4 GiB/s synchronously, as cached read-ahead overlaps the hashing.
        private string ReadSample(Entry entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _currentPath = entry.FullPath;
            ReportProgress(true);
            if (!SameMetadata(entry, ReadEntry(entry.FullPath, entry.RelativePath)))
                throw new IOException(L.T("Le fichier a changé depuis l’inventaire.", "The file has changed since it was listed."));

            // No content verdict can be positive from these samples. They only reject
            // candidates; equal signatures always proceed to a complete SHA-256 read.
            // Disable FileStream buffering so each seek reads exactly its small block.
            using var stream = new FileStream(FileSafety.Native(entry.FullPath), FileMode.Open, FileAccess.Read, FileShare.Read,
                1, FileOptions.RandomAccess);
            var opened = FileSafety.Describe(stream.SafeFileHandle);
            if (stream.Length != entry.Size || opened.LastWriteUtc != entry.LastWriteUtc || opened.CreationUtc != entry.CreationUtc)
                throw new IOException(L.T("Les métadonnées du fichier ouvert ont changé depuis l’inventaire.", "The opened file's metadata has changed since it was listed."));
            var identity = opened.Identity;
            var buffer = ArrayPool<byte>.Shared.Rent(SampleBlockSize);
            try
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long[] offsets = [0, (entry.Size!.Value - SampleBlockSize) / 2, entry.Size.Value - SampleBlockSize];
                foreach (var offset in offsets)
                {
                    stream.Position = offset;
                    var total = 0;
                    while (total < SampleBlockSize)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var read = stream.Read(buffer, total, SampleBlockSize - total);
                        if (read == 0) throw new IOException(L.T("Lecture incomplète pendant la présélection.", "Incomplete read during candidate filtering."));
                        total += read;
                        Interlocked.Add(ref _sampleBytesRead, read);
                        ReportProgress();
                    }
                    hasher.AppendData(buffer, 0, total);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var closing = FileSafety.Describe(stream.SafeFileHandle);
                if (stream.Length != entry.Size || closing.LastWriteUtc != entry.LastWriteUtc || closing.CreationUtc != entry.CreationUtc)
                    throw new IOException(L.T("Le fichier a changé pendant la présélection.", "The file changed during candidate filtering."));
                if (!SameMetadata(entry, ReadEntry(entry.FullPath, entry.RelativePath)) ||
                    (identity is not null && identity != closing.Identity))
                    throw new IOException(L.T("L’identité ou les métadonnées du fichier ont changé pendant la présélection.", "The file's identity or metadata changed during candidate filtering."));
                var signature = Convert.ToHexString(hasher.GetHashAndReset());
                lock (_gate) _samples[entry.FullPath] = signature;
                return signature;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private async Task<string?> HashAsync(Entry entry)
        {
            try { return await RetryIoAsync(() => ReadHash(entry), entry.FullPath); }
            catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
            {
                var reason = L.F($"Impossible de vérifier « {entry.FullPath} » : {exception.Message}", $"Unable to verify ‘{entry.FullPath}’: {exception.Message}");
                lock (_gate) { _errors.Add(reason); _hashes[entry.FullPath] = null; _checkpointFiles.Remove(entry.FullPath); }
                MarkUnverified(entry, reason);
                return null;
            }
        }

        private string? ReadHash(Entry entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
                if (_hashes.TryGetValue(entry.FullPath, out var cached)) return cached;
            _currentPath = entry.FullPath;
            ReportProgress(true);
            byte[]? buffer = null;
            try
            {
                if (!SameMetadata(entry, ReadEntry(entry.FullPath, entry.RelativePath)))
                    throw new IOException(L.T("Le fichier a changé depuis l’inventaire.", "The file has changed since it was listed."));

                // Deny concurrent write/delete handles while bytes are read. An existing
                // incompatible writer causes a reported sharing violation, never a verdict.
                using var stream = new FileStream(FileSafety.Native(entry.FullPath), FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.SequentialScan);
                var opened = FileSafety.Describe(stream.SafeFileHandle);
                if (stream.Length != entry.Size || opened.LastWriteUtc != entry.LastWriteUtc || opened.CreationUtc != entry.CreationUtc)
                    throw new IOException(L.T("Les métadonnées du fichier ouvert ont changé depuis l’inventaire.", "The opened file's metadata has changed since it was listed."));

                var identity = opened.Identity;
                lock (_gate)
                {
                    if (_checkpointFiles.TryGetValue(entry.FullPath, out var saved) && identity is not null && saved.Identity == identity &&
                        saved.Size == entry.Size && saved.LastWriteUtc == entry.LastWriteUtc && saved.CreationUtc == entry.CreationUtc && saved.Attributes == entry.Attributes)
                    {
                        _hashes[entry.FullPath] = saved.Hash;
                        _readIdentities[entry.FullPath] = (entry, identity);
                        _filesReused++;
                        _totalHashBytes = Math.Max(0, _totalHashBytes - (entry.Size ?? 0));
                        ReportProgress(true);
                        return saved.Hash;
                    }
                    _checkpointFiles.Remove(entry.FullPath);
                }

                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
                long readForFile = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hasher.AppendData(buffer, 0, read);
                    readForFile += read;
                    Interlocked.Add(ref _bytesHashed, read);
                    ReportProgress();
                }
                cancellationToken.ThrowIfCancellationRequested();
                var closing = FileSafety.Describe(stream.SafeFileHandle);
                if (readForFile != entry.Size || stream.Length != entry.Size ||
                    closing.LastWriteUtc != entry.LastWriteUtc || closing.CreationUtc != entry.CreationUtc)
                    throw new IOException(L.T("Le fichier a changé pendant le calcul SHA-256.", "The file changed during SHA-256 hashing."));
                if (!SameMetadata(entry, ReadEntry(entry.FullPath, entry.RelativePath)))
                    throw new IOException(L.T("Le chemin ou les métadonnées du fichier ont changé pendant sa lecture.", "The file's path or metadata changed while it was being read."));
                if (identity is not null && identity != closing.Identity)
                    throw new IOException(L.T("L’identité ou la date de changement du fichier a changé pendant sa lecture.", "The file's identity or change time changed while it was being read."));

                var hash = Convert.ToHexString(hasher.GetHashAndReset());
                lock (_gate)
                {
                    _hashes[entry.FullPath] = hash;
                    if (closing.Identity is not null) _readIdentities[entry.FullPath] = (entry, closing.Identity);
                    _filesHashed++;
                    _checkpointFiles[entry.FullPath] = new CheckpointFile { FullPath = entry.FullPath, Size = entry.Size!.Value, LastWriteUtc = entry.LastWriteUtc, CreationUtc = entry.CreationUtc, Attributes = entry.Attributes, Hash = hash, Identity = identity };
                    _checkpointDirty = true;
                }
                SaveCheckpoint();
                ReportProgress(true);
                return hash;
            }
            finally
            {
                if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// Paths are paired ignoring case, as Windows does. Where the two names differ by case the row says so, and in
        /// a case-sensitive folder (WSL, per-folder setting) they are two different names, never identical.
        /// </summary>
        private void MarkCaseDifferences(Snapshot source, Snapshot target)
        {
            foreach (var (relative, first) in source.Entries)
            {
                if (!target.Entries.TryGetValue(relative, out var second) || string.Equals(first.RelativePath, second.RelativePath, StringComparison.Ordinal) ||
                    !_rows.TryGetValue(relative, out var row)) continue;
                var note = L.F($" Noms écrits avec une casse différente : « {first.RelativePath} » et « {second.RelativePath} ».", $" Names written with a different case: ‘{first.RelativePath}’ and ‘{second.RelativePath}’.");
                var sensitive = FileSafety.CaseSensitive(Path.GetDirectoryName(first.FullPath)!) || FileSafety.CaseSensitive(Path.GetDirectoryName(second.FullPath)!);
                _rows[relative] = sensitive
                    ? row with { Status = ComparisonStatus.Conflict, Detail = L.T("Dossier sensible à la casse : ce ne sont pas les mêmes noms.", "Case-sensitive folder: these are not the same names.") + note }
                    : row with { Detail = row.Detail + note };
            }
        }

        /// <summary>
        /// The last listing compares directory entries, which miss an in-place write that keeps size and date,
        /// or any write through another hard link. Each file behind an Identical or Moved row, or behind a copy found
        /// in folder 1 for a file only in folder 2, is therefore queried by handle: a new change time or identity
        /// means its hash no longer proves anything.
        /// </summary>
        private async Task<bool> VerifyReadIdentitiesAsync()
        {
            List<(string Path, Entry Entry, FileIdentity Identity)> proven;
            lock (_gate)
                proven = _rows.Values.Where(row => row.Status is ComparisonStatus.Identical or ComparisonStatus.Moved || row.SourceCopyPath is not null)
                    .SelectMany(row => new[] { row.SourcePath, row.TargetPath, row.SourceCopyPath }).OfType<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(_readIdentities.ContainsKey)
                    .Select(path => (path, _readIdentities[path].Entry, _readIdentities[path].Identity)).ToList();
            static FileIdentity? Query(string path)
            {
                try { return FileSafety.CurrentIdentity(path); }
                catch (Exception exception) when (IsFileSystemException(exception)) { return null; }
            }
            // About 70 µs per file on NVMe: queried with the same per-disk readers as the hashes.
            var changed = 0;
            await Parallel.ForEachAsync(proven, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _readers?.Slots ?? 1), CancellationToken = cancellationToken }, async (item, token) =>
            {
                var now = _readers is { } readers ? await readers.Run(item.Path, () => Task.FromResult(Query(item.Path)), token) : Query(item.Path);
                if (now == item.Identity) return;
                Interlocked.Increment(ref changed);
                var problem = L.F($"Fichier modifié pendant la comparaison, après sa lecture : « {item.Path} ». Relancez l’analyse.", $"File changed during the comparison, after it was read: ‘{item.Path}’. Scan again.");
                lock (_gate) _errors.Add(problem);
                MarkUnverified(item.Entry, problem);
            });
            return changed == 0;
        }

        private bool VerifySnapshot(Snapshot original, Snapshot final, bool isSource)
        {
            var stable = true;
            if (original.RootEntry is null || final.RootEntry is null ||
                !SameMetadata(original.RootEntry, final.RootEntry) ||
                !string.Equals(original.CanonicalRoot, final.CanonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                stable = false;
                var problem = L.F($"La racine « {original.Root} » a changé ou n’est plus entièrement accessible pendant la comparaison.", $"Root ‘{original.Root}’ changed or became partly inaccessible during the comparison.");
                _errors.Add(problem);
                AddRootProblemRow(original.Root, isSource, problem);
            }
            foreach (var relative in original.Entries.Keys.Union(final.Entries.Keys, StringComparer.OrdinalIgnoreCase))
            {
                original.Entries.TryGetValue(relative, out var before);
                final.Entries.TryGetValue(relative, out var after);
                if (before is not null && after is not null && SameMetadata(before, after)) continue;
                stable = false;
                var entry = after ?? before!;
                var problem = L.F($"Élément ajouté, supprimé ou modifié pendant la comparaison : « {entry.FullPath} ». Relancez l’analyse.", $"Item added, removed or changed during the comparison: ‘{entry.FullPath}’. Scan again.");
                _errors.Add(problem);
                if (!_rows.TryGetValue(relative, out var existingRow))
                {
                    _rows[relative] = new ComparisonRow
                    {
                        RelativePath = entry.RelativePath,
                        Kind = entry.Kind,
                        Status = ComparisonStatus.Unverified,
                        SourcePath = isSource ? entry.FullPath : null,
                        TargetPath = isSource ? null : entry.FullPath,
                        SourceSize = isSource ? entry.Size : null,
                        TargetSize = isSource ? null : entry.Size,
                        Detail = problem
                    };
                }
                else
                {
                    // A newly added counterpart was absent from the original row, so a
                    // full-path lookup alone would fail to invalidate that row.
                    _rows[relative] = existingRow with
                    {
                        Status = ComparisonStatus.Unverified,
                        SourcePath = isSource ? entry.FullPath : existingRow.SourcePath,
                        TargetPath = isSource ? existingRow.TargetPath : entry.FullPath,
                        SourceSize = isSource ? entry.Size : existingRow.SourceSize,
                        TargetSize = isSource ? existingRow.TargetSize : entry.Size,
                        Detail = problem
                    };
                }
                MarkUnverified(entry, problem);
            }
            return stable;
        }

        /// <summary>Records a row naming a file at another path than its own, so an incident on that file finds the row without scanning them all.</summary>
        private void Reference(string fullPath, string rowKey)
        {
            lock (_gate)
            {
                if (!_referencedBy.TryGetValue(fullPath, out var keys)) _referencedBy[fullPath] = keys = [];
                keys.Add(rowKey);
            }
        }

        private void MarkUnverified(Entry entry, string reason)
        {
            bool Same(string? path) => string.Equals(path, entry.FullPath, StringComparison.OrdinalIgnoreCase);
            lock (_gate)
            {
                // A row names a file at its own path, or at another one recorded by Reference.
                var keys = _referencedBy.TryGetValue(entry.FullPath, out var others) ? others.Prepend(entry.RelativePath).ToArray() : [entry.RelativePath];
                foreach (var key in keys.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!_rows.TryGetValue(key, out var row)) continue;
                    if (Same(row.SourcePath) || Same(row.TargetPath))
                        _rows[key] = row with { Status = ComparisonStatus.Unverified, Detail = reason };
                    // The file of folder 2 is intact: only the copy that kept it out of a gathering into folder 1 no
                    // longer proves anything, so it is missing from folder 1 again, and will be copied.
                    else if (Same(row.SourceCopyPath))
                        _rows[key] = row with { SourceCopyPath = null, Detail = row.Detail + L.T(" L’exemplaire trouvé dans le dossier 1 a changé pendant la comparaison : il ne compte plus.", " The copy found in folder 1 changed during the comparison: it no longer counts.") };
                }
            }
        }

        private void SetPhase(string phase)
        {
            _phase = phase;
            _currentPath = "";
            ReportProgress(true);
        }

        private void ReportProgress(bool force = false)
        {
            if (progress is null) return;
            lock (_gate)
            {
                if (!force && _progressTimer.ElapsedMilliseconds < 80) return;
                _progressTimer.Restart();
                progress.Report(new ComparisonProgress
                {
                    Phase = _phase,
                    CurrentPath = _currentPath,
                    FilesDiscovered = _filesDiscovered,
                    FilesHashed = _filesHashed,
                    BytesHashed = Interlocked.Read(ref _bytesHashed),
                    SampleBytesRead = Interlocked.Read(ref _sampleBytesRead),
                    TotalHashBytes = _totalHashBytes,
                    FilesReused = _filesReused,
                    RetryAttempt = _retryAttempt
                });
            }
        }
    }

    private sealed class Snapshot(string root)
    {
        public string Root { get; } = root;
        public string? CanonicalRoot { get; set; }
        public Entry? RootEntry { get; set; }
        public Dictionary<string, Entry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Entry(string RelativePath, string FullPath, EntryKind Kind, long? Size,
        DateTime LastWriteUtc, DateTime CreationUtc, FileAttributes Attributes, string? Problem, EntryAccess Access = EntryAccess.Normal);

    /// <summary>A local file whose contents may be read: not a link, not online-only, without listing problem.</summary>
    private static bool Readable(Entry entry) =>
        entry.Kind == EntryKind.File && entry.Problem is null && entry.Access == EntryAccess.Normal && entry.Size.HasValue;

    private static bool SameMetadata(Entry first, Entry second) =>
        first.Kind == second.Kind && first.Size == second.Size && first.LastWriteUtc == second.LastWriteUtc &&
        first.CreationUtc == second.CreationUtc && first.Attributes == second.Attributes && first.Problem == second.Problem &&
        first.Access == second.Access;

    private static bool IsFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or Win32Exception;

    private static bool IsWithin(string root, string path) => FileSafety.IsWithin(root, path);

    private static string NormalizeRoot(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(L.F($"Choisissez le {label}.", $"Choose {label}."), nameof(path));
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) == 0) throw new ArgumentException(L.F($"Le {label} n’est pas un dossier : {fullPath}", $"The path for {label} is not a folder: {fullPath}"), nameof(path));
            if (FileSafety.Access(fullPath, attributes) == EntryAccess.Link)
                throw new ArgumentException(L.F($"Le {label} est un lien ou une jonction. Sélectionnez son dossier réel.", $"The path for {label} is a link or junction. Select its physical folder."), nameof(path));
            return fullPath;
        }
        catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
        {
            throw new ArgumentException(L.F($"Impossible d’ouvrir le {label} : {exception.Message}", $"Unable to open {label}: {exception.Message}"), nameof(path), exception);
        }
    }

    private static void ValidateSeparateRoots(string sourceRoot, string targetRoot)
    {
        try
        {
            if (RelatedRoots(sourceRoot, targetRoot) || RelatedRoots(CanonicalRoot(sourceRoot), CanonicalRoot(targetRoot)) ||
                RelatedByIdentity(sourceRoot, targetRoot))
                throw new ArgumentException(L.T("Choisissez deux dossiers distincts : aucun ne doit être contenu dans l’autre.", "Choose two separate folders: neither may be inside the other."));
        }
        catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
        {
            throw new ArgumentException(L.F($"Impossible de confirmer les chemins réels des dossiers : {exception.Message}", $"Unable to confirm the physical folder paths: {exception.Message}"), exception);
        }
    }

    /// <summary>
    /// Canonical paths keep the server name, so \\NAS\Photos and \\192.168.1.10\Photos look unrelated. The
    /// same folder, or one inside the other, is recognized by its volume and file identifier instead.
    /// </summary>
    private static bool RelatedByIdentity(string first, string second)
    {
        var a = FileSafety.ObjectId(first);
        var b = FileSafety.ObjectId(second);
        if (a is null || b is null) return false;
        return a == b || HasAncestor(first, b.Value) || HasAncestor(second, a.Value);

        static bool HasAncestor(string path, (ulong, ulong, ulong) id)
        {
            for (var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)); parent is not null; parent = Path.GetDirectoryName(parent))
            {
                // An ancestor without list rights, such as a share root, ends the walk: nothing above it is compared.
                try { if (FileSafety.ObjectId(parent) == id) return true; }
                catch (Win32Exception) { return false; }
            }
            return false;
        }
    }

    private static bool RelatedRoots(string first, string second)
    {
        first = Path.TrimEndingDirectorySeparator(first);
        second = Path.TrimEndingDirectorySeparator(second);
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase) ||
            first.StartsWith(Path.EndsInDirectorySeparator(second) ? second : second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            second.StartsWith(Path.EndsInDirectorySeparator(first) ? first : first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalRoot(string path)
    {
        if (!OperatingSystem.IsWindows()) return Path.GetFullPath(path);
        using var handle = CreateFileW(FileSafety.Native(path), 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            if (GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        var resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + resolved[8..];
        return resolved.StartsWith(@"\\?\", StringComparison.Ordinal) ? resolved[4..] : resolved;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint access, FileShare share,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint capacity, uint flags);
}
