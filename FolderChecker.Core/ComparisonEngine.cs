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
        private readonly HashSet<string> _errors = new(StringComparer.Ordinal);
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CheckpointFile> _checkpointFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _progressTimer = Stopwatch.StartNew();
        private readonly Stopwatch _checkpointTimer = Stopwatch.StartNew();
        private long _filesDiscovered, _filesHashed, _bytesHashed, _sampleBytesRead, _totalHashBytes, _filesReused;
        private const long SampleThreshold = 4 * 1024 * 1024;
        private const int SampleBlockSize = 64 * 1024;
        private int _retryAttempt;
        private string? _checkpointPath;
        private string _sourceRoot = "", _targetRoot = "", _sourceCanonical = "", _targetCanonical = "";
        private bool _hasCheckpoint, _checkpointSaved, _checkpointDirty;
        private string _phase = "Préparation";
        private string _currentPath = "";

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

            try
            {
                sourceRoot = await RetryIoAsync(() => NormalizeRoot(source, "dossier 1"), source);
                targetRoot = await RetryIoAsync(() => NormalizeRoot(target, "dossier 2"), target);
                sourceSnapshot = new Snapshot(sourceRoot);
                targetSnapshot = new Snapshot(targetRoot);
                await RetryIoAsync(() => { ValidateSeparateRoots(sourceRoot, targetRoot); return true; }, sourceRoot);
                await PrepareCheckpointAsync(sourceRoot, targetRoot);
                SetPhase("Inventaire du dossier 1");
                await ScanAsync(sourceSnapshot, true);
                SetPhase("Inventaire du dossier 2");
                await ScanAsync(targetSnapshot, true);
                scansComplete = true;
                BuildInitialRows(sourceSnapshot, targetSnapshot);
                PlanHashBytes(sourceSnapshot, targetSnapshot);
                SetPhase("Vérification SHA-256 des fichiers correspondants");

                foreach (var item in sourceSnapshot.Entries.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.Kind != EntryKind.File || item.Problem is not null ||
                        !targetSnapshot.Entries.TryGetValue(item.RelativePath, out var other) ||
                        other.Kind != EntryKind.File || other.Problem is not null || item.Size != other.Size)
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
                                Detail = "Présélection impossible : consultez les erreurs."
                            };
                            continue;
                        }
                        if (sourceSample != targetSample)
                        {
                            _rows[item.RelativePath] = _rows[item.RelativePath] with
                            {
                                Status = ComparisonStatus.Different,
                                Detail = "Même chemin et même taille, mais des octets diffèrent dans les échantillons lus."
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
                        Detail = !valid ? "Vérification impossible : consultez les erreurs." : identical
                            ? "Même chemin et contenu identique (SHA-256 complet)."
                            : "Même chemin et même taille, mais contenu différent (SHA-256).",
                        SourceHash = sourceHash,
                        TargetHash = targetHash
                    };
                    if (identical) _coveredSourceFiles.Add(item.RelativePath);
                }

                if (options.FindMovedFiles)
                    await FindMovedFilesAsync(sourceSnapshot, targetSnapshot);

                cancellationToken.ThrowIfCancellationRequested();
                SetPhase("Contrôle final des modifications");
                var finalSource = new Snapshot(sourceRoot);
                var finalTarget = new Snapshot(targetRoot);
                await ScanAsync(finalSource, false);
                await ScanAsync(finalTarget, false);
                var sourceStable = VerifySnapshot(sourceSnapshot, finalSource, true);
                var targetStable = VerifySnapshot(targetSnapshot, finalTarget, false);
                snapshotVerified = sourceStable && targetStable && _errors.Count == 0;
                cancellationToken.ThrowIfCancellationRequested();
                SetPhase("Comparaison terminée");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                if (!scansComplete)
                {
                    BuildInitialRows(sourceSnapshot, targetSnapshot);
                    foreach (var key in _rows.Keys.ToArray())
                        _rows[key] = _rows[key] with { Status = ComparisonStatus.Unverified, Detail = "Inventaire interrompu : présence non confirmée." };
                }
                SetPhase("Comparaison annulée");
            }
            catch (Exception exception) when (FileSystemRetryPolicy.IsTransient(exception))
            {
                var problem = $"Connexion indisponible après {options.MaxRetries} nouvelle(s) tentative(s) : {exception.Message} Reprenez la comparaison lorsque le dossier est de nouveau accessible.";
                _errors.Add(problem);
                if (!scansComplete) BuildInitialRows(sourceSnapshot, targetSnapshot);
                foreach (var key in _rows.Keys.ToArray())
                    if (!scansComplete || _rows[key].Status == ComparisonStatus.Unverified)
                        _rows[key] = _rows[key] with { Status = ComparisonStatus.Unverified, Detail = problem };
                SetPhase("Connexion interrompue — reprise disponible après reconnexion");
            }
            finally
            {
                _checkpointSaved = SaveCheckpoint(true);
            }

            var orderedRows = _rows.Values.OrderBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            var certain = !cancelled && snapshotVerified && _errors.Count == 0;
            var sourceContained = certain && IsStrictlyContained(sourceSnapshot);
            var targetContained = certain && IsStrictlyContained(targetSnapshot);
            var sourceFileCount = sourceSnapshot.Entries.Values.LongCount(item => item.Kind == EntryKind.File);
            return new ComparisonResult
            {
                SourceRoot = sourceRoot,
                TargetRoot = targetRoot,
                Rows = orderedRows,
                Errors = _errors.ToArray(),
                StartedAt = started,
                CompletedAt = DateTimeOffset.Now,
                Cancelled = cancelled,
                SnapshotVerified = snapshotVerified && !cancelled,
                SourceFullyContained = sourceContained,
                TargetFullyContained = targetContained,
                TreesIdentical = sourceContained && targetContained,
                ContentCovered = certain && _coveredSourceFiles.Count == sourceFileCount,
                SourceFileCount = sourceFileCount,
                TargetFileCount = targetSnapshot.Entries.Values.LongCount(item => item.Kind == EntryKind.File),
                FilesVerified = _filesHashed,
                BytesRead = _bytesHashed,
                SampleBytesRead = _sampleBytesRead,
                FilesReused = _filesReused,
                HasCheckpoint = _hasCheckpoint,
                CheckpointSaved = _checkpointSaved,
                CheckpointPath = _checkpointPath,
                Warnings = _warnings.ToArray()
            };
        }

        private bool IsStrictlyContained(Snapshot snapshot) =>
            // Moved rows can point at a target file under another relative path. Inventory keys
            // preserve which entries actually belong to each tree and require a same-path match.
            snapshot.Entries.Keys.All(relative => _rows.TryGetValue(relative, out var row) &&
                row.Status is ComparisonStatus.Identical or ComparisonStatus.CommonDirectory);

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
                        _phase = $"Connexion interrompue — tentative {attempt}/{options.MaxRetries} dans {delay.TotalSeconds:0.#} s";
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
                _warnings.Add($"La session de reprise ne peut pas être enregistrée : {exception.Message}");
                return;
            }
            _checkpointPath = path;
            if (!options.ResumeFromCheckpoint) return;
            var data = CheckpointData.Read(path);
            if (data is null)
            {
                if (File.Exists(path)) _warnings.Add("Le fichier de reprise est illisible ou invalide. Les fichiers seront vérifiés à nouveau.");
                return;
            }
            if (!string.Equals(data.SourceRoot, sourceRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(data.TargetRoot, targetRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(data.SourceCanonicalRoot, _sourceCanonical, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(data.TargetCanonicalRoot, _targetCanonical, StringComparison.OrdinalIgnoreCase) ||
                data.FindMovedFiles != options.FindMovedFiles)
            {
                _warnings.Add("La session enregistrée concerne d’autres dossiers ou options. Une nouvelle comparaison a été démarrée.");
                return;
            }
            foreach (var file in data.Files)
            {
                if (!Path.IsPathFullyQualified(file.FullPath) ||
                    (!IsWithin(sourceRoot, file.FullPath) && !IsWithin(targetRoot, file.FullPath))) continue;
                _checkpointFiles[file.FullPath] = file;
            }
            _hasCheckpoint = true;
        }

        private void ValidateCheckpointPath(string path)
        {
            if (IsWithin(_sourceRoot, path) || IsWithin(_targetRoot, path))
                throw new ArgumentException("Le fichier de reprise doit être enregistré hors des deux dossiers comparés.", nameof(options.CheckpointPath));
            var parent = Path.GetDirectoryName(path) ?? throw new ArgumentException("Chemin de reprise invalide.");
            var ancestor = parent;
            while (true)
            {
                try
                {
                    if ((File.GetAttributes(ancestor) & FileAttributes.Directory) == 0)
                        throw new ArgumentException("Le parent du fichier de reprise n’est pas un dossier.");
                    break;
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    ancestor = Path.GetDirectoryName(ancestor) ?? throw new ArgumentException("Le dossier de reprise n’est pas accessible.");
                }
            }
            var canonicalParent = Path.GetFullPath(Path.Combine(CanonicalRoot(ancestor), Path.GetRelativePath(ancestor, parent)));
            var canonicalPath = Path.Combine(canonicalParent, Path.GetFileName(path));
            if (IsWithin(_sourceCanonical, canonicalPath) || IsWithin(_targetCanonical, canonicalPath))
                throw new ArgumentException("Le fichier de reprise pointe vers un des dossiers comparés via un lien ou une jonction.", nameof(options.CheckpointPath));
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw new ArgumentException("Le fichier de reprise ne doit pas être un lien, une jonction ou un dossier.");
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException) { }
        }

        private bool SaveCheckpoint(bool force = false)
        {
            if (_checkpointPath is null) return false;
            if (!force && (!_checkpointDirty || (_hasCheckpoint && _checkpointTimer.ElapsedMilliseconds < 1000))) return false;
            string? temporary = null;
            try
            {
                ValidateCheckpointPath(_checkpointPath);
                var data = new CheckpointData
                {
                    SourceRoot = _sourceRoot, TargetRoot = _targetRoot,
                    SourceCanonicalRoot = _sourceCanonical, TargetCanonicalRoot = _targetCanonical,
                    FindMovedFiles = options.FindMovedFiles, SavedAt = DateTimeOffset.Now,
                    Files = _checkpointFiles.Values.ToList()
                };
                temporary = Path.Combine(Path.GetDirectoryName(_checkpointPath)!, $".{Path.GetFileName(_checkpointPath)}.{Guid.NewGuid():N}.tmp");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, data);
                    stream.Flush(true);
                }
                // Replace the directory entry, never truncate a possible pre-existing hard link.
                File.Move(temporary, _checkpointPath, true);
                temporary = null;
                _hasCheckpoint = true;
                _checkpointDirty = false;
                _checkpointTimer.Restart();
                return true;
            }
            catch (Exception exception) when (IsFileSystemException(exception) || exception is ArgumentException or JsonException)
            {
                _warnings.Add($"Enregistrement de la reprise impossible : {exception.Message}");
                return false;
            }
            finally
            {
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
                    var rootInfo = new DirectoryInfo(snapshot.Root);
                    rootInfo.Refresh();
                    return (Entry: ReadEntry(rootInfo, "."), Canonical: CanonicalRoot(snapshot.Root));
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
                var problem = $"Impossible de lire la racine « {snapshot.Root} » : {exception.Message}";
                _errors.Add(problem);
                if (snapshot.RootEntry is not null) snapshot.RootEntry = snapshot.RootEntry with { Problem = problem };
                return;
            }

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
                    if (batch is null)
                    {
                        MarkDirectoryProblem(snapshot, directory, "Lien symbolique ou point de jonction ignoré.");
                        continue;
                    }
                    foreach (var entry in batch)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var relative = entry.RelativePath;
                        if (snapshot.Entries.TryGetValue(relative, out var collision))
                        {
                            var problem = $"Chemins ambigus, distincts seulement par la casse : « {collision.FullPath} » et « {entry.FullPath} ».";
                            _errors.Add(problem);
                            snapshot.Entries[relative] = collision with { Problem = problem };
                            continue;
                        }
                        snapshot.Entries.Add(relative, entry);
                        if (entry.Problem is not null) _errors.Add(entry.Problem);
                        if (entry.Kind == EntryKind.File)
                        {
                            if (countDiscovery) _filesDiscovered++;
                        }
                        else if (entry.Problem is null) pending.Push(entry.FullPath);
                        ReportProgress();
                    }
                }
                catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
                {
                    MarkDirectoryProblem(snapshot, directory, $"Dossier non entièrement lisible : {exception.Message}");
                }
            }
        }

        private List<Entry>? ReadDirectoryBatch(string root, string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return null;
            var result = new List<Entry>();
            var enumeration = new EnumerationOptions
            {
                RecurseSubdirectories = false, IgnoreInaccessible = false,
                AttributesToSkip = 0, ReturnSpecialDirectories = false
            };
            foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", enumeration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(root, info.FullName);
                try
                {
                    result.Add(ReadEnumeratedEntry(info, relative));
                }
                catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
                {
                    result.Add(new Entry(relative, info.FullName, info is DirectoryInfo ? EntryKind.Directory : EntryKind.File,
                        null, default, default, 0, $"Impossible de lire « {info.FullName} » : {exception.Message}"));
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
                throw new IOException("Les attributs de l’élément énuméré sont indisponibles.");
            var kind = (attributes & FileAttributes.Directory) != 0 ? EntryKind.Directory : EntryKind.File;
            var problem = (attributes & FileAttributes.ReparsePoint) != 0
                ? $"Lien symbolique, jonction ou fichier à réanalyse ignoré : « {info.FullName} »." : null;
            long? size = null;
            if (kind == EntryKind.File && problem is null)
                size = info is FileInfo file ? file.Length
                    : throw new IOException("Le type de l’élément énuméré est incohérent.");
            return new Entry(relative, info.FullName, kind, size, info.LastWriteTimeUtc, info.CreationTimeUtc, attributes, problem);
        }

        private static Entry ReadEntry(FileSystemInfo info, string relative)
        {
            // Exists suppresses network errors, which would turn a transient disconnection
            // into a misleading 'missing file'. GetAttributes preserves the native error.
            var attributes = File.GetAttributes(info.FullName);
            var kind = (attributes & FileAttributes.Directory) != 0 ? EntryKind.Directory : EntryKind.File;
            var problem = (attributes & FileAttributes.ReparsePoint) != 0
                ? $"Lien symbolique, jonction ou fichier à réanalyse ignoré : « {info.FullName} »." : null;
            long? size = kind == EntryKind.File && problem is null ? new FileInfo(info.FullName).Length : null;
            return new Entry(relative, info.FullName, kind, size, info.LastWriteTimeUtc, info.CreationTimeUtc, attributes, problem);
        }

        private void MarkDirectoryProblem(Snapshot snapshot, string fullPath, string reason)
        {
            var problem = $"« {fullPath} » : {reason}";
            _errors.Add(problem);
            var relative = Path.GetRelativePath(snapshot.Root, fullPath);
            if (relative == ".")
            {
                if (snapshot.RootEntry is not null) snapshot.RootEntry = snapshot.RootEntry with { Problem = problem };
            }
            else if (snapshot.Entries.TryGetValue(relative, out var entry))
                snapshot.Entries[relative] = entry with { Problem = problem };
        }

        private void BuildInitialRows(Snapshot sourceSnapshot, Snapshot targetSnapshot)
        {
            foreach (var relative in sourceSnapshot.Entries.Keys.Union(targetSnapshot.Entries.Keys, StringComparer.OrdinalIgnoreCase))
            {
                sourceSnapshot.Entries.TryGetValue(relative, out var first);
                targetSnapshot.Entries.TryGetValue(relative, out var second);
                var status = ComparisonStatus.Unverified;
                var detail = "Comparaison du contenu non effectuée.";
                if (first?.Problem is not null || second?.Problem is not null)
                    detail = first?.Problem ?? second!.Problem!;
                else if (first is null && AncestorProblem(sourceSnapshot, relative) is { } sourceProblem)
                    detail = sourceProblem;
                else if (second is null && AncestorProblem(targetSnapshot, relative) is { } targetProblem)
                    detail = targetProblem;
                else if (first is null)
                {
                    status = ComparisonStatus.OnlyTarget;
                    detail = "Ce chemin existe uniquement dans le dossier 2.";
                }
                else if (second is null)
                {
                    status = ComparisonStatus.OnlySource;
                    detail = "Ce chemin manque dans le dossier 2.";
                }
                else if (first.Kind != second.Kind)
                {
                    status = ComparisonStatus.Conflict;
                    detail = "Conflit de type : un fichier et un dossier occupent le même chemin.";
                }
                else if (first.Kind == EntryKind.Directory)
                {
                    status = ComparisonStatus.CommonDirectory;
                    detail = "Dossier présent au même chemin des deux côtés.";
                }
                else if (first.Size != second.Size)
                {
                    status = ComparisonStatus.Different;
                    detail = "Même chemin mais tailles différentes : contenus différents.";
                }
                _rows[relative] = new ComparisonRow
                {
                    Kind = first?.Kind ?? second!.Kind,
                    Status = status,
                    RelativePath = first?.RelativePath ?? second!.RelativePath,
                    SourcePath = first?.FullPath,
                    TargetPath = second?.FullPath,
                    SourceSize = first?.Size,
                    TargetSize = second?.Size,
                    Detail = detail
                };
            }
            if (RootProblem(sourceSnapshot) is { } firstProblem)
                AddRootProblemRow(sourceSnapshot.Root, true, firstProblem);
            if (RootProblem(targetSnapshot) is { } secondProblem)
                AddRootProblemRow(targetSnapshot.Root, false, secondProblem);
        }

        private static string? RootProblem(Snapshot snapshot) => snapshot.RootEntry is null
            ? $"Racine non entièrement accessible : « {snapshot.Root} »."
            : snapshot.RootEntry.Problem;

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
                var sourceFiles = sourceSnapshot.Entries.Values.Where(item => item.Kind == EntryKind.File && item.Problem is null && item.Size.HasValue).ToArray();
                var targetFiles = targetSnapshot.Entries.Values.Where(item => item.Kind == EntryKind.File && item.Problem is null && item.Size.HasValue).ToArray();
                var commonSizes = sourceFiles.Select(item => item.Size!.Value).Intersect(targetFiles.Select(item => item.Size!.Value)).ToHashSet();
                foreach (var item in sourceFiles.Concat(targetFiles).Where(item => commonSizes.Contains(item.Size!.Value)))
                    planned[item.FullPath] = item;
            }
            else
            {
                foreach (var item in sourceSnapshot.Entries.Values)
                {
                    if (item.Kind != EntryKind.File || item.Problem is not null ||
                        !targetSnapshot.Entries.TryGetValue(item.RelativePath, out var other) ||
                        other.Kind != EntryKind.File || other.Problem is not null || item.Size != other.Size)
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
            SetPhase("Recherche des fichiers déplacés ou renommés");
            var candidatesBySize = targetSnapshot.Entries.Values
                .Where(item => item.Kind == EntryKind.File && item.Problem is null && item.Size.HasValue)
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
                if (item.Kind != EntryKind.File || item.Problem is not null || !item.Size.HasValue ||
                    _coveredSourceFiles.Contains(item.RelativePath) || !candidatesBySize.TryGetValue(item.Size.Value, out var sizeBucket))
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
                        Detail = $"Même contenu SHA-256 trouvé ailleurs dans le dossier 2 : {match.RelativePath}"
                    };
                }
                else
                {
                    _rows[item.RelativePath] = row with
                    {
                        Detail = row.Detail + $" Une copie du contenu source existe aussi dans le dossier 2 : {match.RelativePath}"
                    };
                }
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
            if (_samples.TryGetValue(entry.FullPath, out var cached)) return cached;
            try { return await RetryOperationAsync(() => ReadSampleAsync(entry), entry.FullPath); }
            catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
            {
                var reason = $"Impossible de présélectionner « {entry.FullPath} » : {exception.Message}";
                _errors.Add(reason);
                _samples[entry.FullPath] = null;
                _checkpointFiles.Remove(entry.FullPath);
                MarkUnverified(entry, reason);
                return null;
            }
        }

        private async Task<string> ReadSampleAsync(Entry entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _currentPath = entry.FullPath;
            ReportProgress(true);
            var before = new FileInfo(entry.FullPath);
            before.Refresh();
            if (!SameMetadata(entry, ReadEntry(before, entry.RelativePath)))
                throw new IOException("Le fichier a changé depuis l’inventaire.");

            // No content verdict can be positive from these samples. They only reject
            // candidates; equal signatures always proceed to a complete SHA-256 read.
            // Disable FileStream buffering so each seek reads exactly its small block.
            await using var stream = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1, FileOptions.Asynchronous | FileOptions.RandomAccess);
            if (stream.Length != entry.Size || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != entry.LastWriteUtc ||
                File.GetCreationTimeUtc(stream.SafeFileHandle) != entry.CreationUtc)
                throw new IOException("Les métadonnées du fichier ouvert ont changé depuis l’inventaire.");
            var identity = FileSafety.Identity(stream.SafeFileHandle);
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
                        var read = await stream.ReadAsync(buffer.AsMemory(total, SampleBlockSize - total), cancellationToken);
                        if (read == 0) throw new IOException("Lecture incomplète pendant la présélection.");
                        total += read;
                        _sampleBytesRead += read;
                        ReportProgress();
                    }
                    hasher.AppendData(buffer, 0, total);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (stream.Length != entry.Size || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != entry.LastWriteUtc ||
                    File.GetCreationTimeUtc(stream.SafeFileHandle) != entry.CreationUtc)
                    throw new IOException("Le fichier a changé pendant la présélection.");
                var after = new FileInfo(entry.FullPath);
                after.Refresh();
                if (!SameMetadata(entry, ReadEntry(after, entry.RelativePath)) ||
                    (identity is not null && identity != FileSafety.Identity(stream.SafeFileHandle)))
                    throw new IOException("L’identité ou les métadonnées du fichier ont changé pendant la présélection.");
                var signature = Convert.ToHexString(hasher.GetHashAndReset());
                _samples[entry.FullPath] = signature;
                return signature;
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private async Task<string?> HashAsync(Entry entry)
        {
            try { return await RetryOperationAsync(() => ReadHashAsync(entry), entry.FullPath); }
            catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
            {
                var reason = $"Impossible de vérifier « {entry.FullPath} » : {exception.Message}";
                _errors.Add(reason); _hashes[entry.FullPath] = null; _checkpointFiles.Remove(entry.FullPath);
                MarkUnverified(entry, reason);
                return null;
            }
        }

        private async Task<string?> ReadHashAsync(Entry entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_hashes.TryGetValue(entry.FullPath, out var cached)) return cached;
            _currentPath = entry.FullPath;
            ReportProgress(true);
            byte[]? buffer = null;
            try
            {
                var before = new FileInfo(entry.FullPath);
                before.Refresh();
                if (!SameMetadata(entry, ReadEntry(before, entry.RelativePath)))
                    throw new IOException("Le fichier a changé depuis l’inventaire.");

                // Deny concurrent write/delete handles while bytes are read. An existing
                // incompatible writer causes a reported sharing violation, never a verdict.
                await using var stream = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (stream.Length != entry.Size || File.GetLastWriteTimeUtc(stream.SafeFileHandle) != entry.LastWriteUtc ||
                    File.GetCreationTimeUtc(stream.SafeFileHandle) != entry.CreationUtc)
                    throw new IOException("Les métadonnées du fichier ouvert ont changé depuis l’inventaire.");

                var identity = FileSafety.Identity(stream.SafeFileHandle);
                if (_checkpointFiles.TryGetValue(entry.FullPath, out var saved) && identity is not null && saved.Identity == identity &&
                    saved.Size == entry.Size && saved.LastWriteUtc == entry.LastWriteUtc && saved.CreationUtc == entry.CreationUtc && saved.Attributes == entry.Attributes)
                {
                    _hashes[entry.FullPath] = saved.Hash;
                    _filesReused++;
                    _totalHashBytes = Math.Max(0, _totalHashBytes - (entry.Size ?? 0));
                    ReportProgress(true);
                    return saved.Hash;
                }
                _checkpointFiles.Remove(entry.FullPath);

                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
                long readForFile = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    readForFile += read;
                    _bytesHashed += read;
                    ReportProgress();
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (readForFile != entry.Size || stream.Length != entry.Size ||
                    File.GetLastWriteTimeUtc(stream.SafeFileHandle) != entry.LastWriteUtc ||
                    File.GetCreationTimeUtc(stream.SafeFileHandle) != entry.CreationUtc)
                    throw new IOException("Le fichier a changé pendant le calcul SHA-256.");
                var after = new FileInfo(entry.FullPath);
                after.Refresh();
                if (!SameMetadata(entry, ReadEntry(after, entry.RelativePath)))
                    throw new IOException("Le chemin ou les métadonnées du fichier ont changé pendant sa lecture.");
                if (identity is not null && identity != FileSafety.Identity(stream.SafeFileHandle))
                    throw new IOException("L’identité ou la date de changement du fichier a changé pendant sa lecture.");

                var hash = Convert.ToHexString(hasher.GetHashAndReset());
                _hashes[entry.FullPath] = hash;
                _filesHashed++;
                _checkpointFiles[entry.FullPath] = new CheckpointFile { FullPath = entry.FullPath, Size = entry.Size!.Value, LastWriteUtc = entry.LastWriteUtc, CreationUtc = entry.CreationUtc, Attributes = entry.Attributes, Hash = hash, Identity = identity };
                _checkpointDirty = true;
                SaveCheckpoint();
                ReportProgress(true);
                return hash;
            }
            finally
            {
                if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private bool VerifySnapshot(Snapshot original, Snapshot final, bool isSource)
        {
            var stable = true;
            if (original.RootEntry is null || final.RootEntry is null ||
                !SameMetadata(original.RootEntry, final.RootEntry) ||
                !string.Equals(original.CanonicalRoot, final.CanonicalRoot, StringComparison.OrdinalIgnoreCase))
            {
                stable = false;
                var problem = $"La racine « {original.Root} » a changé ou n’est plus entièrement accessible pendant la comparaison.";
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
                var problem = $"Élément ajouté, supprimé ou modifié pendant la comparaison : « {entry.FullPath} ». Relancez l’analyse.";
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

        private void MarkUnverified(Entry entry, string reason)
        {
            foreach (var pair in _rows.ToArray())
            {
                if (string.Equals(pair.Value.SourcePath, entry.FullPath, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Value.TargetPath, entry.FullPath, StringComparison.OrdinalIgnoreCase))
                    _rows[pair.Key] = pair.Value with { Status = ComparisonStatus.Unverified, Detail = reason };
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
            if (progress is null || (!force && _progressTimer.ElapsedMilliseconds < 80)) return;
            _progressTimer.Restart();
            progress.Report(new ComparisonProgress
            {
                Phase = _phase,
                CurrentPath = _currentPath,
                FilesDiscovered = _filesDiscovered,
                FilesHashed = _filesHashed,
                BytesHashed = _bytesHashed,
                SampleBytesRead = _sampleBytesRead,
                TotalHashBytes = _totalHashBytes,
                FilesReused = _filesReused,
                RetryAttempt = _retryAttempt
            });
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
        DateTime LastWriteUtc, DateTime CreationUtc, FileAttributes Attributes, string? Problem);

    private static bool SameMetadata(Entry first, Entry second) =>
        first.Kind == second.Kind && first.Size == second.Size && first.LastWriteUtc == second.LastWriteUtc &&
        first.CreationUtc == second.CreationUtc && first.Attributes == second.Attributes && first.Problem == second.Problem;

    private static bool IsFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException or Win32Exception;

    private static bool IsWithin(string root, string path) => FileSafety.IsWithin(root, path);

    private static string NormalizeRoot(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"Choisissez le {label}.", nameof(path));
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) == 0) throw new ArgumentException($"Le {label} n’est pas un dossier : {fullPath}", nameof(path));
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException($"Le {label} est un lien ou une jonction. Sélectionnez son dossier réel.", nameof(path));
            return fullPath;
        }
        catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
        {
            throw new ArgumentException($"Impossible d’ouvrir le {label} : {exception.Message}", nameof(path), exception);
        }
    }

    private static void ValidateSeparateRoots(string sourceRoot, string targetRoot)
    {
        try
        {
            if (RelatedRoots(sourceRoot, targetRoot) || RelatedRoots(CanonicalRoot(sourceRoot), CanonicalRoot(targetRoot)))
                throw new ArgumentException("Choisissez deux dossiers distincts : aucun ne doit être contenu dans l’autre.");
        }
        catch (Exception exception) when (IsFileSystemException(exception) && !FileSystemRetryPolicy.IsTransient(exception))
        {
            throw new ArgumentException($"Impossible de confirmer les chemins réels des dossiers : {exception.Message}", exception);
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
        using var handle = CreateFileW(path, 0x80, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
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
