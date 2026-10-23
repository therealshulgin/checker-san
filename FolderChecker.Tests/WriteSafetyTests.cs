using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FolderChecker.Core;

namespace FolderChecker.Tests;

// Lot B of the adversarial review (revue/ARBITRAGE.md): writes and removals only where they belong.
internal static partial class Program
{
    // AUT-03 = CMP-04, CMP-08, CMP-09: one check for every output, made before the scan.
    private static async Task OutputsStayOutsideCheckedFolders()
    {
        using var w = new TestWorkspace();
        var run = CliRunner(Path.Combine(w.Root, "activity.log"));
        w.FileAt(w.Source, "a.txt", "same");
        w.FileAt(w.Target, "a.txt", "same");
        var reports = Path.Combine(w.Root, "reports");
        await CreateJunction(reports, w.Source);
        var throughJunction = await run(["compare", w.Source, w.Target, "--json", Path.Combine(reports, "r.json")]);
        Equal(3, throughJunction.Code, throughJunction.Output);
        Check(!throughJunction.Output.Contains("OK —", StringComparison.Ordinal), "the report path is refused before the scan");
        var prefixed = await run(["compare", w.Source, w.Target, "--json", @"\\?\" + Path.Combine(w.Source, "p.json")]);
        Equal(3, prefixed.Code, prefixed.Output);
        var rootWritten = Directory.GetLastWriteTimeUtc(w.Source);
        var stream = await run(["compare", w.Source, w.Target, "--json", w.Source + ":hidden.json"]);
        Equal(3, stream.Code, stream.Output);
        Equal(rootWritten, Directory.GetLastWriteTimeUtc(w.Source), "no stream is attached to the compared folder");
        Equal("a.txt", string.Join(",", Directory.GetFileSystemEntries(w.Source).Select(Path.GetFileName)), "nothing was written into folder 1");

        var inside = await run(["snapshot", w.Source, Path.Combine(w.Source, "s.json")]);
        Check(inside.Code == 3 && !inside.Output.Contains("Snapshot:", StringComparison.Ordinal), "a snapshot destination inside the folder is refused before reading: " + inside.Output);
        var existing = w.FileAt(w.Root, "existing.json", "keep me");
        Equal(3, (await run(["snapshot", w.Source, existing])).Code, "an existing snapshot file is refused before reading");
        Equal("keep me", File.ReadAllText(existing), "and left as it was");
        Throws<IOException>(() => OutputFiles.EnsureOutside(Path.Combine(w.Root, "missing", "r.json"), [w.Source]), "a missing destination folder is refused");
        Throws<IOException>(() => OutputFiles.EnsureOutside(w.Target + ":r.csv", [w.Source, w.Target]), "a stream of a root is refused");
        OutputFiles.EnsureOutside(Path.Combine(w.Root, "source-reports.json"), [w.Source, w.Target]);
    }

    // AUT-08, AUT-09: the folder really written is checked, names do not follow the Windows calendar.
    private static async Task PairReportsNeverLandInComparedFolders()
    {
        using var w = new TestWorkspace();
        var store = Path.Combine(w.Root, "store", "pairs.json");
        var pair = PairStore.Save(new SavedPair { Name = "Reports", Folder1 = Path.Combine(w.Root, "store", "Reports"), Folder2 = w.Target }, store);
        var mine = Directory.CreateDirectory(Path.Combine(w.Root, "store", "Reports", pair.Id)).FullName;
        for (var i = 0; i < 25; i++) File.WriteAllText(Path.Combine(mine, $"2020-01-01 0000{i:D2}.json"), "user file");
        var run = await PairCheck.RunAsync(pair, store, log: Path.Combine(w.Root, "activity.log"));
        Check(run.ReportPath is null && run.Failure is not null, "no report is written into a compared folder");
        Equal(25, Directory.GetFiles(mine).Length, "and no file of that folder is deleted by the rotation");

        var other = PairStore.Save(new SavedPair { Name = "Backup", Folder1 = w.Source, Folder2 = w.Target }, store);
        var folder = Directory.CreateDirectory(Path.Combine(w.Root, "store", "Reports", other.Id)).FullName;
        for (var i = 0; i < PairCheck.ReportsKept; i++) File.WriteAllText(Path.Combine(folder, $"2999-01-01 0000{i:D2}.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "notes.json"), "a file of mine");
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
        PairRun thai;
        try { thai = await PairCheck.RunAsync(other, store, log: Path.Combine(w.Root, "activity.log")); }
        finally { CultureInfo.CurrentCulture = culture; }
        Check(thai.ReportPath is not null && Path.GetFileName(thai.ReportPath).StartsWith(DateTime.Now.Year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "the report name uses Gregorian digits: " + thai.ReportPath);
        Check(File.Exists(thai.ReportPath!), "the report just written is never rotated away");
        Equal(PairCheck.ReportsKept + 1, Directory.GetFiles(folder).Length, "20 reports are kept, and a file that is not a report is left alone");
    }

    // DUP-01: a quarantine at any depth is excluded, and a group is refused rather than keeping a copy set aside.
    private static async Task NestedQuarantineIsNeverKept()
    {
        using var w = new TestWorkspace();
        var photo = TestWorkspace.Bytes(50_000, 9);
        var live = w.FileAt(w.Source, "2020/IMG_001.jpg", photo);
        var backup = w.FileAt(w.Source, "2020/Backup/IMG_001.jpg", photo);
        var year = Path.Combine(w.Source, "2020");
        var first = await new DuplicateEngine().ScanAsync(year, new DuplicateScanOptions { MaxRetries = 0 });
        var aside = await new DuplicateRemoval().DeleteAsync(first, [Path.GetFullPath(backup)], DuplicateRemovalMode.Quarantine);
        Check(aside.Complete, string.Join("; ", aside.Errors));
        var parent = await new DuplicateEngine().ScanAsync(w.Source, new DuplicateScanOptions { MaxRetries = 0 });
        Check(parent.SnapshotVerified && parent.Groups.Count == 0, "the copy set aside is not a duplicate of the live one");
        Check(parent.Warnings.Any(warning => warning.Contains(".CheckerSan-Quarantine", StringComparison.Ordinal)), string.Join("; ", parent.Warnings));
        Check(File.Exists(live), "the live copy is untouched");

        // A folder merely named like a quarantine (not made by Checker San) is still never the copy kept,
        // and refusing its group no longer blocks the other groups.
        using var d = new TestWorkspace();
        d.FileAt(d.Source, "x.bin", TestWorkspace.Bytes(4000, 1));
        var y = d.FileAt(d.Source, "y.bin", TestWorkspace.Bytes(4000, 1));
        d.FileAt(d.Source, ".CheckerSan-Quarantine/other.bin", TestWorkspace.Bytes(4000, 2));
        var ordinary = d.FileAt(d.Source, "ordinary.bin", TestWorkspace.Bytes(4000, 2));
        var scan = await new DuplicateEngine().ScanAsync(d.Source, new DuplicateScanOptions { MaxRetries = 0 });
        Equal(2, scan.Groups.Count, "both groups are found");
        var plan = DuplicateSelection.Extras(scan, KeepRule.FirstPath);
        Check(plan.Remove.All(path => !path.EndsWith("ordinary.bin", StringComparison.Ordinal)), "the rule never keeps the copy in a quarantine folder");
        var removal = await new DuplicateRemoval().DeleteAsync(scan, [Path.GetFullPath(y), Path.GetFullPath(ordinary)], DuplicateRemovalMode.Permanent);
        Check(!File.Exists(y) && File.Exists(ordinary), "the unrelated group is processed, the other group refused");
        Check(removal.Errors.Single().Contains("ordinary.bin", StringComparison.Ordinal) || removal.Errors.Single().Contains("other.bin", StringComparison.Ordinal), string.Join("; ", removal.Errors));
    }

    // DUP-02: no file can be renamed away and replaced while its group is verified, whatever the mode.
    private static async Task VerifiedDuplicatesCannotBeSwapped()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "keep.bin", TestWorkspace.Bytes(300_000, 4));
        var first = w.FileAt(w.Source, "a/copy.bin", TestWorkspace.Bytes(300_000, 4));
        var second = w.FileAt(w.Source, "b/copy.bin", TestWorkspace.Bytes(300_000, 4));
        var scan = await ScanRemovalFixture(w);
        if (!DuplicateRemoval.RecycleBinAvailable(w.Source)) throw new SkipTestException("no Recycle Bin on the test drive");
        Exception? swap = null;
        var tried = false;
        // Recycle Bin mode, where the verified file used to stay renamable; stopped before the shell is ever called.
        using var stop = new CancellationTokenSource();
        var result = await new DuplicateRemoval().DeleteAsync(scan, [Path.GetFullPath(first), Path.GetFullPath(second)], DuplicateRemovalMode.RecycleBin,
            new RemovalObserver(p =>
            {
                if (tried || !p.CurrentPath.EndsWith(Path.Combine("b", "copy.bin"), StringComparison.Ordinal)) return;
                tried = true;
                try { File.Move(first, first + ".old"); }
                catch (Exception exception) { swap = exception; }
                stop.Cancel();
            }), stop.Token);
        Check(tried && swap is IOException or UnauthorizedAccessException, "a verified file cannot be renamed while its group is checked: " + swap);
        Check(result.Cancelled && File.Exists(first) && File.Exists(second) && !File.Exists(first + ".old"), "nothing was moved: " + string.Join("; ", result.Errors));
    }

    // DUP-03: restoring never follows a link planted in the quarantine, nor a session replaced by a link.
    private static async Task RestoreNeverFollowsLinks()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        var scan = await ScanRemovalFixture(w);
        var copy = Path.Combine(w.Source, "nested", "copy.bin");
        var moved = await new DuplicateRemoval().DeleteAsync(scan, [copy], DuplicateRemovalMode.Quarantine);
        Check(moved.Complete, string.Join("; ", moved.Errors));
        var session = DuplicateRemoval.QuarantineSessions(w.Source).Single().Path;
        var privateFolder = w.DirectoryAt(w.Root, "Private");
        var victim = w.FileAt(privateFolder, "victim.docx", "private contents");
        await CreateJunction(Path.Combine(session, "evil"), privateFolder);
        var entry = JsonSerializer.Serialize(new { RelativePath = @"evil\victim.docx", Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(victim))), Size = new FileInfo(victim).Length });
        File.AppendAllText(session + ".manifest.jsonl", entry + "\n");
        var restored = await new DuplicateRemoval().RestoreAsync(session);
        Equal(1, restored.Restored, "the real quarantined file is restored");
        Check(File.Exists(copy), "it is back in place");
        Check(File.Exists(victim) && !File.Exists(Path.Combine(w.Source, "evil", "victim.docx")), "the file behind the junction stays where it was");
        Check(restored.Errors.Count == 1, string.Join("; ", restored.Errors));

        var quarantine = Path.GetDirectoryName(session)!;
        var elsewhere = w.DirectoryAt(w.Root, "Private2");
        var empty = w.DirectoryAt(elsewhere, "EmptyProjectFolder");
        var forged = Path.Combine(quarantine, new string('f', 32));
        await CreateJunction(forged, elsewhere);
        File.WriteAllText(forged + ".manifest.jsonl", JsonSerializer.Serialize(new { Schema = 1, Root = w.Source, CreatedAt = DateTimeOffset.Now }) + "\n");
        Check(DuplicateRemoval.QuarantineSessions(w.Source).All(listed => !string.Equals(listed.Path, forged, StringComparison.OrdinalIgnoreCase)), "a session replaced by a link is never offered");
        await ThrowsAsync<InvalidOperationException>(() => new DuplicateRemoval().RestoreAsync(forged));
        Check(Directory.Exists(empty), "no folder outside the scanned folder is removed");
    }

    // COP-01 (final pass), COP-07, COP-08, COP-09 and the review leads on the copy.
    private static async Task CopyWritesOnlyWhereItWasPlanned()
    {
        // The final folder pass refuses a root replaced by a junction instead of writing metadata elsewhere.
        using (var w = new TestWorkspace())
        {
            var album = w.DirectoryAt(w.Source, "Album");
            w.FileAt(w.Source, "Album/track.mp3", "music");
            File.SetAttributes(album, FileAttributes.Hidden | FileAttributes.Directory);
            var outside = w.DirectoryAt(w.Root, "outside");
            var outsideAlbum = w.DirectoryAt(outside, "Album");
            var plan = CopyEngine.CreatePlan(await Compare(w), true);
            var moved = Path.Combine(w.Root, "target-moved");
            var swapped = false;
            var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"), new CopyObserver(p =>
            {
                if (swapped || p.CompletedFiles < p.TotalFiles || !p.Phase.Contains("termin", StringComparison.OrdinalIgnoreCase)) return;
                swapped = true;
                Directory.Move(w.Target, moved);
                CreateJunction(w.Target, outside).GetAwaiter().GetResult();
            }));
            Check(swapped && !copy.Complete, "the swapped root is refused: " + string.Join("; ", copy.Errors));
            Equal(FileAttributes.Directory, File.GetAttributes(outsideAlbum), "a folder outside the copy keeps its attributes");
            Directory.Delete(w.Target);
            Directory.Move(moved, w.Target);
        }

        // A folder that appeared in the destination after the comparison keeps its own attributes and dates.
        using (var w = new TestWorkspace())
        {
            var projects = w.DirectoryAt(w.Source, "Projects");
            Directory.SetLastWriteTimeUtc(projects, new DateTime(2005, 5, 5, 0, 0, 0, DateTimeKind.Utc));
            File.SetAttributes(projects, FileAttributes.Hidden | FileAttributes.Directory);
            var plan = CopyEngine.CreatePlan(await Compare(w), true);
            var mine = w.DirectoryAt(w.Target, "Projects");
            var created = Directory.GetLastWriteTimeUtc(mine);
            var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
            Check(copy.Complete, string.Join("; ", copy.Errors));
            Check((File.GetAttributes(mine) & FileAttributes.Hidden) == 0 && Directory.GetLastWriteTimeUtc(mine) == created, "an existing folder is not given the source's metadata");
        }

        // Resuming a copy renamed before its attributes were applied applies them.
        using (var w = new TestWorkspace())
        {
            var file = w.FileAt(w.Source, "report.pdf", TestWorkspace.Bytes(70_000, 3));
            File.SetAttributes(file, FileAttributes.ReadOnly | FileAttributes.Hidden);
            try
            {
                var journal = Path.Combine(w.Root, "copy.json");
                var plan = CopyEngine.CreatePlan(await Compare(w), true);
                Check((await new CopyEngine().CopyAsync(plan, journal)).Complete, "first copy");
                var copied = Path.Combine(w.Target, "report.pdf");
                File.SetAttributes(copied, FileAttributes.Normal);
                var crashed = CopyEngine.ReadPlan(journal)!;
                crashed.Items.Single().Completed = false;
                File.WriteAllText(journal, JsonSerializer.Serialize(crashed) + "\n");
                var resumed = await new CopyEngine().CopyAsync(CopyEngine.ReadPlan(journal)!, journal);
                Check(resumed.Complete, string.Join("; ", resumed.Errors));
                Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, File.GetAttributes(copied) & (FileAttributes.ReadOnly | FileAttributes.Hidden), "the attributes are applied on resume");
                File.SetAttributes(copied, FileAttributes.Normal);
            }
            finally { File.SetAttributes(file, FileAttributes.Normal); }
        }

        // An abandoned temporary is reported but never copied; a backup folder recreated at the same path is refused.
        using (var w = new TestWorkspace())
        {
            w.FileAt(w.Source, "video.mp4", "video");
            w.FileAt(w.Target, ".checkersan-0123456789abcdef0123456789abcdef.partial", "fragment");
            var comparison = await Compare(w);
            Check(comparison.Rows.Single(row => row.RelativePath.EndsWith(".partial", StringComparison.Ordinal)).Detail.Contains("temporaire", StringComparison.OrdinalIgnoreCase), "the fragment is named for what it is");
            var plan = CopyEngine.CreateBidirectionalPlan(comparison);
            Check(plan.Items.All(item => !item.RelativePath.EndsWith(".partial", StringComparison.Ordinal)) && plan.AbandonedTemporaries == 1, "the fragment is never copied over");
            var oneWay = CopyEngine.CreatePlan(comparison, true);
            Directory.Delete(w.Target, recursive: true);
            Directory.CreateDirectory(w.Target);
            var swapped = await new CopyEngine().CopyAsync(oneWay, Path.Combine(w.Root, "copy.json"));
            Check(!swapped.Complete && !File.Exists(Path.Combine(w.Target, "video.mp4")), "a folder recreated at the same path is another folder: " + string.Join("; ", swapped.Errors));
        }

        // A source that became online-only after the plan is not read, so never downloaded.
        using (var w = new TestWorkspace())
        {
            var cloud = w.FileAt(w.Source, "cloud.bin", TestWorkspace.Bytes(10_000, 8));
            var plan = CopyEngine.CreatePlan(await Compare(w), true);
            File.SetAttributes(cloud, FileAttributes.Offline);
            try
            {
                var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
                Check(!copy.Complete && copy.BytesWritten == 0 && !File.Exists(Path.Combine(w.Target, "cloud.bin")), string.Join("; ", copy.Errors));
            }
            finally { File.SetAttributes(cloud, FileAttributes.Normal); }
        }
    }
}
