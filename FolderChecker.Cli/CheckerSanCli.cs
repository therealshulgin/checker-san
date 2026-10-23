using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FolderChecker.Core;

namespace FolderChecker.Cli;

/// <summary>
/// Command line for scheduled or scripted checks. Exit codes are stable: 0 nothing to report (identical, no
/// duplicates, complete snapshot), 1 proven differences or duplicates, 2 incomplete verification (errors,
/// unreadable or online-only files, real items facing links, changes during the scan, Ctrl+C), 3 invalid
/// command or fatal error.
/// Nothing is ever copied, moved or deleted from the command line: simulations only list what would be.
/// </summary>
public static class CheckerSanCli
{
    public const int Clean = ExitCodes.Clean, Differences = ExitCodes.Differences, Incomplete = ExitCodes.Incomplete, Failure = ExitCodes.Failure;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        // The display language is process-wide: a caller hosting the command line keeps its own afterwards.
        var previous = L.Language;
        try { return await RunCoreAsync(args, output, error, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C: nothing was proven and nothing was saved, which is an incomplete verification.
            output.WriteLine(L.T("INCOMPLET — arrêté avant la fin : rien n’a été vérifié jusqu’au bout ni enregistré.", "INCOMPLETE — stopped before the end: nothing was fully verified or saved."));
            ActivityLog.Write("CLI", string.Join(' ', args) + " → cancelled", LogOption(args));
            return Incomplete;
        }
        catch (Exception exception)
        {
            // Last resort: whatever happens, the exit code stays one of the four documented ones.
            error.WriteLine(L.T("Erreur : ", "Error: ") + exception.Message);
            return Failure;
        }
        finally { L.SetLanguage(previous); }
    }

    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase) { "--no-moved", "--include-hidden", "--help", "-h", "/?" };
    private static readonly HashSet<string> CommonOptions = new(StringComparer.OrdinalIgnoreCase) { "lang", "log", "readers", "pairs", "help", "h", "?" };
    private static readonly Dictionary<string, string[]> CommandOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["compare"] = ["json", "no-moved", "simulate-copy"],
        ["snapshot"] = [],
        ["verify"] = ["json"],
        ["duplicates"] = ["json", "min-size", "exclude", "include-hidden", "simulate-keep", "keep-in"],
        ["pairs"] = [],
        ["run"] = []
    };

    private static string? LogOption(string[] args) =>
        Array.FindIndex(args, arg => string.Equals(arg, "--log", StringComparison.OrdinalIgnoreCase)) is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;

    private static async Task<int> RunCoreAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (Flags.Contains(args[i])) options[args[i].TrimStart('-', '/')] = null;
            else if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                // A flag or another option is never taken as a value: "--json --no-moved" lacks a file name.
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) return Usage(error, $"Missing value for {args[i]}.");
                options[args[i][2..]] = args[++i];
            }
            else positional.Add(args[i]);
        }
        var language = options.GetValueOrDefault("lang") ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en");
        if (language is not ("fr" or "en")) return Usage(error, "--lang accepts en or fr.");
        L.SetLanguage(language);
        if (positional.Count == 0 || options.ContainsKey("help") || options.ContainsKey("h") || options.ContainsKey("?")) { output.WriteLine(Help()); return positional.Count == 0 ? Failure : Clean; }
        // A misspelt option would silently change nothing in a scheduled script: refuse it before any scan.
        if (CommandOptions.TryGetValue(positional[0], out var specific))
            foreach (var name in options.Keys)
                if (!CommonOptions.Contains(name) && !specific.Contains(name, StringComparer.OrdinalIgnoreCase))
                    return Usage(error, L.F($"Option inconnue pour « {positional[0]} » : --{name}", $"Unknown option for ‘{positional[0]}’: --{name}"));
        if (options.TryGetValue("log", out var logText) && string.IsNullOrWhiteSpace(logText)) return Usage(error, L.T("--log attend un chemin de fichier.", "--log needs a file path."));
        int? readers = null;
        if (options.TryGetValue("readers", out var readerText))
        {
            if (!int.TryParse(readerText, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 16) return Usage(error, "--readers accepts 1 to 16.");
            readers = count;
        }
        var log = options.GetValueOrDefault("log");
        var json = options.GetValueOrDefault("json");
        var store = options.GetValueOrDefault("pairs");
        var simulateCopy = options.GetValueOrDefault("simulate-copy")?.ToLowerInvariant();
        if (simulateCopy is not (null or "both" or "1to2" or "2to1")) return Usage(error, "--simulate-copy accepts both, 1to2 or 2to1.");
        try
        {
            return (positional[0].ToLowerInvariant(), positional.Count) switch
            {
                ("compare", 3) => await CompareAsync(positional[1], positional[2], !options.ContainsKey("no-moved"), readers, json, log, simulateCopy, output, cancellationToken),
                ("snapshot", 3) => await SnapshotAsync(positional[1], positional[2], readers, log, output, cancellationToken),
                ("verify", 3) => await VerifyAsync(positional[1], positional[2], readers, json, log, output, cancellationToken),
                ("duplicates", 2) => await DuplicatesAsync(positional[1], options, readers, json, log, output, cancellationToken),
                ("pairs", 1) => Pairs(store, output),
                ("run", 2) => await RunPairAsync(positional[1], store, readers, log, output, error, cancellationToken),
                _ => Usage(error, L.T("Commande ou nombre d’arguments incorrect.", "Unknown command or wrong number of arguments."))
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error.WriteLine(L.T("Erreur : ", "Error: ") + exception.Message);
            ActivityLog.Write("CLI", string.Join(' ', args) + " → " + exception.Message, log);
            return Failure;
        }
    }

    private static async Task<int> CompareAsync(string first, string second, bool moved, int? readers, string? json, string? log,
        string? simulateCopy, TextWriter output, CancellationToken token)
    {
        CheckJson(json, first, second);
        var result = await new ComparisonEngine().CompareAsync(first, second, new ComparisonOptions(moved) { ParallelReads = readers }, null, token);
        int Count(ComparisonStatus status) => result.Rows.Count(row => row.Status == status);
        var differences = ExitCodes.DifferenceCount(result);
        var code = ExitCodes.ForComparison(result);
        output.WriteLine(L.F($"Dossier 1 : {result.SourceRoot}", $"Folder 1: {result.SourceRoot}"));
        output.WriteLine(L.F($"Dossier 2 : {result.TargetRoot}", $"Folder 2: {result.TargetRoot}"));
        output.WriteLine(L.F($"Identiques : {Count(ComparisonStatus.Identical)} · absents du 2 : {Count(ComparisonStatus.OnlySource)} · absents du 1 : {Count(ComparisonStatus.OnlyTarget)} · différents : {Count(ComparisonStatus.Different) + Count(ComparisonStatus.Conflict)} · déplacés : {Count(ComparisonStatus.Moved)} · non vérifiés : {Count(ComparisonStatus.Unverified)} · liens non suivis : {Count(ComparisonStatus.Skipped)}",
            $"Identical: {Count(ComparisonStatus.Identical)} · missing from 2: {Count(ComparisonStatus.OnlySource)} · missing from 1: {Count(ComparisonStatus.OnlyTarget)} · different: {Count(ComparisonStatus.Different) + Count(ComparisonStatus.Conflict)} · moved: {Count(ComparisonStatus.Moved)} · not verified: {Count(ComparisonStatus.Unverified)} · links skipped: {Count(ComparisonStatus.Skipped)}"));
        if (!result.TreesIdentical && Count(ComparisonStatus.Skipped) > 0)
            output.WriteLine(L.T("! Des éléments réels font face à des liens ou jonctions non suivis : leur contenu n’est pas comparé.",
                "! Real items face links or junctions that are not followed: their contents are not compared."));
        output.WriteLine(Verdict(code, L.T("Les deux dossiers sont identiques.", "Both folders are identical."), L.T("Des différences sont prouvées.", "Differences are proven.")));
        foreach (var problem in result.Errors.Take(20)) output.WriteLine("! " + problem);
        var simulation = simulateCopy is null ? null : SimulateCopy(result, simulateCopy, output);
        if (json is not null) WriteJson(json, new { Application = "Checker San", Command = "compare", ExitCode = code, Result = result, CopySimulation = simulation }, result.SourceRoot, result.TargetRoot);
        ActivityLog.Write("CLI compare", $"{result.SourceRoot} ↔ {result.TargetRoot} · exit {code} · {differences} difference(s) · {result.Errors.Count} error(s)" +
            (simulateCopy is null ? "" : $" · copy simulated ({simulateCopy})"), log);
        return code;
    }

    /// <summary>Lists what « Complete folders » would copy, with the space it needs, without copying anything.</summary>
    private static object? SimulateCopy(ComparisonResult result, string direction, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine(L.T("SIMULATION — rien n’est copié.", "SIMULATION — nothing is copied."));
        if (!CopyEngine.CanCreatePlan(result))
        {
            output.WriteLine(L.T("Simulation impossible : la comparaison doit être complète et sans erreur.", "Cannot simulate: the comparison must be complete and without errors."));
            return null;
        }
        var plan = direction == "both" ? CopyEngine.CreateBidirectionalPlan(result) : CopyEngine.CreatePlan(result, direction == "1to2");
        var needs = CopyEngine.SpaceNeeds(plan);
        string Arrow(CopyItem item) => plan.Bidirectional ? item.ReverseDirection ? "2 → 1" : "1 → 2" : direction == "1to2" ? "1 → 2" : "2 → 1";
        foreach (var item in plan.Items.Take(200))
            output.WriteLine($"{L.T("COPIERAIT", "WOULD COPY")} {Arrow(item)}  {(item.Kind == EntryKind.Directory ? L.T("[dossier]", "[folder]") : L.Size(item.Size))}  {item.RelativePath}");
        if (plan.Items.Count > 200) output.WriteLine(L.F($"… et {plan.Items.Count - 200} autre(s).", $"… and {plan.Items.Count - 200} more."));
        output.WriteLine(L.F($"{plan.FileCount:N0} fichier(s) · {L.Size(plan.TotalBytes)} · {plan.Items.Count(item => item.Kind == EntryKind.Directory):N0} dossier(s)",
            $"{plan.FileCount:N0} file(s) · {L.Size(plan.TotalBytes)} · {plan.Items.Count(item => item.Kind == EntryKind.Directory):N0} folder(s)"));
        foreach (var need in needs.Where(need => need.Required > 0))
            output.WriteLine(L.F($"Espace libre sur {need.Root} : {L.Size(need.Available)} · nécessaire : {L.Size(need.Required)}{(need.Sufficient ? "" : " — INSUFFISANT")}",
                $"Free space on {need.Root}: {L.Size(need.Available)} · needed: {L.Size(need.Required)}{(need.Sufficient ? "" : " — NOT ENOUGH")}"));
        var conflicts = result.Rows.Count(row => row.Status is ComparisonStatus.Different or ComparisonStatus.Conflict);
        if (conflicts > 0) output.WriteLine(L.F($"{conflicts:N0} contenu(s) différent(s) ou conflit(s) ne seraient pas remplacés.", $"{conflicts:N0} different file(s) or conflict(s) would not be replaced."));
        return new
        {
            Direction = direction, plan.FileCount, plan.TotalBytes, Space = needs,
            Items = plan.Items.Select(item => new { Direction = Arrow(item), item.Kind, item.RelativePath, item.Size })
        };
    }

    private static async Task<int> SnapshotAsync(string folder, string file, int? readers, string? log, TextWriter output, CancellationToken token)
    {
        SnapshotEngine.ValidateDestination(folder, file);
        var snapshot = await new SnapshotEngine().CreateAsync(folder, readers, null, token);
        SnapshotEngine.Save(snapshot, file);
        var code = snapshot.Complete ? Clean : Incomplete;
        output.WriteLine(L.F($"Instantané : {snapshot.Files.Count} fichier(s), {snapshot.Directories.Count} dossier(s), {snapshot.Links.Count} lien(s) non suivi(s) → {Path.GetFullPath(file)}",
            $"Snapshot: {snapshot.Files.Count} file(s), {snapshot.Directories.Count} folder(s), {snapshot.Links.Count} link(s) skipped → {Path.GetFullPath(file)}"));
        foreach (var path in snapshot.Unverified.Take(20)) output.WriteLine(L.T("Non vérifié : ", "Not verified: ") + path);
        foreach (var problem in snapshot.Errors.Take(20)) output.WriteLine("! " + problem);
        output.WriteLine(Verdict(code, L.T("Instantané complet.", "Snapshot complete."), ""));
        ActivityLog.Write("CLI snapshot", $"{snapshot.Root} → {Path.GetFullPath(file)} · exit {code} · {snapshot.Files.Count} file(s)", log);
        return code;
    }

    private static async Task<int> VerifyAsync(string folder, string file, int? readers, string? json, string? log, TextWriter output, CancellationToken token)
    {
        var expected = SnapshotEngine.Load(file);
        CheckJson(json, folder);
        var actual = await new SnapshotEngine().CreateAsync(folder, readers, null, token);
        var verification = SnapshotEngine.Compare(expected, actual);
        // Same order as compare: an error leaves the result unproven, while a file merely not read (online
        // only) does not hide the differences that were proven.
        var errors = expected.Errors.Count + actual.Errors.Count;
        var code = errors > 0 ? Incomplete : verification.Differences.Count > 0 ? Differences : verification.Certain ? Clean : Incomplete;
        output.WriteLine(L.F($"Instantané du {expected.CreatedAt.ToLocalTime():g} ({expected.Root}) comparé à {actual.Root}", $"Snapshot of {expected.CreatedAt.ToLocalTime():g} ({expected.Root}) compared with {actual.Root}"));
        foreach (var difference in verification.Differences.Take(50)) output.WriteLine($"{Label(difference.Change)}  {difference.Path}");
        if (verification.Differences.Count > 50) output.WriteLine(L.F($"… et {verification.Differences.Count - 50} autre(s).", $"… and {verification.Differences.Count - 50} more."));
        foreach (var path in actual.Unverified.Take(20)) output.WriteLine(L.T("Non vérifié : ", "Not verified: ") + path);
        foreach (var problem in actual.Errors.Take(20)) output.WriteLine("! " + problem);
        if (expected.Errors.Count > 0)
            output.WriteLine(L.F($"! L’instantané lui-même est incomplet ({expected.Errors.Count} erreur(s) lors de sa création).", $"! The snapshot itself is incomplete ({expected.Errors.Count} error(s) when it was taken)."));
        output.WriteLine(Verdict(code, L.T("Le dossier correspond exactement à l’instantané.", "The folder matches the snapshot exactly."), L.F($"{verification.Differences.Count} différence(s) prouvée(s).", $"{verification.Differences.Count} proven difference(s).")));
        if (json is not null) WriteJson(json, new { Application = "Checker San", Command = "verify", ExitCode = code, verification.Certain, verification.Differences, actual.Unverified, actual.Errors }, actual.Root);
        ActivityLog.Write("CLI verify", $"{actual.Root} vs {Path.GetFullPath(file)} · exit {code} · {verification.Differences.Count} difference(s)", log);
        return code;
    }

    private static async Task<int> DuplicatesAsync(string folder, Dictionary<string, string?> options, int? readers, string? json, string? log, TextWriter output, CancellationToken token)
    {
        long minimum = 1;
        if (options.TryGetValue("min-size", out var text) && (!long.TryParse(text, CultureInfo.InvariantCulture, out minimum) || minimum < 0))
            throw new ArgumentException("--min-size accepts a number of bytes.");
        KeepRule? keep = options.GetValueOrDefault("simulate-keep")?.ToLowerInvariant() switch
        {
            null => null, "first" => KeepRule.FirstPath, "oldest" => KeepRule.Oldest, "newest" => KeepRule.Newest,
            "shortest" => KeepRule.ShortestPath, "folder" => KeepRule.InFolder,
            _ => throw new ArgumentException("--simulate-keep accepts first, oldest, newest, shortest or folder.")
        };
        var keepIn = options.GetValueOrDefault("keep-in");
        if (keep == KeepRule.InFolder && string.IsNullOrWhiteSpace(keepIn)) throw new ArgumentException("--simulate-keep folder needs --keep-in <folder>.");
        var exclude = options.TryGetValue("exclude", out var patterns)
            ? (patterns ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : DuplicateScanOptions.DefaultExcludedNames;
        CheckJson(json, folder);
        var result = await new DuplicateEngine().ScanAsync(folder, new DuplicateScanOptions
        {
            ResumeFromCheckpoint = false, MinimumSize = minimum, ExcludedNames = exclude,
            SkipHiddenAndSystem = !options.ContainsKey("include-hidden"), ParallelReads = readers
        }, null, token);
        var complete = !result.Cancelled && result.SnapshotVerified && result.Errors.Count == 0;
        var code = !complete ? Incomplete : result.Groups.Count > 0 ? Differences : Clean;
        var extra = result.Groups.Sum(group => group.Size * (group.Files.Count - 1));
        output.WriteLine(L.F($"{result.Groups.Count} groupe(s) de fichiers identiques · {L.Size(extra)} en double · {result.FilesScanned} fichier(s) analysé(s)",
            $"{result.Groups.Count} group(s) of identical files · {L.Size(extra)} duplicated · {result.FilesScanned} file(s) scanned"));
        foreach (var group in result.Groups.Take(20))
            output.WriteLine($"{group.Hash[..12]}  {L.Size(group.Size)}  " + string.Join("  |  ", group.Files.Select(file => file.RelativePath)));
        foreach (var problem in result.Errors.Take(20)) output.WriteLine("! " + problem);
        output.WriteLine(Verdict(code, L.T("Aucun doublon.", "No duplicates."), L.T("Des doublons sont confirmés par SHA-256. Rien n’a été retiré.", "Duplicates are confirmed by SHA-256. Nothing was removed.")));
        var simulation = keep is { } rule ? SimulateRemoval(result, rule, keepIn, complete, output) : null;
        if (json is not null) WriteJson(json, new { Application = "Checker San", Command = "duplicates", ExitCode = code, Result = result, RemovalSimulation = simulation }, result.Root);
        ActivityLog.Write("CLI duplicates", $"{result.Root} · exit {code} · {result.Groups.Count} group(s)" + (keep is null ? "" : $" · removal simulated ({keep})"), log);
        return code;
    }

    /// <summary>Applies a keep rule as the duplicate window would, and lists the copies it would remove, without removing any.</summary>
    private static object? SimulateRemoval(DuplicateResult result, KeepRule rule, string? keepIn, bool complete, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine(L.T("SIMULATION — rien n’est retiré.", "SIMULATION — nothing is removed."));
        if (!complete)
        {
            output.WriteLine(L.T("Simulation impossible : la recherche doit être complète et sans erreur.", "Cannot simulate: the search must be complete and without errors."));
            return null;
        }
        var plan = DuplicateSelection.Extras(result, rule, keepIn is null ? null : Path.GetFullPath(keepIn));
        var removed = plan.Remove.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shown = 0;
        foreach (var group in result.Groups.Where(group => group.Files.Any(file => removed.Contains(file.FullPath))))
        {
            if (shown++ == 50) { output.WriteLine(L.T("… (liste complète dans le rapport JSON)", "… (full list in the JSON report)")); break; }
            output.WriteLine($"{group.Hash[..12]}  {L.Size(group.Size)}");
            foreach (var file in group.Files)
                output.WriteLine((removed.Contains(file.FullPath) ? L.T("  RETIRERAIT  ", "  WOULD REMOVE  ") : L.T("  CONSERVERAIT ", "  WOULD KEEP    ")) + file.RelativePath);
        }
        var freed = result.Groups.SelectMany(group => group.Files).Where(file => removed.Contains(file.FullPath)).Sum(file => file.Size);
        output.WriteLine(L.F($"{plan.Remove.Count:N0} copie(s) seraient retirées · {L.Size(freed)} · un exemplaire gardé par groupe", $"{plan.Remove.Count:N0} extra copy(ies) would be removed · {L.Size(freed)} · one copy kept per group"));
        if (plan.UndecidedGroups > 0)
            output.WriteLine(L.F($"{plan.UndecidedGroups:N0} groupe(s) sans copie dans le dossier choisi resteraient intacts.", $"{plan.UndecidedGroups:N0} group(s) without a copy in the chosen folder would be left intact."));
        return new { Rule = rule, KeepIn = keepIn, plan.Remove, BytesFreed = freed, plan.UndecidedGroups };
    }

    private static int Pairs(string? store, TextWriter output)
    {
        var pairs = PairStore.Load(store);
        if (pairs.Count == 0) output.WriteLine(L.T("Aucune paire enregistrée. Enregistrez-en une depuis Checker San.", "No saved pairs. Save one from Checker San."));
        foreach (var pair in pairs)
        {
            output.WriteLine($"{pair.Name}  [{pair.Id}]");
            output.WriteLine($"  {pair.Folder1}  ⇄  {pair.Folder2}");
            output.WriteLine($"  {pair.ScheduleText} · {pair.LastRunText}");
        }
        return Clean;
    }

    private static async Task<int> RunPairAsync(string name, string? store, int? readers, string? log, TextWriter output, TextWriter error, CancellationToken token)
    {
        if (PairStore.Find(name, store) is not { } pair)
        {
            error.WriteLine(L.F($"Aucune paire ne s’appelle « {name} ». Voir : checkersan-cli pairs", $"No pair is called “{name}”. See: checkersan-cli pairs"));
            return Failure;
        }
        var run = await PairCheck.RunAsync(pair, store, readers, log, null, token);
        output.WriteLine(L.F($"Paire : {pair.Name}", $"Pair: {pair.Name}"));
        output.WriteLine(L.F($"Dossier 1 : {pair.Folder1}", $"Folder 1: {pair.Folder1}"));
        output.WriteLine(L.F($"Dossier 2 : {pair.Folder2}", $"Folder 2: {pair.Folder2}"));
        output.WriteLine((pair with { LastRun = run }).LastRunText);
        if (run.ReportPath is not null) output.WriteLine(L.T("Rapport : ", "Report: ") + run.ReportPath);
        else if (run.Failure is not null && run.ExitCode != Failure) output.WriteLine("! " + run.Failure);
        output.WriteLine(Verdict(run.ExitCode, L.T("Les deux dossiers sont identiques.", "Both folders are identical."), L.T("Des différences sont prouvées.", "Differences are proven.")));
        return run.ExitCode;
    }

    private static string Verdict(int code, string clean, string differences) => code switch
    {
        Clean => "OK — " + clean,
        Differences => L.T("ÉCARTS — ", "DIFFERENCES — ") + differences,
        Failure => L.T("ÉCHEC — la vérification n’a pas pu être faite.", "FAILED — the check could not be run."),
        _ => L.T("INCOMPLET — la vérification n’a pas pu tout prouver.", "INCOMPLETE — verification could not prove everything.")
    };

    private static string Label(SnapshotChange change) => change switch
    {
        SnapshotChange.Missing => L.T("ABSENT    ", "MISSING   "),
        SnapshotChange.Changed => L.T("MODIFIÉ   ", "CHANGED   "),
        SnapshotChange.Added => L.T("AJOUTÉ    ", "ADDED     "),
        SnapshotChange.MissingFolder => L.T("DOSSIER ABSENT", "FOLDER MISSING"),
        _ => L.T("DOSSIER AJOUTÉ", "FOLDER ADDED")
    };

    private static string OutsideMessage => L.T("Enregistrez le rapport à l’extérieur des dossiers vérifiés.", "Save the report outside the checked folders.");

    /// <summary>Refuses a JSON report path before a scan that may last hours, rather than after it.</summary>
    private static void CheckJson(string? path, params string[] roots)
    {
        if (path is not null) OutputFiles.EnsureOutside(path, roots, OutsideMessage);
    }

    /// <summary>A JSON report outside the checked folders, through links and \?\ too; an existing file is never replaced.</summary>
    private static void WriteJson(string path, object report, params string[] roots)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        using var stream = OutputFiles.CreateOutside(path, roots, OutsideMessage);
        JsonSerializer.Serialize(stream, report, options);
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine(Help());
        return Failure;
    }

    private static string Help() => L.T(
        """
        Checker San en ligne de commande — rien n’est jamais copié, déplacé ni supprimé.

          checkersan-cli compare <dossier1> <dossier2> [--json rapport.json] [--no-moved] [--simulate-copy both|1to2|2to1]
          checkersan-cli snapshot <dossier> <instantané.json>
          checkersan-cli verify <dossier> <instantané.json> [--json rapport.json]
          checkersan-cli duplicates <dossier> [--json rapport.json] [--min-size octets] [--exclude "a;b"] [--include-hidden]
                                    [--simulate-keep first|oldest|newest|shortest|folder [--keep-in <dossier>]]
          checkersan-cli pairs                      (paires enregistrées dans Checker San)
          checkersan-cli run <nom de la paire>      (rapport dans %LOCALAPPDATA%\CheckerSan\Reports)

        Simulations : --simulate-copy liste ce que « Compléter les dossiers » copierait ; --simulate-keep liste les copies
        qu’une règle retirerait. Rien n’est copié ni retiré.
        Options communes : --readers 1-16 (lectures simultanées), --lang en|fr, --log fichier (journal d’activité), --pairs fichier.
        Codes de sortie : 0 rien à signaler · 1 écarts ou doublons prouvés · 2 vérification incomplète (erreurs, fichiers non lus, éléments réels face à des liens) · 3 commande invalide ou erreur.
        """,
        """
        Checker San command line — nothing is ever copied, moved or deleted.

          checkersan-cli compare <folder1> <folder2> [--json report.json] [--no-moved] [--simulate-copy both|1to2|2to1]
          checkersan-cli snapshot <folder> <snapshot.json>
          checkersan-cli verify <folder> <snapshot.json> [--json report.json]
          checkersan-cli duplicates <folder> [--json report.json] [--min-size bytes] [--exclude "a;b"] [--include-hidden]
                                    [--simulate-keep first|oldest|newest|shortest|folder [--keep-in <folder>]]
          checkersan-cli pairs                      (pairs saved in Checker San)
          checkersan-cli run <pair name>            (report in %LOCALAPPDATA%\CheckerSan\Reports)

        Simulations: --simulate-copy lists what Complete folders would copy; --simulate-keep lists the copies a rule
        would remove. Nothing is copied or removed.
        Common options: --readers 1-16 (simultaneous reads), --lang en|fr, --log file (activity log), --pairs file.
        Exit codes: 0 nothing to report · 1 proven differences or duplicates · 2 incomplete verification (errors, unread files, real items facing links) · 3 invalid command or error.
        """);
}
