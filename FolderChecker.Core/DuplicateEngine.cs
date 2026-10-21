using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FolderChecker.Core;

/// <summary>
/// Size and sparse samples eliminate candidates; only complete SHA-256 digests establish duplicates.
/// Persistent samples and digests require matching native identity, change time and metadata.
/// </summary>
public sealed class DuplicateEngine
{
    public static string? ReadCheckpointRoot(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 128L * 1024 * 1024) return null;
            var cache = JsonSerializer.Deserialize<Cache>(stream);
            return cache is { Schema: 1, Algorithm: "SHA256-SAMPLE64K-V1", Files: not null }
                && !string.IsNullOrWhiteSpace(cache.Root) && Path.IsPathFullyQualified(cache.Root)
                && !string.IsNullOrWhiteSpace(cache.CanonicalRoot) ? cache.Root : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public Task<DuplicateResult> ScanAsync(string root, DuplicateScanOptions options,
        IProgress<DuplicateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxRetries);
        ArgumentOutOfRangeException.ThrowIfNegative(options.InitialRetryDelayMs);
        return Task.Run(() => new Run(root, options, progress, cancellationToken).ExecuteAsync());
    }

    internal static string? IdentityString(FileIdentity? identity) => identity is null || identity.ChangeTime <= 0 ? null
        : $"{identity.Volume:X16}:{identity.IdLow:X16}:{identity.IdHigh:X16}:{identity.ChangeTime:X16}";

    internal static int? HardLinkCount(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows() || !GetFileInformationByHandleEx(handle, 1,
                out StandardInfo information, (uint)Marshal.SizeOf<StandardInfo>())
            || information.NumberOfLinks is 0 or > int.MaxValue) return null;
        return (int)information.NumberOfLinks;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass,
        out StandardInfo information, uint size);

    private sealed record CacheEntry
    {
        public DuplicateFile File { get; init; } = new();
        public string Sample { get; init; } = "";
        public string? Hash { get; init; }
    }

    private sealed record Cache
    {
        public int Schema { get; init; } = 1;
        public string Algorithm { get; init; } = "SHA256-SAMPLE64K-V1";
        public string Root { get; init; } = "";
        public string CanonicalRoot { get; init; } = "";
        public DateTimeOffset SavedAt { get; init; }
        public List<CacheEntry> Files { get; init; } = [];
    }

    private sealed record InventoryItem(DuplicateFile File, bool Directory);

    private sealed class Run(string requestedRoot, DuplicateScanOptions options,
        IProgress<DuplicateProgress>? progress, CancellationToken cancellationToken)
    {
        private const int SampleLength = 64 * 1024;
        private readonly Dictionary<string, CacheEntry> _cached = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CacheEntry> _completed = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reused = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _fullyHashed = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _errors = new(StringComparer.Ordinal);
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private readonly List<DuplicateGroup> _groups = [];
        private readonly Stopwatch _saveTimer = Stopwatch.StartNew();
        private string _root = "", _canonicalRoot = "", _phase = "Préparation", _currentPath = "";
        private string? _checkpoint;
        private long _bytesRead, _filesScanned;
        private int _retryAttempt;
        private bool _checkpointDirty;

        internal async Task<DuplicateResult> ExecuteAsync()
        {
            _root = requestedRoot;
            var cancelled = false;
            var verified = false;
            var checkpointSaved = false;
            try
            {
                if (string.IsNullOrWhiteSpace(requestedRoot))
                    throw new ArgumentException("Sélectionnez un dossier à analyser.", nameof(requestedRoot));
                _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requestedRoot.Trim().Trim('"')));
                await RetryAsync(() =>
                {
                    if ((File.GetAttributes(_root) & FileAttributes.Directory) == 0)
                        throw new ArgumentException("Le chemin sélectionné ne désigne pas un dossier.");
                    _canonicalRoot = Canonical(_root);
                    return true;
                }, _root);
                PrepareCheckpoint();
                SetPhase("Inventaire des fichiers");
                var initial = await InventoryAsync(true);
                foreach (var sizeGroup in initial.Values.Where(item => !item.Directory
                             && (item.File.Attributes & FileAttributes.ReparsePoint) == 0)
                             .GroupBy(item => item.File.Size).Where(group => group.Count() > 1))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SetPhase("Filtrage rapide des candidats");
                    var candidates = new List<CacheEntry>();
                    var physicalFiles = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in sizeGroup.OrderBy(item => item.File.RelativePath, StringComparer.OrdinalIgnoreCase))
                    {
                        var sample = await ReadSafeAsync(item.File, false);
                        if (sample is null) continue;
                        var physical = PhysicalIdentity(sample.File.Identity);
                        if (physical is not null && !physicalFiles.Add(physical))
                        {
                            _warnings.Add($"Lien physique écarté du calcul d’espace récupérable : {item.File.RelativePath}");
                            continue;
                        }
                        candidates.Add(sample);
                    }
                    foreach (var sampleGroup in candidates.GroupBy(item => item.Sample).Where(group => group.Count() > 1))
                    {
                        SetPhase("Vérification SHA-256 complète");
                        var hashed = new List<CacheEntry>();
                        foreach (var candidate in sampleGroup)
                        {
                            var complete = candidate.Hash is not null ? candidate : await ReadSafeAsync(candidate.File, true);
                            if (complete?.Hash is not null)
                            {
                                _fullyHashed.Add(complete.File.FullPath);
                                hashed.Add(complete);
                            }
                        }
                        foreach (var hashGroup in hashed.GroupBy(item => item.Hash!).Where(group => group.Count() > 1))
                        {
                            _groups.Add(new DuplicateGroup
                            {
                                Hash = hashGroup.Key, Size = sizeGroup.Key,
                                Files = hashGroup.Select(item => item.File).OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray()
                            });
                        }
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                SetPhase("Contrôle final des modifications");
                var final = await InventoryAsync(false);
                if (!string.Equals(_canonicalRoot, await RetryAsync(() => Canonical(_root), _root), StringComparison.OrdinalIgnoreCase))
                    _errors.Add("La destination réelle du dossier a changé pendant le scan.");
                if (initial.Count != final.Count)
                    _errors.Add("Des fichiers ou dossiers ont été ajoutés ou retirés pendant le scan. Relancez-le.");
                foreach (var pair in initial)
                {
                    if (!final.TryGetValue(pair.Key, out var last))
                        _errors.Add($"Chemin retiré pendant le scan : {pair.Key}");
                    else if (pair.Value.Directory != last.Directory || !SameMetadata(pair.Value.File, last.File))
                        _errors.Add($"Chemin modifié pendant le scan : {pair.Key}. Relancez la recherche.");
                }
                // The identity check also detects changes whose size and timestamps were restored.
                foreach (var completed in _completed.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ValidateFinalAsync(completed.File);
                }
                cancellationToken.ThrowIfCancellationRequested();
                verified = _errors.Count == 0;
                SetPhase(verified ? "Recherche de doublons terminée" : "Recherche incomplète");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                SetPhase("Recherche arrêtée — progression conservée");
            }
            catch (Exception exception) when (FileSystemRetryPolicy.IsTransient(exception))
            {
                _errors.Add($"Connexion indisponible après {options.MaxRetries} nouvelle(s) tentative(s) : {exception.Message}");
                SetPhase("Connexion interrompue — reprise après reconnexion");
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                _errors.Add(exception.Message);
                SetPhase("Recherche incomplète");
            }
            finally
            {
                checkpointSaved = SaveCheckpoint(true);
            }
            return new DuplicateResult
            {
                Root = _root, CanonicalRoot = _canonicalRoot,
                Groups = _groups.OrderByDescending(group => group.Size * (group.Files.Count - 1))
                    .ThenBy(group => group.Files[0].RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
                Errors = _errors.ToArray(), Warnings = _warnings.ToArray(), Cancelled = cancelled,
                SnapshotVerified = verified && !cancelled, FilesScanned = _filesScanned,
                FilesHashed = _fullyHashed.Count, BytesRead = _bytesRead, FilesReused = _reused.Count,
                CheckpointSaved = checkpointSaved
            };
        }

        private async Task<Dictionary<string, InventoryItem>> InventoryAsync(bool count)
        {
            var entries = new Dictionary<string, InventoryItem>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(_root);
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _currentPath = directory;
                List<InventoryItem> batch;
                try
                {
                    batch = await RetryAsync(() =>
                    {
                        var items = new List<InventoryItem>();
                        var directoryAttributes = File.GetAttributes(directory);
                        if (!string.Equals(directory, _root, StringComparison.OrdinalIgnoreCase)
                            && (directoryAttributes & FileAttributes.ReparsePoint) != 0)
                            throw new IOException($"Le dossier est devenu un lien : {directory}");
                        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            // Enumeration already supplies these metadata. Re-querying every path
                            // would add multiple network round trips per file before any hashing.
                            var attributes = entry.Attributes;
                            var isDirectory = (attributes & FileAttributes.Directory) != 0;
                            if (isDirectory && (attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                // NTFS directory-index timestamps can lag behind child creation.
                                // Refresh directories only so their final check compares current
                                // metadata without adding a network round trip for every file.
                                entry.Refresh();
                                attributes = File.GetAttributes(entry.FullName);
                                if ((attributes & FileAttributes.Directory) == 0)
                                    throw new IOException($"Le dossier a changé de type : {entry.FullName}");
                            }
                            if (string.Equals(directory, _root, StringComparison.OrdinalIgnoreCase)
                                && isDirectory && string.Equals(entry.Name, DuplicateRemoval.QuarantineDirectoryName,
                                    StringComparison.OrdinalIgnoreCase)
                                && DuplicateRemoval.IsManagedQuarantine(entry.FullName))
                            {
                                _warnings.Add("La quarantaine Checker San est exclue de la recherche de doublons.");
                                continue;
                            }
                            var file = new DuplicateFile
                            {
                                FullPath = entry.FullName, RelativePath = Path.GetRelativePath(_root, entry.FullName),
                                Size = isDirectory || (attributes & FileAttributes.ReparsePoint) != 0 ? 0 : ((FileInfo)entry).Length,
                                LastWriteUtc = entry.LastWriteTimeUtc, CreationUtc = entry.CreationTimeUtc, Attributes = attributes
                            };
                            items.Add(new InventoryItem(file, isDirectory));
                        }
                        return items;
                    }, directory);
                }
                catch (Exception exception) when (IsFileError(exception) && !FileSystemRetryPolicy.IsTransient(exception))
                {
                    _errors.Add($"Inventaire impossible ({directory}) : {exception.Message}");
                    continue;
                }
                foreach (var item in batch)
                {
                    if (!entries.TryAdd(item.File.RelativePath, item))
                    {
                        _errors.Add($"Chemins ambigus, seule la casse diffère : {item.File.RelativePath}");
                        continue;
                    }
                    if ((item.File.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        _errors.Add($"Lien ou point de réanalyse ignoré : {item.File.RelativePath}");
                        continue;
                    }
                    if (item.Directory) pending.Push(item.File.FullPath);
                    else if (count) _filesScanned++;
                }
                Report();
            }
            return entries;
        }

        private async Task<CacheEntry?> ReadSafeAsync(DuplicateFile file, bool full)
        {
            _currentPath = file.FullPath;
            try
            {
                return await RetryAsync(() => Read(file, full), file.FullPath);
            }
            catch (Exception exception) when (IsFileError(exception) && !FileSystemRetryPolicy.IsTransient(exception))
            {
                _errors.Add($"Lecture impossible ({file.RelativePath}) : {exception.Message}");
                return null;
            }
        }

        private CacheEntry? Read(DuplicateFile expected, bool full)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(expected.FullPath);
            using var stream = new FileStream(expected.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                full ? 1024 * 1024 : 1, full ? FileOptions.SequentialScan : FileOptions.RandomAccess);
            var current = Describe(expected, IdentityString(FileSafety.Identity(stream.SafeFileHandle)));
            if (!SameMetadata(expected, current) || stream.Length != expected.Size
                || (expected.Identity is not null && expected.Identity != current.Identity))
                throw new IOException("Le fichier a changé depuis son inventaire.");
            var linkCount = HardLinkCount(stream.SafeFileHandle);
            if (linkCount > 1)
            {
                _warnings.Add($"Lien physique écarté du calcul d’espace récupérable : {expected.RelativePath}");
                return null;
            }
            if (linkCount is null)
                _warnings.Add("Le système ne fournit pas le nombre de liens physiques de certains fichiers : leur suppression automatique sera refusée.");
            CacheEntry? prior = null;
            if (_completed.TryGetValue(current.FullPath, out var inRun)) prior = inRun;
            else if (_cached.TryGetValue(current.FullPath, out var persisted)) prior = persisted;
            if (current.Identity is not null && prior is not null && SameMetadata(prior.File, current)
                && prior.File.Identity == current.Identity && (!full || prior.Hash is not null))
            {
                if (!_completed.ContainsKey(current.FullPath)) _reused.Add(current.FullPath);
                _completed[current.FullPath] = prior;
                if (prior.Hash is not null) _fullyHashed.Add(current.FullPath);
                Report();
                return prior;
            }
            var small = stream.Length <= SampleLength * 3L;
            var digest = full || small ? ReadWhole(stream) : ReadSample(stream);
            var after = Describe(current, IdentityString(FileSafety.Identity(stream.SafeFileHandle)));
            if (!SameMetadata(current, after) || stream.Length != current.Size || current.Identity != after.Identity)
                throw new IOException("Le fichier a changé pendant sa lecture.");
            var entry = new CacheEntry
            {
                File = after,
                Sample = full && !small ? prior?.Sample ?? "" : digest,
                Hash = full || small ? digest : null
            };
            // Full hashing follows a successful sample for every large candidate.
            if (entry.Sample.Length != 64) throw new IOException("Échantillon du fichier indisponible.");
            _completed[current.FullPath] = entry;
            if (entry.Hash is not null) _fullyHashed.Add(current.FullPath);
            _checkpointDirty = true;
            SaveCheckpoint(false);
            Report();
            return entry;
        }

        private string ReadWhole(FileStream stream)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            try
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, read);
                    _bytesRead += read;
                    Report();
                }
                cancellationToken.ThrowIfCancellationRequested();
                return Convert.ToHexString(hash.GetHashAndReset());
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private string ReadSample(FileStream stream)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(SampleLength);
            try
            {
                long[] offsets = [0, (stream.Length - SampleLength) / 2, stream.Length - SampleLength];
                foreach (var offset in offsets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Position = offset;
                    var count = 0;
                    while (count < SampleLength)
                    {
                        var read = stream.Read(buffer, count, SampleLength - count);
                        if (read == 0) throw new IOException("Fin de fichier inattendue pendant l’échantillonnage.");
                        _bytesRead += read;
                        count += read;
                    }
                    hash.AppendData(buffer, 0, count);
                    Report();
                }
                return Convert.ToHexString(hash.GetHashAndReset());
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private async Task ValidateFinalAsync(DuplicateFile file)
        {
            try
            {
                await RetryAsync(() =>
                {
                    ValidatePath(file.FullPath);
                    using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var current = Describe(file, IdentityString(FileSafety.Identity(stream.SafeFileHandle)));
                    if (!SameMetadata(file, current) || stream.Length != file.Size
                        || (file.Identity is not null && file.Identity != current.Identity))
                        throw new IOException("Le fichier a changé après sa vérification.");
                    return true;
                }, file.FullPath);
            }
            catch (Exception exception) when (IsFileError(exception) && !FileSystemRetryPolicy.IsTransient(exception))
            {
                _errors.Add($"Contrôle final impossible ({file.RelativePath}) : {exception.Message}");
            }
        }

        private void ValidatePath(string path)
        {
            if (!FileSafety.IsWithin(_root, path)) throw new IOException("Fichier situé hors du dossier analysé.");
            var relative = Path.GetRelativePath(_root, path);
            var current = _root;
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Un lien ou point de réanalyse a remplacé le chemin.");
            }
        }

        private static DuplicateFile Describe(DuplicateFile original, string? identity)
        {
            var info = new FileInfo(original.FullPath);
            var attributes = File.GetAttributes(original.FullPath);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new IOException("Le chemin ne désigne plus un fichier ordinaire.");
            return original with
            {
                Size = info.Length, LastWriteUtc = info.LastWriteTimeUtc, CreationUtc = info.CreationTimeUtc,
                Attributes = attributes, Identity = identity
            };
        }

        private void PrepareCheckpoint()
        {
            if (string.IsNullOrWhiteSpace(options.CheckpointPath)) return;
            var checkpoint = Path.GetFullPath(options.CheckpointPath);
            ValidateCheckpoint(checkpoint);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(checkpoint)!);
                ValidateCheckpoint(checkpoint);
                _checkpoint = checkpoint;
                if (!options.ResumeFromCheckpoint || !File.Exists(checkpoint)) return;
                using var stream = new FileStream(checkpoint, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 128L * 1024 * 1024) throw new JsonException("Session trop volumineuse.");
                var cache = JsonSerializer.Deserialize<Cache>(stream);
                if (cache is not { Schema: 1, Algorithm: "SHA256-SAMPLE64K-V1", Files: not null }
                    || !string.Equals(cache.Root, _root, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(cache.CanonicalRoot, _canonicalRoot, StringComparison.OrdinalIgnoreCase))
                    return;
                foreach (var entry in cache.Files)
                {
                    if (entry?.File is null || string.IsNullOrWhiteSpace(entry.File.FullPath)
                        || !Path.IsPathFullyQualified(entry.File.FullPath)
                        || !FileSafety.IsWithin(_root, entry.File.FullPath) || entry.File.Size < 0
                        || !string.Equals(Path.GetRelativePath(_root, entry.File.FullPath), entry.File.RelativePath,
                            StringComparison.OrdinalIgnoreCase)
                        || !ValidHash(entry.Sample) || (entry.Hash is not null && !ValidHash(entry.Hash))) continue;
                    _cached[entry.File.FullPath] = entry;
                }
            }
            catch (Exception exception) when (IsFileError(exception) || exception is JsonException or ArgumentException)
            {
                _warnings.Add($"Session de reprise non disponible : {exception.Message}");
            }
        }

        private void ValidateCheckpoint(string path)
        {
            if (FileSafety.IsWithin(_root, path) || FileSafety.IsWithin(_canonicalRoot, path))
                throw new ArgumentException("La session de reprise doit être enregistrée hors du dossier analysé.");
            var parent = Path.GetDirectoryName(path)!;
            var existing = parent;
            while (!Directory.Exists(existing))
                existing = Path.GetDirectoryName(existing) ?? throw new ArgumentException("Dossier de session invalide.");
            var canonicalParent = Path.GetFullPath(Path.Combine(Canonical(existing), Path.GetRelativePath(existing, parent)));
            if (FileSafety.IsWithin(_canonicalRoot, Path.Combine(canonicalParent, Path.GetFileName(path))))
                throw new ArgumentException("Un lien redirige la session de reprise dans le dossier analysé.");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("La session de reprise ne peut pas être un lien.");
        }

        private bool SaveCheckpoint(bool force)
        {
            if (_checkpoint is null) return false;
            if (!force && (!_checkpointDirty || _saveTimer.ElapsedMilliseconds < 1000)) return false;
            string? temporary = null;
            try
            {
                ValidateCheckpoint(_checkpoint);
                temporary = _checkpoint + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, new Cache
                    {
                        Root = _root, CanonicalRoot = _canonicalRoot, SavedAt = DateTimeOffset.Now,
                        Files = _cached.Values.Where(entry => !_completed.ContainsKey(entry.File.FullPath))
                            .Concat(_completed.Values).ToList()
                    });
                    stream.Flush(true);
                }
                ValidateCheckpoint(_checkpoint);
                File.Move(temporary, _checkpoint, true);
                _checkpointDirty = false;
                _saveTimer.Restart();
                return true;
            }
            catch (Exception exception) when (IsFileError(exception) || exception is ArgumentException)
            {
                _warnings.Add($"Progression non enregistrée : {exception.Message}");
                return false;
            }
            finally
            {
                if (temporary is not null)
                    try { File.Delete(temporary); } catch (Exception exception) when (IsFileError(exception)) { }
            }
        }

        private async Task<T> RetryAsync<T>(Func<T> operation, string path)
        {
            var previousPhase = _phase;
            try
            {
                return await FileSystemRetryPolicy.ExecuteAsync(_ => Task.FromResult(operation()),
                    options.MaxRetries, options.InitialRetryDelayMs, (attempt, delay) =>
                    {
                        _retryAttempt = attempt;
                        _phase = $"Connexion interrompue — tentative {attempt}/{options.MaxRetries} dans {delay.TotalSeconds:0.#} s";
                        _currentPath = path;
                        Report();
                    }, cancellationToken);
            }
            finally { _retryAttempt = 0; _phase = previousPhase; }
        }

        private static bool SameMetadata(DuplicateFile left, DuplicateFile right) =>
            left.Size == right.Size && left.LastWriteUtc == right.LastWriteUtc
            && left.CreationUtc == right.CreationUtc && left.Attributes == right.Attributes;
        private static bool ValidHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        private static string? PhysicalIdentity(string? identity) => identity is null ? null : identity[..identity.LastIndexOf(':')];
        private static bool IsFileError(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or Win32Exception or System.Security.SecurityException;
        private static string Canonical(string path) => OperatingSystem.IsWindows() ? FileSafety.CanonicalDirectory(path) : Path.GetFullPath(path);
        private void SetPhase(string phase) { _phase = phase; Report(); }
        private void Report() => progress?.Report(new DuplicateProgress
        {
            Phase = _phase, CurrentPath = _currentPath, FilesScanned = _filesScanned,
            FilesHashed = _fullyHashed.Count, BytesRead = _bytesRead, FilesReused = _reused.Count, RetryAttempt = _retryAttempt
        });
    }
}
