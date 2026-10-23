using System.Security.Cryptography;
using System.Text.Json;

namespace FolderChecker.Core;

public sealed record SnapshotFile(string Path, long Size, DateTime LastWriteUtc, string Sha256);

/// <summary>
/// The relative paths, sizes and complete SHA-256 of a folder at one moment, to check it again later, for
/// example a backup disk that is usually disconnected. Links are listed but never followed.
/// </summary>
public sealed record FolderSnapshot
{
    public int Schema { get; init; } = 1;
    public string Application { get; init; } = "Checker San";
    public string Algorithm { get; init; } = "SHA256";
    public string Root { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<string> Directories { get; init; } = [];
    public IReadOnlyList<SnapshotFile> Files { get; init; } = [];
    /// <summary>Links and junctions: reported, not followed, not part of the comparison.</summary>
    public IReadOnlyList<string> Links { get; init; } = [];
    /// <summary>Files not hashed (unreadable, online-only, changed while read): their contents are not proven.</summary>
    public IReadOnlyList<string> Unverified { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    /// <summary>Folders that could not be listed ("." for the root): what they hold is unknown, neither missing nor added.</summary>
    public IReadOnlyList<string> UnreadableFolders { get; init; } = [];
    /// <summary>Every file was hashed and the final listing matched the first one.</summary>
    public bool Complete { get; init; }
}

public enum SnapshotChange { Missing, Changed, Added, MissingFolder, AddedFolder }

public sealed record SnapshotDifference(string Path, SnapshotChange Change);

public sealed record SnapshotVerification(IReadOnlyList<SnapshotDifference> Differences, bool Certain)
{
    /// <summary>Proven identical: both snapshots complete and no difference at all.</summary>
    public bool Identical => Certain && Differences.Count == 0;
}

public sealed record SnapshotProgress(string CurrentPath, long FilesHashed, long FilesTotal, long BytesRead);

public sealed class SnapshotEngine
{
    static SnapshotEngine() => FileSafety.Initialize();

    public Task<FolderSnapshot> CreateAsync(string root, int? parallelReads = null,
        IProgress<SnapshotProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(root, parallelReads, progress, cancellationToken), cancellationToken);

    private static async Task<FolderSnapshot> Create(string requested, int? parallelReads, IProgress<SnapshotProgress>? progress, CancellationToken token)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(requested.Trim().Trim('"')));
        var rootMetadata = FileSafety.Stat(root);
        if ((rootMetadata.Attributes & FileAttributes.Directory) == 0 || FileSafety.Access(root, rootMetadata.Attributes) == EntryAccess.Link)
            throw new ArgumentException(L.T("Choisissez un dossier réel, pas un fichier, un lien ou une jonction.", "Choose a physical folder, not a file, link or junction."));
        var gate = new object();
        var errors = new List<string>();
        var unverified = new List<string>();
        var links = new List<string>();
        var unreadable = new List<string>();
        var first = await ListAsync(root, links, errors, unreadable, token);
        var files = first.Where(pair => !pair.Value.Directory && pair.Value.Access == EntryAccess.Normal).Select(pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        unverified.AddRange(first.Where(pair => pair.Value.Access == EntryAccess.OnlineOnly).Select(pair => pair.Key));
        var hashed = new SnapshotFile?[files.Length];
        var identities = new FileIdentity?[files.Length];
        long done = 0, bytes = 0;
        var readers = new DeviceReaders([root], parallelReads);
        await Parallel.ForEachAsync(Enumerable.Range(0, files.Length), new ParallelOptions { MaxDegreeOfParallelism = readers.Slots, CancellationToken = token }, async (i, ct) =>
        {
            var relative = files[i];
            try
            {
                (hashed[i], identities[i]) = await readers.Run(first[relative].FullName, () => FileSystemRetryPolicy.ExecuteAsync(
                    _ => Task.FromResult(Hash(relative, first[relative], ct, read => Interlocked.Add(ref bytes, read))), 3, 1000, null, ct), ct);
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                lock (gate) { unverified.Add(relative); errors.Add(L.F($"« {relative} » : {exception.Message}", $"‘{relative}’: {exception.Message}")); }
            }
            progress?.Report(new SnapshotProgress(relative, Interlocked.Increment(ref done), files.Length, Interlocked.Read(ref bytes)));
        });
        // A file added, removed or changed during the snapshot makes it an unproven picture of the folder.
        var final = await ListAsync(root, [], [], [], token);
        foreach (var relative in first.Keys.Union(final.Keys, StringComparer.OrdinalIgnoreCase))
            if (!first.TryGetValue(relative, out var before) || !final.TryGetValue(relative, out var after) || before != after)
                errors.Add(L.F($"Élément ajouté, supprimé ou modifié pendant l’instantané : « {relative} ».", $"Item added, removed or changed during the snapshot: ‘{relative}’."));
        // Listings miss an in-place write that keeps size and date, and any write through another hard link.
        for (var i = 0; i < files.Length; i++)
        {
            if (hashed[i] is null || identities[i] is null) continue;
            FileIdentity? now;
            try { now = FileSafety.CurrentIdentity(first[files[i]].FullName); }
            catch (Exception exception) when (IsFileError(exception)) { now = null; }
            if (now == identities[i]) continue;
            errors.Add(L.F($"Fichier modifié pendant l’instantané, après sa lecture : « {files[i]} ».", $"File changed during the snapshot, after it was read: ‘{files[i]}’."));
            unverified.Add(files[i]);
            hashed[i] = null;
        }
        return new FolderSnapshot
        {
            Root = root, CreatedAt = DateTimeOffset.Now,
            Directories = first.Where(pair => pair.Value.Directory && pair.Value.Access != EntryAccess.Link).Select(pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Files = hashed.OfType<SnapshotFile>().ToArray(),
            Links = links.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Unverified = unverified.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Errors = errors.Distinct().ToArray(),
            UnreadableFolders = unreadable.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Complete = errors.Count == 0 && unverified.Count == 0
        };
    }

    private sealed record Listed(string FullName, bool Directory, EntryAccess Access, long Size, DateTime LastWriteUtc, DateTime CreationUtc, FileAttributes Attributes);

    private static Task<Dictionary<string, Listed>> ListAsync(string root, List<string> links, List<string> errors, List<string> unreadable,
        CancellationToken token) => Task.Run(() =>
    {
        var entries = new Dictionary<string, Listed>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false, BufferSize = 64 * 1024 };
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
                {
                    var relative = FileSafety.RelativeTo(root, info.FullName);
                    var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                    var access = FileSafety.Access(info.FullName, info.Attributes);
                    // In a case-sensitive folder two names may differ only by case: one entry would hide the other.
                    if (entries.TryGetValue(relative, out var twin))
                    {
                        errors.Add(L.F($"Chemins ambigus, distincts seulement par la casse : « {twin.FullName} » et « {info.FullName} ».",
                            $"Ambiguous paths that differ only by case: ‘{twin.FullName}’ and ‘{info.FullName}’."));
                        continue;
                    }
                    if (access == EntryAccess.Link) links.Add(relative);
                    else if (isDirectory) pending.Push(info.FullName);
                    // Directory dates move when the snapshot's own reads update access times; only files keep their dates.
                    entries[relative] = isDirectory
                        ? new Listed(info.FullName, true, access, 0, default, default, default)
                        : new Listed(info.FullName, false, access, access == EntryAccess.Link ? 0 : ((FileInfo)info).Length, info.LastWriteTimeUtc, info.CreationTimeUtc, info.Attributes);
                }
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                var relative = Path.GetRelativePath(root, directory);
                unreadable.Add(relative);
                errors.Add(L.F($"Dossier non entièrement lisible « {relative} » : {exception.Message}", $"Folder not fully readable ‘{relative}’: {exception.Message}"));
            }
        }
        return entries;
    }, token);

    private static (SnapshotFile File, FileIdentity? Identity) Hash(string relative, Listed listed, CancellationToken token, Action<int> onRead)
    {
        // The listed name, not the relative key: in a case-sensitive folder the key's spelling may be the other file's.
        using var stream = new FileStream(FileSafety.Native(listed.FullName), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        var opened = FileSafety.Describe(stream.SafeFileHandle);
        if (stream.Length != listed.Size || opened.LastWriteUtc != listed.LastWriteUtc)
            throw new IOException(L.T("Le fichier a changé depuis l’inventaire.", "The file has changed since it was listed."));
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            token.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
            total += read;
            onRead(read);
        }
        var closing = FileSafety.Describe(stream.SafeFileHandle);
        if (total != listed.Size || closing.LastWriteUtc != listed.LastWriteUtc || (opened.Identity is not null && opened.Identity != closing.Identity))
            throw new IOException(L.T("Le fichier a changé pendant sa lecture.", "The file changed while it was being read."));
        return (new SnapshotFile(relative, total, listed.LastWriteUtc, Convert.ToHexString(hasher.GetHashAndReset())), closing.Identity);
    }

    /// <summary>Differences between a saved snapshot and the folder now; certain only if both are complete.</summary>
    public static SnapshotVerification Compare(FolderSnapshot expected, FolderSnapshot actual)
    {
        var differences = new List<SnapshotDifference>();
        var now = actual.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var unknown = actual.Unverified.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // What an unreadable folder holds is unknown: nothing under it is missing now, nor was absent before.
        bool UnreadableNow(string path) => Under(path, actual.UnreadableFolders);
        bool UnreadableBefore(string path) => Under(path, expected.UnreadableFolders);
        foreach (var file in expected.Files)
        {
            if (now.TryGetValue(file.Path, out var current))
            {
                if (current.Size != file.Size || !string.Equals(current.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    differences.Add(new SnapshotDifference(file.Path, SnapshotChange.Changed));
            }
            // An unreadable file is not missing: it is unproven, which already makes the result uncertain.
            else if (!unknown.Contains(file.Path) && !UnreadableNow(file.Path)) differences.Add(new SnapshotDifference(file.Path, SnapshotChange.Missing));
        }
        var before = expected.Files.Select(file => file.Path).Concat(expected.Unverified).ToHashSet(StringComparer.OrdinalIgnoreCase);
        differences.AddRange(actual.Files.Where(file => !before.Contains(file.Path) && !UnreadableBefore(file.Path)).Select(file => new SnapshotDifference(file.Path, SnapshotChange.Added)));
        var foldersBefore = expected.Directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var foldersNow = actual.Directories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        differences.AddRange(expected.Directories.Where(folder => !foldersNow.Contains(folder) && !UnreadableNow(folder)).Select(folder => new SnapshotDifference(folder, SnapshotChange.MissingFolder)));
        differences.AddRange(actual.Directories.Where(folder => !foldersBefore.Contains(folder) && !UnreadableBefore(folder)).Select(folder => new SnapshotDifference(folder, SnapshotChange.AddedFolder)));
        return new SnapshotVerification(differences.OrderBy(difference => difference.Path, StringComparer.OrdinalIgnoreCase).ToArray(), expected.Complete && actual.Complete);
    }

    /// <summary>Strictly inside one of the folders ("." being the root): the folder entry itself was listed by its parent.</summary>
    private static bool Under(string path, IReadOnlyList<string> folders) => folders.Any(folder =>
        folder == "." || path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    /// <summary>Writes a new snapshot file outside the folder it describes; an existing file is never replaced.</summary>
    public static void Save(FolderSnapshot snapshot, string path)
    {
        using var output = OutputFiles.CreateOutside(path, [snapshot.Root], OutsideMessage);
        JsonSerializer.Serialize(output, snapshot, new JsonSerializerOptions { WriteIndented = true });
        output.Flush(true);
    }

    /// <summary>The checks of <see cref="Save"/>, made before hours of reading rather than after.</summary>
    public static void ValidateDestination(string root, string path) => OutputFiles.EnsureOutside(path, [root], OutsideMessage);

    private static string OutsideMessage => L.T("Enregistrez l’instantané à l’extérieur du dossier qu’il décrit.", "Save the snapshot outside the folder it describes.");

    public static FolderSnapshot Load(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 512L * 1024 * 1024) throw new InvalidDataException(L.T("Instantané trop volumineux.", "The snapshot is too large."));
        var snapshot = JsonSerializer.Deserialize<FolderSnapshot>(input);
        static bool Relative(string? value) => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) &&
            !value.Split(['/', '\\']).Any(part => part is "" or "." or ".." || part.Contains(':'));
        if (snapshot is not { Schema: 1, Algorithm: "SHA256" } || snapshot.Files is null || snapshot.Directories is null ||
            snapshot.Files.Any(file => file is null || !Relative(file.Path) || file.Size < 0 || file.Sha256 is not { Length: 64 } || !file.Sha256.All(Uri.IsHexDigit)) ||
            snapshot.Directories.Any(folder => !Relative(folder)) ||
            (snapshot.UnreadableFolders ?? []).Any(folder => folder != "." && !Relative(folder)))
            throw new InvalidDataException(L.T("Ce fichier n’est pas un instantané Checker San valide.", "This file is not a valid Checker San snapshot."));
        snapshot = snapshot with { Links = snapshot.Links ?? [], Unverified = snapshot.Unverified ?? [], Errors = snapshot.Errors ?? [], UnreadableFolders = snapshot.UnreadableFolders ?? [] };
        // A snapshot made by this engine lists each path once, and is complete only with nothing left unproven.
        var paths = snapshot.Files.Select(file => file.Path).Concat(snapshot.Directories).Concat(snapshot.Links).Concat(snapshot.Unverified).ToList();
        if (snapshot.Links.Concat(snapshot.Unverified).Any(item => !Relative(item)) ||
            paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Count ||
            (snapshot.Complete && (snapshot.Unverified.Count > 0 || snapshot.Errors.Count > 0)))
            throw new InvalidDataException(L.T("Cet instantané se contredit (chemins en double, ou « complet » malgré des fichiers non vérifiés) : il n’est pas utilisé.", "This snapshot contradicts itself (repeated paths, or ‘complete’ despite unverified files): it is not used."));
        return snapshot;
    }

    private static bool IsFileError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException;
}
