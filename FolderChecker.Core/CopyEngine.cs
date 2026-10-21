using System.Security.Cryptography;
using System.Text.Json;

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
    public string PartialName { get; set; } = ".checkersan-" + Guid.NewGuid().ToString("N") + ".partial";
    public bool Completed { get; set; }
    public string? VerifiedHash { get; set; }
    public string? SourceIdentity { get; set; }
    public string? PartialIdentity { get; set; }
    public string? DestinationIdentity { get; set; }
}

/// <summary>Copies only explicitly planned missing paths. Never overwrites a final destination.</summary>
public sealed class CopyEngine
{
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
        if (comparison.Cancelled || !comparison.SnapshotVerified || comparison.Errors.Count > 0 ||
            comparison.Rows.Any(row => row.Status == ComparisonStatus.Unverified))
            throw new InvalidOperationException("Terminez une comparaison sans erreur avant de préparer une copie.");
    }

    private static void AddMissingItems(CopyPlan plan, ComparisonResult comparison, bool sourceToTarget, bool reverseDirection)
    {
        var source = reverseDirection ? plan.TargetRoot : plan.SourceRoot;
        var blocked = comparison.Rows
            .Where(r => r.Status is ComparisonStatus.Conflict or ComparisonStatus.Unverified)
            .Select(r => r.RelativePath.Replace('/', '\\').TrimEnd('\\') + "\\")
            .ToArray();
        foreach (var row in comparison.Rows.Where(r => sourceToTarget
                     ? r.Status is ComparisonStatus.OnlySource or ComparisonStatus.Moved
                     : r.Status == ComparisonStatus.OnlyTarget))
        {
            // An absent descendant cannot be created below a file/directory conflict.
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
                item.SourceIdentity = IdentityText(FileSafety.Identity(file.SafeFileHandle));
            }
            plan.Items.Add(item);
        }
    }

    private static void FinishPlan(CopyPlan plan)
    {
        plan.Items = plan.Items.OrderBy(i => i.ReverseDirection)
            .ThenBy(i => i.Kind == EntryKind.Directory ? 0 : 1)
            .ThenBy(i => i.RelativePath.Length).ToList();
        ValidatePlanStructure(plan);
    }
    public static CopyPlan? ReadPlan(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length > 128L * 1024 * 1024) return null;
            var plan = JsonSerializer.Deserialize<CopyPlan>(file);
            if (plan is null) return null;
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
        void Report(string phase, string path, int retry = 0, bool force = false)
        {
            if (!force && retry == 0 && timer.ElapsedMilliseconds < 100) return;
            timer.Restart(); progress?.Report(new(phase, path, completed, totalFiles, written, resumed, retry));
        }
        Task<T> Retry<T>(Func<Task<T>> action, string path) => FileSystemRetryPolicy.ExecuteAsync(_ => action(), 3, 1000,
            (attempt, delay) => Report($"Reconnexion dans {delay.TotalSeconds:0} s", path, attempt, true), ct);
        try
        {
            ValidatePlanStructure(plan);
            totalFiles = plan.FileCount;
            await Retry(() => { ValidateRoots(plan); return Task.FromResult(true); }, plan.TargetRoot);
            ValidateCheckpoint(plan, checkpointPath);
            SavePlan(plan, checkpointPath);
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
                    if (!ValidPartialName(item.PartialName)) throw new ArgumentException("Nom de fichier temporaire invalide.");
                    await Retry(async () =>
                    {
                        ValidateRoots(plan);
                        var source = SafePath(sourceRoot, item.RelativePath);
                        var target = SafePath(targetRoot, item.RelativePath);
                        AssertNoLinks(sourceRoot, source);
                        if (item.Kind == EntryKind.Directory)
                        {
                            if ((File.GetAttributes(source) & FileAttributes.Directory) == 0) throw new IOException("Le dossier source a changé de type.");
                            EnsureDirectory(targetRoot, target);
                            item.Completed = true; SavePlan(plan, checkpointPath);
                            return true;
                        }
                        EnsureDirectory(targetRoot, Path.GetDirectoryName(target)!);
                        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        ValidateSource(item, input);
                        if (Exists(target))
                        {
                            AssertNoLinks(targetRoot, target);
                            if (item.VerifiedHash is null) throw new IOException("La destination existe déjà : elle n’a pas été remplacée.");
                            using var existing = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous);
                            var identity = IdentityText(FileSafety.Identity(existing.SafeFileHandle));
                            if (!(item.Completed && identity is not null && identity == item.DestinationIdentity && existing.Length == item.Size))
                            {
                                var hash = Convert.ToHexString(await SHA256.HashDataAsync(existing, ct));
                                if (existing.Length != item.Size || hash != item.VerifiedHash) throw new IOException("La destination existante a un contenu différent : aucun écrasement.");
                            }
                            item.DestinationIdentity = identity; item.Completed = true;
                            SavePlan(plan, checkpointPath);
                            return true;
                        }
                        item.Completed = false;
                        var partial = Path.Combine(Path.GetDirectoryName(target)!, item.PartialName);
                        AssertNoLinks(targetRoot, partial);
                        var existed = Exists(partial);
                        using (var output = new FileStream(partial, existed ? FileMode.Open : FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            var partialId = IdentityText(FileSafety.Identity(output.SafeFileHandle), includeChangeTime: false);
                            if (existed && (item.PartialIdentity is null || partialId != item.PartialIdentity))
                                throw new IOException("Le fichier temporaire ne peut plus être identifié. Conservez-le et démarrez une nouvelle copie depuis un nouveau scan.");
                            item.PartialIdentity = partialId;
                            SavePlan(plan, checkpointPath);
                            using var sourceHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            byte[] buffer = new byte[1024 * 1024], prior = new byte[buffer.Length];
                            long offset = output.Length;
                            if (offset > input.Length) throw new IOException("Le fichier temporaire a une taille incohérente.");
                            long checkedBytes = 0;
                            Report("Vérification de la partie déjà copiée", displayPath, force: true);
                            while (checkedBytes < offset)
                            {
                                ct.ThrowIfCancellationRequested();
                                int wanted = (int)Math.Min(buffer.Length, offset - checkedBytes);
                                await input.ReadExactlyAsync(buffer.AsMemory(0, wanted), ct);
                                await output.ReadExactlyAsync(prior.AsMemory(0, wanted), ct);
                                if (!buffer.AsSpan(0, wanted).SequenceEqual(prior.AsSpan(0, wanted)))
                                    throw new IOException("La partie déjà copiée diffère de la source. Aucun contenu existant n’a été écrasé ; relancez une nouvelle copie après comparaison.");
                                sourceHasher.AppendData(buffer, 0, wanted); checkedBytes += wanted;
                                Report("Vérification de la partie déjà copiée", displayPath);
                            }
                            resumed += offset;
                            Report("Copie", displayPath, force: true);
                            int read;
                            while ((read = await input.ReadAsync(buffer, ct)) != 0)
                            {
                                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                                sourceHasher.AppendData(buffer, 0, read); written += read;
                                Report("Copie", displayPath, force: true);
                            }
                            await output.FlushAsync(ct); output.Flush(true);
                            ValidateSource(item, input);
                            var expectedHash = Convert.ToHexString(sourceHasher.GetHashAndReset());
                            output.Position = 0;
                            Report("Vérification SHA-256 de la copie", displayPath, force: true);
                            var destinationHash = Convert.ToHexString(await SHA256.HashDataAsync(output, ct));
                            if (output.Length != item.Size || destinationHash != expectedHash) throw new IOException("La vérification SHA-256 de la copie a échoué.");
                            item.VerifiedHash = expectedHash;
                            SavePlan(plan, checkpointPath);
                        }
                        ct.ThrowIfCancellationRequested();
                        AssertNoLinks(targetRoot, Path.GetDirectoryName(target)!);
                        // Both handles have been validated; final move is no-overwrite and on the same volume/share.
                        File.Move(partial, target, overwrite: false);
                        using (var final = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                            item.DestinationIdentity = IdentityText(FileSafety.Identity(final.SafeFileHandle));
                        item.Completed = true; SavePlan(plan, checkpointPath);
                        return true;
                    }, displayPath);
                    if (item.Kind == EntryKind.File) completed++;
                    Report("Élément terminé", displayPath, force: true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (FileSystemRetryPolicy.IsTransient(ex)) { errors.Add($"{displayPath} : connexion indisponible après trois tentatives. {ex.Message}"); break; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
                { errors.Add($"{displayPath} : {ex.Message}"); }
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
            throw new IOException("La source a changé depuis la préparation. Relancez une comparaison avant de copier cette nouvelle version.");
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
            throw new ArgumentException("Journal de copie invalide ou version non prise en charge.");
        if (plan.Schema == 1 && plan.Bidirectional)
            throw new ArgumentException("Un ancien journal ne peut pas contenir une copie bidirectionnelle.");
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var partials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var item in plan.Items)
        {
            if (item is null || item.Size < 0 || !Enum.IsDefined(item.Kind) ||
                (item.Kind == EntryKind.Directory && item.Size != 0) || !ValidPartialName(item.PartialName))
                throw new ArgumentException("Un élément du journal de copie est invalide.");
            if (item.ReverseDirection && (!plan.Bidirectional || plan.Schema == 1))
                throw new ArgumentException("Sens de copie ambigu dans le journal.");
            if (item.VerifiedHash is not null && (item.VerifiedHash.Length != 64 || !item.VerifiedHash.All(Uri.IsHexDigit)))
                throw new ArgumentException("Empreinte SHA-256 invalide dans le journal.");
            SafePath(plan.SourceRoot, item.RelativePath);
            SafePath(plan.TargetRoot, item.RelativePath);
            var target = SafePath(item.ReverseDirection ? plan.SourceRoot : plan.TargetRoot, item.RelativePath);
            if (!destinations.Add(target))
                throw new ArgumentException("Le journal contient plusieurs opérations pour une même destination.");
            if (item.Kind == EntryKind.File)
            {
                var partial = Path.Combine(Path.GetDirectoryName(target)!, item.PartialName);
                if (!partials.Add(partial))
                    throw new ArgumentException("Le journal partage un fichier temporaire entre plusieurs copies.");
                if (item.Size > long.MaxValue - totalBytes)
                    throw new ArgumentException("La taille totale du journal est invalide.");
                totalBytes += item.Size;
            }
        }
        if (partials.Overlaps(destinations))
            throw new ArgumentException("Un fichier temporaire entre en conflit avec une destination prévue.");
    }
    private static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split(['/', '\\']).Any(s => s is "" or "." or ".."))
            throw new ArgumentException("Chemin relatif de copie invalide.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!FileSafety.IsWithin(root, path)) throw new ArgumentException("Le chemin de copie sort du dossier choisi.");
        return path;
    }
    private static void ValidateRoots(CopyPlan plan)
    {
        if ((File.GetAttributes(plan.SourceRoot) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != FileAttributes.Directory ||
            (File.GetAttributes(plan.TargetRoot) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != FileAttributes.Directory)
            throw new IOException("Les racines doivent être des dossiers réels accessibles.");
        var source = FileSafety.CanonicalDirectory(plan.SourceRoot); var target = FileSafety.CanonicalDirectory(plan.TargetRoot);
        if (!source.Equals(plan.SourceCanonical, StringComparison.OrdinalIgnoreCase) || !target.Equals(plan.TargetCanonical, StringComparison.OrdinalIgnoreCase)) throw new IOException("L’emplacement réel d’un dossier a changé.");
        if (FileSafety.IsWithin(source, target) || FileSafety.IsWithin(target, source)) throw new ArgumentException("Les dossiers de copie doivent être distincts et non imbriqués.");
    }
    private static void AssertNoLinks(string root, string path)
    {
        if (!FileSafety.IsWithin(root, path)) throw new IOException("Chemin hors du dossier choisi.");
        var current = root;
        foreach (var part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            if (!Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Un lien ou une jonction bloque la copie : " + current);
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
            if ((File.GetAttributes(current) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory) throw new IOException("La destination contient un fichier ou un lien à la place d’un dossier : " + current);
        }
    }
    private static void ValidateCheckpoint(CopyPlan plan, string path)
    {
        path = Path.GetFullPath(path);
        if (FileSafety.IsWithin(plan.SourceRoot, path) || FileSafety.IsWithin(plan.TargetRoot, path)) throw new IOException("Le journal de copie doit être situé hors des dossiers comparés.");
        var parent = Path.GetDirectoryName(path)!;
        var ancestor = parent;
        while (!Directory.Exists(ancestor)) ancestor = Path.GetDirectoryName(ancestor) ?? throw new IOException("Journal inaccessible.");
        var canonical = Path.Combine(FileSafety.CanonicalDirectory(ancestor), Path.GetRelativePath(ancestor, parent), Path.GetFileName(path));
        if (FileSafety.IsWithin(plan.SourceCanonical, canonical) || FileSafety.IsWithin(plan.TargetCanonical, canonical)) throw new IOException("Le journal de copie pointe dans un dossier comparé.");
    }
    private static void SavePlan(CopyPlan plan, string path)
    {
        ValidateCheckpoint(plan, path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, plan); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
