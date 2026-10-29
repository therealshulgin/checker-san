using System.Reflection;
using FolderChecker.Cli;
using FolderChecker.Core;

namespace FolderChecker.Tests;

// Gathering everything into folder 1 or 2: contents found elsewhere are not copied again, incidents no longer block.
internal static partial class Program
{
    private static string[] Paths(CopyPlan plan) => plan.Items.Select(item => item.RelativePath).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Contents of 5,000 bytes, distinct per seed: above the size under which a gathering copies a file for its name alone.</summary>
    private static byte[] Body(int seed, int length = 5000) => TestWorkspace.Bytes(length, seed);

    private static async Task GatherSkipsContentsFoundElsewhere()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "a/one.txt", Body(1));
        w.FileAt(w.Source, "a/two.txt", Body(2));
        w.FileAt(w.Source, "new/three.txt", Body(3, 6000));
        w.FileAt(w.Source, "onlymoved/four.txt", Body(4));
        w.DirectoryAt(w.Source, "empty/inner");
        w.FileAt(w.Target, "archive/one.txt", Body(1));
        w.FileAt(w.Target, "archive/x/four.txt", Body(4));
        w.FileAt(w.Target, "b/dup-of-two.txt", Body(2));
        w.FileAt(w.Target, "b/two-again.txt", Body(2));
        w.FileAt(w.Target, "b/five.txt", Body(5));
        w.DirectoryAt(w.Target, "c");
        var result = await Compare(w);
        Check(result.Finished && result.MovedFilesSearched && result.Errors.Count == 0, string.Join("; ", result.Errors));
        var again = result.Rows.Single(row => row.RelativePath == @"b\two-again.txt");
        Equal(ComparisonStatus.OnlyTarget, again.Status, "a path only in folder 2 keeps its status");
        Equal(Path.Combine(w.Source, "a", "two.txt"), again.SourceCopyPath, "the second copy in folder 2 is traced back to folder 1, though the first match stopped the search");
        Check(again.TargetHash is not null && again.TargetHash == result.Rows.Single(row => row.RelativePath == @"a\two.txt").SourceHash, "the match is proven by SHA-256");
        Equal<string?>(null, again.SourcePath, "the folder 1 copy is not mistaken for a file at the same path");
        Equal<string?>(null, result.Rows.Single(row => row.RelativePath == @"b\five.txt").SourceCopyPath, "a file absent from folder 1 names no copy there");

        var into2 = CopyEngine.CreateGatherPlan(result, intoTarget: true);
        Equal(string.Join("|", new[] { "empty", @"empty\inner", "new", @"new\three.txt" }), string.Join("|", Paths(into2)),
            "only the contents folder 2 lacks, with the folders holding them or holding no file");
        var into1 = CopyEngine.CreateGatherPlan(result, intoTarget: false);
        Equal(string.Join("|", new[] { "b", @"b\five.txt", "c" }), string.Join("|", Paths(into1)),
            "neither the moved files nor the second copy of a file of folder 1 come back");
        Equal(1, CopyEngine.GatherFileCount(result, intoTarget: true), "the button counts what the plan copies into 2");
        Equal(1, CopyEngine.GatherFileCount(result, intoTarget: false), "the button counts what the plan copies into 1");
        Check(CopyEngine.CreatePlan(result, true).Items.Any(item => item.RelativePath == @"a\one.txt"), "completing path by path still rebuilds the tree, unlike gathering");

        var run = CliRunner(Path.Combine(w.Root, "activity.log"));
        var simulated = await run(["compare", w.Source, w.Target, "--simulate-copy", "into2"]);
        Check(simulated.Output.Contains(@"new\three.txt") && !simulated.Output.Contains(@"a\one.txt"), "the command line simulates the same gathering: " + simulated.Output);

        foreach (var plan in new[] { into1, into2 })
        {
            var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, $"gather-{Guid.NewGuid():N}.json"));
            Check(copy.Complete, string.Join("; ", copy.Errors));
        }
        Check(File.ReadAllBytes(Path.Combine(w.Source, "b", "five.txt")).SequenceEqual(Body(5)), "gathered into folder 1");
        Check(File.ReadAllBytes(Path.Combine(w.Target, "new", "three.txt")).SequenceEqual(Body(3, 6000)), "gathered into folder 2");
        Check(!File.Exists(Path.Combine(w.Source, "b", "two-again.txt")) && !File.Exists(Path.Combine(w.Target, "a", "one.txt")), "nothing already present is duplicated");
        var after = await Compare(w);
        Equal(0, CopyEngine.GatherFileCount(after, true) + CopyEngine.GatherFileCount(after, false), "each folder now holds a copy of every file");
        Check(after.ContentCovered && after.TargetContentCovered, "and the comparison proves it both ways");
        Equal(0, CopyEngine.GatherShortfall(after, true) + CopyEngine.GatherShortfall(after, false), "nothing is left without a copy");
    }

    private static async Task GatherProceedsDespiteIncidents()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows sharing-mode test");
        using var w = new TestWorkspace();
        AddCommonFile(w);
        w.FileAt(w.Source, "only/new.txt", "new in 1");
        w.FileAt(w.Source, "only/locked.bin", TestWorkspace.Bytes(4096));
        var other = TestWorkspace.Bytes(4096);
        other[0] ^= 0xFF;
        w.FileAt(w.Target, "extra/other.bin", other);
        w.FileAt(w.Target, "extra/old.txt", "old in 2");
        ComparisonResult result;
        using (new FileStream(Path.Combine(w.Source, "common.wav"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (new FileStream(Path.Combine(w.Source, "only", "locked.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await Compare(w);
        Check(result.Errors.Count > 0 && result.Finished && !result.Cancelled, "unreadable files are incidents of a finished comparison");
        Equal(ComparisonStatus.Unverified, result.Rows.Single(row => row.RelativePath == @"only\locked.bin").Status, "the unreadable file is not counted as absent");
        Check(CopyEngine.CanCreatePlan(result), "incidents no longer prevent copying the other items");
        Equal(string.Join("|", new[] { "only", @"only\new.txt" }), string.Join("|", Paths(CopyEngine.CreateGatherPlan(result, true))),
            "the unreadable file is left out, the readable one is gathered");
        Equal(string.Join("|", new[] { "extra", @"extra\old.txt", @"extra\other.bin" }), string.Join("|", Paths(CopyEngine.CreateGatherPlan(result, false))),
            "the other direction is not held back either");
        Check(CopyEngine.CreatePlan(result, true).Items.All(item => item.RelativePath != @"only\locked.bin"), "completing path by path leaves it out too");
        Equal(2, CopyEngine.GatherShortfall(result, true), "the two unreadable files of folder 1 are still without a proven copy in folder 2");
        Check(!result.ContentCovered, "so the comparison claims no complete coverage");

        Check(!CopyEngine.CanCreatePlan(result with { Finished = false }), "an interrupted comparison cannot prepare a copy");
        Check(!CopyEngine.CanCreatePlan(result with { Cancelled = true }), "nor can a stopped one");
        var unreadableRoot = result with { Rows = [.. result.Rows, new ComparisonRow { Kind = EntryKind.Directory, RelativePath = ".", Status = ComparisonStatus.Unverified }] };
        Check(!CopyEngine.CanCreatePlan(unreadableRoot), "nor one whose root could not be read in full");
        Equal(0, CopyEngine.GatherFileCount(unreadableRoot, true), "no button is offered then");
        Throws<InvalidOperationException>(() => CopyEngine.CreateGatherPlan(unreadableRoot, true), "the plan itself refuses");
    }

    /// <summary>Rewrites a file in place with other bytes of the same length, its dates put back: only its handle tells.</summary>
    private static void RewriteInPlace(string path)
    {
        var written = File.GetLastWriteTimeUtc(path);
        var created = File.GetCreationTimeUtc(path);
        var bytes = File.ReadAllBytes(path);
        bytes[0] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, written);
        File.SetCreationTimeUtc(path, created);
    }

    // The folder 1 copy that lets a file of folder 2 stay out of a gathering is checked by handle at the end, like a moved
    // file. Here it differs from the file at its own path in folder 2, so no other proven row would have it checked.
    // REG-M08: once that copy changes, the file of folder 2, intact, is missing from folder 1 again and is gathered.
    // A moved file whose copy in folder 2 changes is found through the index of rows naming other paths (lot C).
    private static async Task GatherRechecksCopiesFoundInFolderOne()
    {
        using var w = new TestWorkspace();
        var source = w.FileAt(w.Source, "a.txt", Body(1));
        w.FileAt(w.Target, "a.txt", Body(2, 6000));
        w.FileAt(w.Target, @"b\y.txt", Body(1));
        w.FileAt(w.Source, "c.bin", Body(3));
        var copy = w.FileAt(w.Target, @"moved\c.bin", Body(3));
        var changed = false;
        var progress = new InlineProgress(p =>
        {
            if (changed || !(p.Phase.StartsWith("Contrôle final", StringComparison.Ordinal) || p.Phase.StartsWith("Final check", StringComparison.Ordinal))) return;
            changed = true;
            RewriteInPlace(source);
            RewriteInPlace(copy);
        });
        var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(), progress);
        Check(changed, "the folder 1 copy was rewritten after it was read");
        var y = result.Rows.Single(row => row.RelativePath == @"b\y.txt");
        Check(y is { Status: ComparisonStatus.OnlyTarget, SourceCopyPath: null }, "the copy it relied on no longer counts, but the file itself is intact: " + y);
        Equal(ComparisonStatus.Unverified, result.Rows.Single(row => row.RelativePath == "a.txt").Status, "the rewritten file is an incident");
        Check(CopyEngine.CreateGatherPlan(result, false).Items.Any(item => item.RelativePath == @"b\y.txt"), "so the file of folder 2 is gathered into folder 1");
        Check(CopyEngine.CreatePlan(result, false).Items.Any(item => item.RelativePath == @"b\y.txt"), "and completing folder 1 path by path still copies it");
        Equal(ComparisonStatus.Unverified, result.Rows.Single(row => row.RelativePath == "c.bin").Status, "a moved file whose copy changed after it was read proves nothing either");
    }

    // REG-M06: a copy relied on is checked again when the plan is made, against the identity the comparison read.
    private static async Task GatherRechecksCopiesWhenPlanning()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, @"Docs\these.docx", Body(1));
        var copy = w.FileAt(w.Target, @"Ancien\these-v1.docx", Body(1));
        var photo = w.FileAt(w.Source, @"Photos\mariage.jpg", Body(2));
        w.FileAt(w.Target, @"A trier\IMG_4411.jpg", Body(2));
        w.FileAt(w.Source, @"Musique\titre.mp3", Body(3));
        w.FileAt(w.Target, @"Tri\titre.mp3", Body(3));
        var folderOneCopy = w.FileAt(w.Source, @"Albums\vacances.jpg", Body(4));
        w.FileAt(w.Target, @"Vrac\vacances.jpg", Body(4));
        var result = await Compare(w);
        Equal(0, CopyEngine.CreateGatherPlan(result, true).FileCount, "before any change, every file has its copy in folder 2");
        File.Delete(copy);
        RewriteInPlace(photo);
        RewriteInPlace(folderOneCopy);
        var into2 = Paths(CopyEngine.CreateGatherPlan(result, true));
        Check(into2.Contains(@"Docs\these.docx") && into2.Contains("Docs"), "a file whose copy was deleted since is gathered, with its folder: " + string.Join("|", into2));
        Check(into2.Contains(@"Photos\mariage.jpg"), "and so is a file edited since its copy was found");
        Check(!into2.Contains(@"Musique\titre.mp3"), "a file whose copy is unchanged still is not");
        Check(Paths(CopyEngine.CreateGatherPlan(result, false)).Contains(@"Vrac\vacances.jpg"), "the other way round too");
    }

    // REG-M07: a file held open by another program is still planned; copying it fails for that item alone.
    private static async Task GatherSurvivesALockedUnreadFile()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows sharing-mode test");
        using var w = new TestWorkspace();
        w.FileAt(w.Source, @"seulement\a.txt", Body(1));
        var locked = w.FileAt(w.Source, @"seulement\base.pst", Body(2, 7000));
        var gone = w.FileAt(w.Source, @"seulement\parti.txt", Body(3));
        var result = await Compare(w);
        File.Delete(gone);
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var plan = CopyEngine.CreateGatherPlan(result, true);
            Check(plan.Items.Any(item => item.RelativePath == @"seulement\base.pst") && plan.Items.Any(item => item.RelativePath == @"seulement\a.txt"),
                "a file held open is planned with the others: " + string.Join("|", Paths(plan)));
            Equal(1, plan.UnreadableFiles, "a file gone since the comparison is counted, without aborting the plan");
            var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "gather.json"));
            Check(copy.Errors.Count == 1 && copy.Errors[0].Contains("base.pst"), "copying fails for that file alone: " + string.Join("; ", copy.Errors));
            Check(File.Exists(Path.Combine(w.Target, "seulement", "a.txt")), "and the others are copied");
        }
    }

    // REG-M09, REG-P06: the command line refuses a gathering without the search for moved files, says what a plan leaves
    // out, and gives the same containment answers as the screen and the reports.
    private static async Task GatherOnTheCommandLine()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "seul.txt", Body(1));
        var odd = @"\\?\" + Path.Combine(w.Source, "notes.");
        File.WriteAllBytes(odd, Body(2, 7000));
        try
        {
            var run = CliRunner(Path.Combine(w.Root, "activity.log"));
            var refused = await run(["compare", w.Source, w.Target, "--no-moved", "--simulate-copy", "into2"]);
            Equal(CheckerSanCli.Failure, refused.Code, "a gathering without the search for moved files is refused: " + refused.Output);
            var report = Path.Combine(w.Root, "report.json");
            var simulated = await run(["compare", w.Source, w.Target, "--simulate-copy", "into2", "--json", report]);
            Check(simulated.Output.Contains("Folder 2 contains all of folder 1: No"), "the containment answers are printed: " + simulated.Output);
            Check(simulated.Output.Contains("1 path(s) with a name Windows would alter left out"), "a name left out is said");
            Check(simulated.Output.Contains("1 item(s) would still have no proven copy"), "and so is what stays without a copy");
            var json = File.ReadAllText(report);
            Check(json.Contains("\"Folder2HoldsFolder1\": false") && json.Contains("\"UnsupportedNames\": 1"), "the JSON report says the same: " + json[..Math.Min(400, json.Length)]);
        }
        finally { File.Delete(odd); }
    }

    private static async Task GatherFindsLargeCopiesBySample()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(4 * 1024 * 1024 + 100);
        w.FileAt(w.Source, "big.bin", data);
        w.FileAt(w.Target, @"t1\big.bin", data);
        w.FileAt(w.Target, @"t2\big.bin", data);
        var other = (byte[])data.Clone();
        other[0] ^= 0xFF;
        w.FileAt(w.Target, @"t3\other.bin", other);
        var result = await Compare(w);
        Equal(Path.Combine(w.Source, "big.bin"), result.Rows.Single(row => row.RelativePath == @"t2\big.bin").SourceCopyPath, "a large second copy is found once its sample matches");
        var third = result.Rows.Single(row => row.RelativePath == @"t3\other.bin");
        Check(third.SourceCopyPath is null && third.TargetHash is null, "a different sample rules a file out without reading it whole");
        Equal(string.Join("|", new[] { "t3", @"t3\other.bin" }), string.Join("|", Paths(CopyEngine.CreateGatherPlan(result, false))), "only the different file is gathered into 1");
    }

    // The buttons count again at every display: 100,000 rows, a fifth of them online-only, must stay instant.
    private static Task GatherCountStaysFastOnLargeResults()
    {
        var rows = new List<ComparisonRow>();
        for (var folder = 0; folder < 1000; folder++)
        {
            rows.Add(new ComparisonRow { Kind = EntryKind.Directory, Status = ComparisonStatus.OnlySource, RelativePath = $@"d{folder}" });
            for (var file = 0; file < 100; file++)
            {
                var status = file % 5 == 0 ? ComparisonStatus.Unverified : file % 2 == 0 ? ComparisonStatus.OnlySource : ComparisonStatus.Moved;
                rows.Add(new ComparisonRow
                {
                    Kind = EntryKind.File, RelativePath = $@"d{folder}\sub\f{file}.jpg", Status = status, OnlineOnly = file % 5 == 0, SourceSize = 100_000,
                    TargetPath = status == ComparisonStatus.Moved ? $@"D:\two\sorted\d{folder}-f{file}.jpg" : null
                });
            }
        }
        var result = new ComparisonResult { SourceRoot = @"C:\one", TargetRoot = @"D:\two", Rows = rows, Finished = true, MovedFilesSearched = true };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var count = CopyEngine.GatherFileCount(result, true) + CopyEngine.GatherFileCount(result, false);
        clock.Stop();
        Equal(1000 * 40, count, "every readable file only in folder 1, and none the other way");
        Check(clock.ElapsedMilliseconds < 1000, $"both counts take {clock.ElapsedMilliseconds} ms");
        return Task.CompletedTask;
    }

    private static async Task FolderAlignmentSuggestsTheSubfolder()
    {
        using var w = new TestWorkspace();
        var seed = 10;
        foreach (var name in new[] { @"p\1.txt", @"p\2.txt", @"p\q\3.txt", "4.txt" })
        {
            var body = Body(seed++);
            w.FileAt(w.Source, name, body);
            w.FileAt(w.Target, Path.Combine("Backup", "Work", name), body);
        }
        w.FileAt(w.Target, "unrelated.txt", Body(99));
        var suggestion = FolderAlignment.Suggest(await Compare(w));
        Check(suggestion is { InFolder2: true, Files: 4, Outside: 1 }, "folder 2 holds folder 1 in a subfolder, and one file of folder 2 lies outside it: " + suggestion);
        Equal(Path.Combine(w.Target, "Backup", "Work"), suggestion!.Folder, "that subfolder is named");
        var swapped = FolderAlignment.Suggest(await new ComparisonEngine().CompareAsync(w.Target, w.Source, new ComparisonOptions()));
        Check(swapped is { InFolder2: false, Outside: 1 } && swapped.Folder == Path.Combine(w.Target, "Backup", "Work"), "the other way round, the subfolder is in folder 1");
        var aligned = await new ComparisonEngine().CompareAsync(w.Source, Path.Combine(w.Target, "Backup", "Work"), new ComparisonOptions());
        Equal<FolderAlignment.Suggestion?>(null, FolderAlignment.Suggest(aligned), "folders that line up get no suggestion");

        // REG-P01: three files of twenty-three laid out elsewhere are not "most of the folder".
        for (var index = 0; index < 20; index++) w.FileAt(w.Source, $"projet-{index:00}.txt", Body(200 + index));
        Equal<FolderAlignment.Suggestion?>(null, FolderAlignment.Suggest(await Compare(w)), "a minority of the folder suggests nothing");
    }

    // REG-M01: at the root of a drive, the Recycle Bin and system folders are the system's. Elsewhere, a recycle bin, a
    // quarantine or a temporary is listed, but never taken for the copy that lets a file stay out of a gathering.
    private static async Task GatherIgnoresRecycleBinAndQuarantine()
    {
        Check(SystemFolders.IsVolumeRoot(Path.GetPathRoot(Environment.SystemDirectory)!), "a drive root ending with its separator is a volume root");
        using var w = new TestWorkspace();
        Check(!SystemFolders.IsVolumeRoot(w.Source), "an ordinary folder is not");
        w.FileAt(w.Source, @"Photos\IMG_0001.jpg", Body(1));
        w.FileAt(w.Source, @"Travail\devis.docx", Body(2));
        var bin = w.DirectoryAt(w.Target, "$RECYCLE.BIN");
        File.SetAttributes(bin, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        w.FileAt(w.Target, @"$RECYCLE.BIN\S-1-5-21-1-2-3-1001\$RX7Q2LM\Photos\IMG_0001.jpg", Body(1));
        w.FileAt(w.Target, @"$RECYCLE.BIN\S-1-5-21-1-2-3-1001\$RX7Q2LM\Travail\devis.docx", Body(2));
        w.FileAt(w.Target, @"System Volume Information\tracking.log", Body(3));
        w.FileAt(w.Target, @"Docs\garder.txt", Body(4));

        var options = new ComparisonOptions();
        typeof(ComparisonOptions).GetProperty("TreatRootsAsVolumes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(options, true);
        var root = await Compare(w, options);
        Check(root.Rows.All(row => !row.RelativePath.StartsWith("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) && !row.RelativePath.StartsWith("System Volume", StringComparison.OrdinalIgnoreCase)),
            "a volume root leaves its recycle bin and system folders out of the comparison");
        Check(root.Warnings.Any(warning => warning.Contains("$RECYCLE.BIN") && warning.Contains("System Volume Information")), "and says so");
        Check(root.Errors.Count == 0 && root.ContentCovered == false, string.Join("; ", root.Errors));
        Equal(string.Join("|", new[] { "Photos", @"Photos\IMG_0001.jpg", "Travail", @"Travail\devis.docx" }), string.Join("|", Paths(CopyEngine.CreateGatherPlan(root, true))),
            "files whose only other copy is in the Recycle Bin are gathered");
        Equal(string.Join("|", new[] { "Docs", @"Docs\garder.txt" }), string.Join("|", Paths(CopyEngine.CreateGatherPlan(root, false))), "and the Recycle Bin is never gathered as contents");
        Equal<FolderAlignment.Suggestion?>(null, FolderAlignment.Suggest(root), "nor suggested as a folder to compare with");

        // Not at a volume root: the same folders are listed, but never prove anything.
        w.FileAt(w.Target, @".CheckerSan-Quarantine\2026-10-20 1200\Rangement\plage (2).jpg", Body(5));
        w.FileAt(w.Source, @"Photos\plage.jpg", Body(5));
        w.FileAt(w.Source, @".CheckerSan-Quarantine\2026-10-21 0900\vieux.txt", Body(6));
        w.FileAt(w.Target, @"Archives\vieux.txt", Body(6));
        var plain = await Compare(w);
        Check(plain.Rows.Any(row => row.RelativePath.StartsWith("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase)), "an ordinary root lists everything");
        Check(plain.Rows.Where(row => row.Status == ComparisonStatus.Moved).All(row => !SystemFolders.NeverEvidence(Path.GetRelativePath(w.Target, row.TargetPath!))),
            "no file is found as a recycled or quarantined copy");
        var into2 = Paths(CopyEngine.CreateGatherPlan(plain, true));
        Check(new[] { @"Photos\IMG_0001.jpg", @"Travail\devis.docx", @"Photos\plage.jpg" }.All(into2.Contains), "so all three are gathered: " + string.Join("|", into2));
        Equal<string?>(null, plain.Rows.Single(row => row.RelativePath == @"Archives\vieux.txt").SourceCopyPath, "a quarantined copy in folder 1 proves nothing either");
        Check(Paths(CopyEngine.CreateGatherPlan(plain, false)).Contains(@"Archives\vieux.txt"), "so it is gathered into 1");
    }

    // REG-M02: the temporary of a copy stopped during its check holds every byte, yet it is never a kept copy.
    private static async Task GatherIgnoresInterruptedTemporaries()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, @"Videos\vacances.mp4", Body(1));
        w.FileAt(w.Target, $@"Videos\.checkersan-{Guid.NewGuid():N}.partial", Body(1));
        w.FileAt(w.Target, @"Photos\scan.tif", Body(2));
        w.FileAt(w.Source, $@"Photos\.checkersan-{Guid.NewGuid():N}.partial", Body(2));
        var result = await Compare(w);
        Equal(ComparisonStatus.OnlySource, result.Rows.Single(row => row.RelativePath == @"Videos\vacances.mp4").Status, "the source file is not found as its own temporary");
        Equal<string?>(null, result.Rows.Single(row => row.RelativePath == @"Photos\scan.tif").SourceCopyPath, "nor the other way round");
        Check(Paths(CopyEngine.CreateGatherPlan(result, true)).Contains(@"Videos\vacances.mp4"), "the file is gathered into 2");
        Check(Paths(CopyEngine.CreateGatherPlan(result, false)).Contains(@"Photos\scan.tif"), "and into 1");

        // Defense in depth: a row naming a temporary as its copy still counts as missing.
        var moved = new ComparisonResult
        {
            SourceRoot = w.Source, TargetRoot = w.Target, Finished = true, MovedFilesSearched = true,
            Rows = [new ComparisonRow { Kind = EntryKind.File, Status = ComparisonStatus.Moved, RelativePath = "film.mp4", SourceSize = 10_000,
                TargetPath = Path.Combine(w.Target, $".checkersan-{Guid.NewGuid():N}.partial") }]
        };
        Equal(1, CopyEngine.GatherFileCount(moved, true), "a temporary is never the copy a file relies on");
    }

    // REG-M03: an empty or tiny file is worth its name; unrelated files share such contents.
    private static async Task GatherKeepsEmptyFiles()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, @"projet\paquet\__init__.py", "");
        w.FileAt(w.Source, @"projet\paquet\sous\__init__.py", "");
        w.FileAt(w.Source, @"projet\logs\.gitkeep", "");
        w.FileAt(w.Source, @"config\port.txt", "8080");
        w.FileAt(w.Source, @"config\actif.flag", "1");
        w.FileAt(w.Target, @"divers\vide.txt", "");
        w.FileAt(w.Target, @"divers\autre.txt", "8080");
        w.FileAt(w.Target, @"divers\compteur.txt", "1");
        var result = await Compare(w);
        Check(result.Rows.Count(row => row.Status == ComparisonStatus.Moved) == 5, "the comparison itself still finds the same bytes elsewhere");
        Equal(string.Join("|", new[] { "config", @"config\actif.flag", @"config\port.txt", "projet", @"projet\logs", @"projet\logs\.gitkeep",
                @"projet\paquet", @"projet\paquet\__init__.py", @"projet\paquet\sous", @"projet\paquet\sous\__init__.py" }.Order(StringComparer.OrdinalIgnoreCase)),
            string.Join("|", Paths(CopyEngine.CreateGatherPlan(result, true))), "but a gathering copies them with their names and folders");
        Equal(string.Join("|", new[] { "divers", @"divers\autre.txt", @"divers\compteur.txt", @"divers\vide.txt" }),
            string.Join("|", Paths(CopyEngine.CreateGatherPlan(result, false))), "both ways");
    }

    // REG-M04, REG-P02, REG-P04: what a gathering leaves behind is counted, and the button counts what the plan copies.
    private static async Task GatherCountsWhatItLeavesBehind()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "rapport.docx", Body(1));
        w.FileAt(w.Target, "rapport.docx", Body(2, 5100));
        w.FileAt(w.Source, "commun.txt", Body(3));
        w.FileAt(w.Target, "commun.txt", Body(3));
        var odd = @"\\?\" + Path.Combine(w.Source, "notes.");
        File.WriteAllBytes(odd, Body(4));
        try
        {
            var result = await Compare(w);
            Equal(0, CopyEngine.GatherFileCount(result, true), "a name Windows would alter is not counted as copyable");
            Equal(0, CopyEngine.CreateGatherPlan(result, true).FileCount, "as the plan leaves it out");
            Equal(2, CopyEngine.GatherShortfall(result, true), "the other version and the odd name stay without a copy in folder 2");
            Equal(1, CopyEngine.GatherShortfall(result, false), "the other version stays without a copy in folder 1");
            Check(!result.ContentCovered && !result.TargetContentCovered, "neither folder holds a copy of every file");
        }
        finally { File.Delete(odd); }
    }

    // Piste 1 of the engine review: a folder named with a final dot cannot be listed. Its error must hold back what it
    // contains, or copies allowed despite incidents would drop it without a word.
    private static async Task GatherBlocksUnlistedFolders()
    {
        using var w = new TestWorkspace();
        var first = @"\\?\" + Path.Combine(w.Source, "Archives.");
        var second = @"\\?\" + Path.Combine(w.Target, "Archives.");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllBytes(first + @"\contrat.pdf", Body(1));
        File.WriteAllBytes(second + @"\autre.pdf", Body(2));
        w.FileAt(w.Source, @"Docs\a.txt", Body(3));
        try
        {
            var result = await Compare(w);
            Equal(ComparisonStatus.Unverified, result.Rows.Single(row => row.RelativePath == "Archives.").Status, "the unlisted folder is an incident, not a folder present on both sides");
            Check(CopyEngine.CanCreatePlan(result), "the other items can still be copied");
            Equal(string.Join("|", new[] { "Docs", @"Docs\a.txt" }), string.Join("|", Paths(CopyEngine.CreateGatherPlan(result, true))), "nothing is planned in or for it");
            Check(CopyEngine.GatherShortfall(result, true) >= 1, "and it is counted as left behind");
        }
        finally
        {
            File.Delete(first + @"\contrat.pdf");
            File.Delete(second + @"\autre.pdf");
            Directory.Delete(first);
            Directory.Delete(second);
        }
    }
}
