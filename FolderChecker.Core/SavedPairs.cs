using System.Globalization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FolderChecker.Core;

public enum ScheduleFrequency { Daily, Weekly }

/// <summary>When Windows checks a saved pair: every day, or one day a week, at a local time.</summary>
public sealed record PairSchedule(ScheduleFrequency Frequency, DayOfWeek Day, TimeOnly Time);

/// <summary>
/// Outcome of the last check of a pair. Counts are kept rather than text, so it reads in either language.
/// Links counts the items behind links or junctions, which are never compared.
/// </summary>
public sealed record PairRun(DateTimeOffset At, int ExitCode, int Differences, int Unverified, int Errors, string? ReportPath, string? Failure = null, int Links = 0);

/// <summary>Two folders checked together again and again, for example a folder and its backup.</summary>
public sealed record SavedPair
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Folder1 { get; init; } = "";
    public string Folder2 { get; init; } = "";
    public bool FindMovedFiles { get; init; } = true;
    public PairSchedule? Schedule { get; init; }
    public PairRun? LastRun { get; init; }

    public string ScheduleText => Schedule is not { } schedule ? L.T("Non planifiée", "Not scheduled")
        : schedule.Frequency == ScheduleFrequency.Daily ? L.F($"Tous les jours à {schedule.Time:HH\\:mm}", $"Every day at {schedule.Time:HH\\:mm}")
        : L.F($"Chaque {L.Culture.DateTimeFormat.GetDayName(schedule.Day)} à {schedule.Time:HH\\:mm}", $"Every {L.Culture.DateTimeFormat.GetDayName(schedule.Day)} at {schedule.Time:HH\\:mm}");

    public string LastRunText => LastRun is not { } run ? L.T("Jamais vérifiée", "Never checked")
        : run.At.ToLocalTime().ToString("g", L.Culture) + " — " + run.ExitCode switch
        {
            ExitCodes.Clean => L.T("identiques", "identical"),
            ExitCodes.Differences => L.F($"{run.Differences:N0} écart(s)", $"{run.Differences:N0} difference(s)"),
            ExitCodes.Incomplete => L.T("incomplète", "incomplete") + IncompleteReasons(run),
            _ => L.T("impossible", "failed") + (run.Failure is null ? "" : " : " + run.Failure)
        };

    private static string IncompleteReasons(PairRun run)
    {
        var reasons = new List<string>();
        if (run.Errors > 0) reasons.Add(L.F($"{run.Errors:N0} erreur(s)", $"{run.Errors:N0} error(s)"));
        if (run.Unverified > 0) reasons.Add(L.F($"{run.Unverified:N0} non vérifié(s)", $"{run.Unverified:N0} not verified"));
        if (run.Links > 0) reasons.Add(L.F($"{run.Links:N0} élément(s) derrière des liens non suivis", $"{run.Links:N0} item(s) behind links not followed"));
        return reasons.Count == 0 ? "" : " (" + string.Join(", ", reasons) + ")";
    }
}

/// <summary>
/// Saved pairs in one JSON file. Every change rereads the file under a lock shared by the app, the command line
/// and scheduled checks, then replaces it in one step. A damaged file is reported and never overwritten.
/// </summary>
public static class PairStore
{
    private sealed record StoreFile(int Schema, List<SavedPair>? Pairs);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CheckerSan", "pairs.json");

    /// <summary>Reports of saved-pair checks, beside the store, one folder per pair.</summary>
    public static string ReportsFolder(string storePath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(storePath))!, "Reports");

    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    /// <summary>Eight hexadecimal digits: safe in a task name and on a command line.</summary>
    public static bool IsId(string? id) => id is { Length: 8 } && id.All(char.IsAsciiHexDigit);

    public static IReadOnlyList<SavedPair> Load(string? path = null)
    {
        path = Path.GetFullPath(path ?? DefaultPath);
        using (Lock(path)) return Read(path);
    }

    /// <summary>The pair with this id, otherwise the one with this name, ignoring case.</summary>
    public static SavedPair? Find(string idOrName, string? path = null)
    {
        var pairs = Load(path);
        return pairs.FirstOrDefault(pair => string.Equals(pair.Id, idOrName, StringComparison.OrdinalIgnoreCase))
            ?? pairs.FirstOrDefault(pair => string.Equals(pair.Name, idOrName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds a pair, or replaces the one with the same id. A pair without an id gets a new one. Names are unique, ignoring case.</summary>
    public static SavedPair Save(SavedPair pair, string? path = null)
    {
        pair = pair with { Id = pair.Id.Length == 0 ? NewId() : pair.Id, Name = pair.Name.Trim(), Folder1 = pair.Folder1.Trim().Trim('"'), Folder2 = pair.Folder2.Trim().Trim('"') };
        if (pair.Name.Length is 0 or > 80) throw new ArgumentException(L.T("Donnez un nom à la paire (80 caractères au plus).", "Give the pair a name (80 characters at most)."));
        if (pair.Folder1.Length == 0 || pair.Folder2.Length == 0) throw new ArgumentException(L.T("Choisissez les deux dossiers de la paire.", "Choose both folders of the pair."));
        // A scheduled task starts in another folder than the app: a relative path would point elsewhere.
        if (!Path.IsPathFullyQualified(pair.Folder1) || !Path.IsPathFullyQualified(pair.Folder2))
            throw new ArgumentException(L.T("Donnez le chemin complet des deux dossiers (par exemple D:\\Photos ou \\\\PC\\Partage).", "Give the full path of both folders (for example D:\\Photos or \\\\PC\\Share)."));
        if (!IsId(pair.Id)) throw new ArgumentException("Invalid pair id.");
        Update(path, pairs =>
        {
            if (pairs.Any(other => other.Id != pair.Id && string.Equals(other.Name, pair.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(L.F($"Une autre paire s’appelle déjà « {pair.Name} ».", $"Another pair is already called “{pair.Name}”."));
            var index = pairs.FindIndex(other => other.Id == pair.Id);
            if (index >= 0) pairs[index] = pair; else pairs.Add(pair);
        });
        return pair;
    }

    public static void Remove(string id, string? path = null) => Update(path, pairs => pairs.RemoveAll(pair => pair.Id == id));

    /// <summary>
    /// Changes one pair as it is now in the file, under the lock: a result recorded meanwhile by a scheduled check
    /// is kept, unlike saving a copy read earlier. Returns the pair as saved, or null if it no longer exists.
    /// </summary>
    public static SavedPair? Modify(string id, Func<SavedPair, SavedPair> change, string? path = null)
    {
        SavedPair? changed = null;
        Update(path, pairs =>
        {
            var index = pairs.FindIndex(pair => pair.Id == id);
            if (index >= 0) pairs[index] = changed = change(pairs[index]) with { Id = id };
        });
        return changed;
    }

    /// <summary>Records a check without touching the rest of the pair, which may have been edited meanwhile.</summary>
    public static void RecordRun(string id, PairRun run, string? path = null) => Update(path, pairs =>
    {
        var index = pairs.FindIndex(pair => pair.Id == id);
        if (index >= 0) pairs[index] = pairs[index] with { LastRun = run };
    });

    private static void Update(string? path, Action<List<SavedPair>> change)
    {
        path = Path.GetFullPath(path ?? DefaultPath);
        using var gate = Lock(path);
        var pairs = Read(path).ToList();
        change(pairs);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, new StoreFile(1, pairs), Json);
                output.Flush(true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static IReadOnlyList<SavedPair> Read(string path)
    {
        if (!File.Exists(path)) return [];
        StoreFile? file = null;
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (input.Length <= 16 * 1024 * 1024)
                try { file = JsonSerializer.Deserialize<StoreFile>(input, Json); }
                catch (JsonException) { }
        }
        if (file is not { Schema: 1, Pairs: { } pairs } ||
            pairs.Any(pair => pair is null || !IsId(pair.Id) || string.IsNullOrWhiteSpace(pair.Name) || string.IsNullOrWhiteSpace(pair.Folder1) || string.IsNullOrWhiteSpace(pair.Folder2)) ||
            pairs.Select(pair => pair.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pairs.Count)
            throw new InvalidDataException(L.F($"Le fichier des paires est endommagé ; il n’a pas été modifié : {path}", $"The saved pairs file is damaged and was left unchanged: {path}"));
        return pairs;
    }

    /// <summary>One lock per store file for every process of the session. The file is replaced in one step, so an abandoned lock leaves it intact.</summary>
    private static Releaser Lock(string path)
    {
        // Named after the real location, so that two spellings of one file (junction, mapped drive) share the lock.
        var parent = Path.GetDirectoryName(path)!;
        var real = path;
        try { if (Directory.Exists(parent)) real = Path.Combine(FileSafety.CanonicalDirectory(parent), Path.GetFileName(path)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        var mutex = new Mutex(false, @"Local\CheckerSan-pairs-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(real.ToUpperInvariant())))[..16]);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
                throw new IOException(L.T("Le fichier des paires est occupé ; réessayez.", "The saved pairs file is busy; try again."));
        }
        catch (AbandonedMutexException) { }
        catch { mutex.Dispose(); throw; }
        return new Releaser(mutex);
    }

    private sealed class Releaser(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}

/// <summary>
/// Checks a saved pair as the scheduler does: a read-only comparison, a compact JSON report beside the store,
/// and the outcome recorded with the pair and in the activity log.
/// </summary>
public static partial class PairCheck
{
    /// <summary>Latest reports kept per pair; older ones, written by Checker San in its own folder, are removed.</summary>
    public const int ReportsKept = 20;

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{6}(-\d{1,2})?\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex ReportName();

    public static async Task<PairRun> RunAsync(SavedPair pair, string? storePath = null, int? parallelReads = null, string? log = null,
        IProgress<ComparisonProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        storePath = Path.GetFullPath(storePath ?? PairStore.DefaultPath);
        PairRun run;
        try
        {
            var result = await new ComparisonEngine().CompareAsync(pair.Folder1, pair.Folder2,
                new ComparisonOptions(pair.FindMovedFiles) { ParallelReads = parallelReads }, progress, cancellationToken);
            var code = ExitCodes.ForComparison(result);
            string? report = null, failure = null;
            try { report = WriteReport(pair, result, code, storePath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { failure = L.T("rapport non enregistré : ", "report not saved: ") + exception.Message; }
            run = new PairRun(DateTimeOffset.Now, code, ExitCodes.DifferenceCount(result), result.Rows.Count(row => row.Status == ComparisonStatus.Unverified),
                result.Errors.Count, report, failure, ExitCodes.LinkCount(result));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            run = new PairRun(DateTimeOffset.Now, ExitCodes.Failure, 0, 0, 1, null, exception.Message);
        }
        try { PairStore.RecordRun(pair.Id, run, storePath); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { }
        ActivityLog.Write(L.T("Paire", "Pair"), $"{pair.Name} · {pair.Folder1} ↔ {pair.Folder2} · exit {run.ExitCode} · {run.Differences} difference(s) · {run.Errors} error(s)" +
            (run.Failure is null ? "" : " · " + run.Failure), log);
        return run;
    }

    private static string WriteReport(SavedPair pair, ComparisonResult result, int code, string storePath)
    {
        // The folder actually written, Reports\<id>, may not exist yet: resolve its nearest existing ancestor,
        // so that a junction anywhere on the way is followed before comparing with the roots.
        var folder = Path.Combine(PairStore.ReportsFolder(storePath), pair.Id);
        var existing = folder;
        while (!Directory.Exists(existing)) existing = Path.GetDirectoryName(existing)!;
        var canonicalFolder = Path.GetFullPath(Path.Combine(FileSafety.CanonicalDirectory(existing), Path.GetRelativePath(existing, folder)));
        foreach (var root in new[] { result.SourceRoot, result.TargetRoot })
            if (FileSafety.IsWithin(root, folder) || FileSafety.IsWithin(FileSafety.CanonicalDirectory(root), canonicalFolder))
                throw new IOException(L.T("le dossier des rapports se trouve dans un dossier comparé.", "the reports folder is inside a compared folder."));
        Directory.CreateDirectory(folder);
        // Gregorian digits whatever the Windows calendar, so names sort by date for every process.
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        // Identical files and common folders are only counted, so a daily report of a large folder stays small.
        var report = new
        {
            Application = "Checker San", Pair = pair.Name, pair.Folder1, pair.Folder2, ExitCode = code,
            result.StartedAt, result.CompletedAt, result.Cancelled, result.SnapshotVerified, result.TreesIdentical,
            result.SourceFullyContained, result.TargetFullyContained, result.SourceFileCount, result.TargetFileCount,
            Identical = result.Rows.Count(row => row.Status == ComparisonStatus.Identical),
            Rows = result.Rows.Where(row => row.Status is not (ComparisonStatus.Identical or ComparisonStatus.CommonDirectory)),
            result.Errors, result.Warnings
        };
        // Two checks of a pair within one second, from the app and the command line, each keep their report.
        string path;
        for (var attempt = 1; ; attempt++)
        {
            path = Path.Combine(folder, stamp + (attempt == 1 ? "" : $"-{attempt}") + ".json");
            try
            {
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(output, report, options);
                break;
            }
            catch (IOException) when (attempt < 10 && File.Exists(path)) { }
        }
        // Only names exactly like ours, never the report just written, and the newest are kept.
        foreach (var old in Directory.GetFiles(folder, "*.json")
                     .Where(file => ReportName().IsMatch(Path.GetFileName(file)) && !string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                     .OrderDescending(StringComparer.Ordinal).Skip(ReportsKept - 1))
            File.Delete(old);
        return path;
    }
}
