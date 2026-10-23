using System.Runtime.InteropServices;
using System.Text.Json;
using FolderChecker.Cli;
using FolderChecker.Core;

namespace FolderChecker.Tests;

// Lot C of the adversarial review (revue/ARBITRAGE.md): robustness of the command line, pairs, removal, copy and comparison.
internal static partial class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

    // AUT-04, AUT-05, AUT-10: whatever happens, one of the four documented codes, and typos are refused before a scan.
    private static async Task CommandLineAlwaysEndsWithADocumentedCode()
    {
        using var w = new TestWorkspace();
        var log = Path.Combine(w.Root, "activity.log");
        var run = CliRunner(log);
        w.FileAt(w.Source, "a.txt", "one");
        w.FileAt(w.Target, "a.txt", "two!");
        var snapshot = Path.Combine(w.Root, "s.json");
        Equal(CheckerSanCli.Clean, (await run(["snapshot", w.Source, snapshot])).Code, "reference snapshot");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            foreach (var command in new[] { new[] { "snapshot", w.Source, Path.Combine(w.Root, "never.json") }, new[] { "verify", w.Source, snapshot } })
            {
                using var output = new StringWriter();
                Equal(CheckerSanCli.Incomplete, await CheckerSanCli.RunAsync([.. command, "--lang", "en", "--log", log], output, TextWriter.Null, cancelled.Token), "Ctrl+C during " + command[0]);
            }
        }
        Check(File.ReadAllText(log).Contains("cancelled", StringComparison.Ordinal), "a stopped command leaves an activity line");
        Check(!File.Exists(Path.Combine(w.Root, "never.json")), "a stopped snapshot saves nothing");

        Equal(3, (await CheckerSanCli.RunAsync(["compare", w.Source, w.Target, "--lang", "en", "--log", ""], TextWriter.Null, TextWriter.Null)), "an empty log path is refused before the scan");
        Equal(CheckerSanCli.Differences, (await CheckerSanCli.RunAsync(["compare", w.Source, w.Target, "--lang", "en", "--log", w.Root], TextWriter.Null, TextWriter.Null)), "a log that cannot be written never changes the verdict");
        var typo = await run(["compare", w.Source, w.Target, "--jsno", Path.Combine(w.Root, "r.json")]);
        Check(typo.Code == 3 && typo.Output.Contains("--jsno", StringComparison.Ordinal), typo.Output);
        var flagAsValue = await run(["compare", w.Source, w.Target, "--json", "--no-moved"]);
        Check(flagAsValue.Code == 3 && !File.Exists("--no-moved"), "a flag is never taken for a file name: " + flagAsValue.Output);
        Equal(3, (await run(["snapshot", w.Source, Path.Combine(w.Root, "t.json"), "--min-size", "3"])).Code, "an option of another command is refused");
        Equal(CheckerSanCli.Differences, (await run(["compare", w.Source, w.Target, "--NO-MOVED"])).Code, "flags ignore case");
        Equal(CheckerSanCli.Clean, (await run(["compare", w.Source, w.Target, "--help"])).Code, "help with a command still exits with 0");
    }

    // AUT-06, AUT-07 and the review leads: targeted updates keep recorded results, full paths only.
    private static Task PairsKeepRecordedResults()
    {
        using var w = new TestWorkspace();
        var store = Path.Combine(w.Root, "store", "pairs.json");
        var pair = PairStore.Save(new SavedPair { Name = "Photos", Folder1 = w.Source, Folder2 = w.Target }, store);
        PairStore.RecordRun(pair.Id, new PairRun(DateTimeOffset.Now, ExitCodes.Differences, 42, 0, 0, null), store);
        var scheduled = PairStore.Modify(pair.Id, current => current with { Schedule = new PairSchedule(ScheduleFrequency.Daily, DayOfWeek.Monday, new TimeOnly(3, 0)) }, store);
        Check(scheduled?.LastRun is { Differences: 42 } && PairStore.Find(pair.Id, store)!.LastRun is { Differences: 42 }, "scheduling keeps the result recorded meanwhile");
        Check(PairStore.Modify("ffffffff", current => current, store) is null, "a deleted pair is reported");
        Throws<ArgumentException>(() => PairStore.Save(new SavedPair { Name = "Relative", Folder1 = "Photos", Folder2 = w.Target }, store), "a relative folder is refused");
        return Task.CompletedTask;
    }

    // DUP-04, DUP-06, DUP-08: read-only copies, one group's refusal, a failing journal, another spelling of the root.
    private static async Task DuplicateRemovalKeepsGoingAndTracing()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "keep1.bin", TestWorkspace.Bytes(5000, 1));
        var readOnly = w.FileAt(w.Source, "cd/copy1.bin", TestWorkspace.Bytes(5000, 1));
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        w.FileAt(w.Source, "keep2.bin", TestWorkspace.Bytes(5000, 2));
        var changed = w.FileAt(w.Source, "copy2.bin", TestWorkspace.Bytes(5000, 2));
        w.FileAt(w.Source, "keep3.bin", TestWorkspace.Bytes(5000, 3));
        var third = w.FileAt(w.Source, "copy3.bin", TestWorkspace.Bytes(5000, 3));
        var scan = await ScanRemovalFixture(w);
        File.WriteAllBytes(changed, TestWorkspace.Bytes(5000, 9));
        var result = await new DuplicateRemoval { LogPath = Path.Combine(w.Root, "actions.csv") }.DeleteAsync(scan,
            [Path.GetFullPath(readOnly), Path.GetFullPath(changed), Path.GetFullPath(third)], DuplicateRemovalMode.Permanent);
        Check(!File.Exists(readOnly), "a read-only duplicate is deleted");
        Check(File.Exists(changed) && !File.Exists(third), "the changed group is refused, the next one still processed");
        Check(result.Errors.Count == 1 && result.DeletedPaths.Count == 2, string.Join("; ", result.Errors));

        // The journal stops accepting lines after it was opened (full disk): the removal is reported, never lost.
        using var j = new TestWorkspace(); AddRemovalPair(j);
        var journal = Path.Combine(j.Root, "actions.csv");
        File.WriteAllText(journal, "header\r\n");
        var copy = Path.Combine(j.Source, "nested", "copy.bin");
        var journalScan = await ScanRemovalFixture(j);
        DuplicateRemovalResult blocked;
        using (var locker = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (OperatingSystem.IsWindows()) locker.Lock(locker.Length, 1024 * 1024);
            blocked = await new DuplicateRemoval { LogPath = journal }.DeleteAsync(journalScan, [copy], DuplicateRemovalMode.Permanent);
        }
        Check(!File.Exists(copy) && blocked.DeletedPaths.Single() == copy, "the deleted file is in the result");
        Check(blocked.Errors.Any(error => error.Contains("journal", StringComparison.OrdinalIgnoreCase) && error.Contains(copy, StringComparison.Ordinal)), string.Join("; ", blocked.Errors));

        // Moved aside through another spelling of the folder (a junction higher up, like a mapped drive), restored
        // through its real path.
        using var a = new TestWorkspace(); AddRemovalPair(a);
        var alias = Path.Combine(a.Root, "alias");
        await CreateJunction(alias, a.Root);
        var spelled = Path.Combine(alias, Path.GetFileName(a.Source));
        var aliasScan = await new DuplicateEngine().ScanAsync(spelled, new DuplicateScanOptions { MaxRetries = 0 });
        var aside = await new DuplicateRemoval().DeleteAsync(aliasScan, [Path.Combine(spelled, "nested", "copy.bin")], DuplicateRemovalMode.Quarantine);
        Check(aside.Complete, string.Join("; ", aside.Errors));
        var session = DuplicateRemoval.QuarantineSessions(a.Source).Single();
        Equal(1, (await new DuplicateRemoval().RestoreAsync(session.Path)).Restored, "a quarantine is restorable under the folder's real name");
        Check(File.Exists(Path.Combine(a.Source, "nested", "copy.bin")), "the file is back");
    }

    // COP-02, COP-04, COP-06: the mark of the Web, real clusters, names Windows would alter.
    private static async Task CopyKeepsMarksCountsClustersAndSkipsOddNames()
    {
        using var w = new TestWorkspace();
        var setup = w.FileAt(w.Source, "setup.exe", TestWorkspace.Bytes(20_000, 5));
        File.WriteAllText(setup + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        File.WriteAllText(setup + ":private.notes", "not copied");
        w.FileAt(w.Source, "a/one.txt", "1");
        w.FileAt(w.Source, "b/two.txt", "2");
        var odd = @"\\?\" + Path.Combine(w.Source, "draft ");
        File.WriteAllText(odd, "trailing space");
        var surrogate = Path.Combine(w.Source, "photo\uD83D.jpg");
        File.WriteAllText(surrogate, "lone surrogate");
        try
        {
            var plan = CopyEngine.CreatePlan(await Compare(w), true);
            Equal(2, plan.UnsupportedNames, "names Windows would alter are left out, without breaking the plan");
            var root = Path.GetPathRoot(Path.GetFullPath(w.Target))!;
            Check(GetDiskFreeSpaceW(root, out var sectors, out var bytes, out _, out _), "cluster size");
            var cluster = (long)sectors * bytes;
            Equal(((20_000 + cluster - 1) / cluster + 2 + 2) * cluster, CopyEngine.SpaceNeeds(plan).Single().Required, "each file and each new folder takes whole clusters");
            var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
            Check(copy.Complete, string.Join("; ", copy.Errors));
            var copied = Path.Combine(w.Target, "setup.exe");
            Equal("[ZoneTransfer]\r\nZoneId=3\r\n", File.ReadAllText(copied + ":Zone.Identifier"), "the mark of the Web is copied");
            Check(!File.Exists(copied + ":private.notes"), "other alternate streams are not");
        }
        finally
        {
            File.Delete(odd);
            File.Delete(surrogate);
        }
    }

    // CMP-06, CMP-11: deep trees, names with a final space, names that differ only by case.
    private static async Task ComparisonReadsLongAndOddNames()
    {
        using var w = new TestWorkspace();
        var deep = string.Join('\\', Enumerable.Repeat(new string('d', 60), 6));
        foreach (var root in new[] { w.Source, w.Target })
        {
            Directory.CreateDirectory(@"\\?\" + Path.Combine(root, deep));
            File.WriteAllText(@"\\?\" + Path.Combine(root, deep, "video.mp4"), "deep contents");
            File.WriteAllText(@"\\?\" + Path.Combine(root, "draft "), "trailing space");
        }
        w.FileAt(w.Source, "Makefile", "same");
        w.FileAt(w.Target, "makefile", "same");
        var result = await Compare(w);
        Check(result.Errors.Count == 0 && result.TreesIdentical, "a path over 300 characters and a final space are compared: " + string.Join("; ", result.Errors));
        Check(result.Rows.Any(row => row.RelativePath == "draft " && row.Status == ComparisonStatus.Identical), "the exact name is kept");
        Check(result.Rows.Single(row => row.RelativePath.Equals("Makefile", StringComparison.OrdinalIgnoreCase)).Detail.Contains("casse", StringComparison.Ordinal), "a case difference is said");

        var sensitive = w.DirectoryAt(w.Target, "cs");
        var start = new System.Diagnostics.ProcessStartInfo("fsutil.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "file", "setCaseSensitiveInfo", sensitive, "enable" }) start.ArgumentList.Add(argument);
        using (var process = System.Diagnostics.Process.Start(start)!)
        {
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                w.FileAt(w.Source, "cs/Readme", "text");
                w.FileAt(w.Target, "cs/README", "text");
                var cased = await Compare(w);
                Check(!cased.TreesIdentical && cased.Rows.Single(row => row.RelativePath.EndsWith("Readme", StringComparison.OrdinalIgnoreCase)).Status == ComparisonStatus.Conflict,
                    "in a case-sensitive folder, Readme and README are not the same name");
            }
        }
        foreach (var root in new[] { w.Source, w.Target })
        {
            File.Delete(@"\\?\" + Path.Combine(root, "draft "));
            File.Delete(@"\\?\" + Path.Combine(root, deep, "video.mp4"));
            for (var path = Path.Combine(root, deep); path.Length > root.Length; path = Path.GetDirectoryName(path)!) Directory.Delete(@"\\?\" + path);
        }
    }

    // CMP-10: a snapshot that contradicts itself is refused.
    private static Task SnapshotLoadRefusesContradictions()
    {
        using var w = new TestWorkspace();
        var hash = new string('A', 64);
        void Write(string name, object snapshot) => File.WriteAllText(Path.Combine(w.Root, name), JsonSerializer.Serialize(snapshot));
        Write("repeated.json", new { Schema = 1, Algorithm = "SHA256", Root = w.Source, Directories = Array.Empty<string>(), Complete = false,
            Files = new[] { new { Path = "a.txt", Size = 1, LastWriteUtc = DateTime.UtcNow, Sha256 = hash }, new { Path = "A.TXT", Size = 9, LastWriteUtc = DateTime.UtcNow, Sha256 = hash } } });
        Write("complete.json", new { Schema = 1, Algorithm = "SHA256", Root = w.Source, Directories = Array.Empty<string>(), Complete = true,
            Files = Array.Empty<object>(), Unverified = new[] { "intruder.exe" } });
        Throws<InvalidDataException>(() => SnapshotEngine.Load(Path.Combine(w.Root, "repeated.json")), "a path listed twice is refused");
        Throws<InvalidDataException>(() => SnapshotEngine.Load(Path.Combine(w.Root, "complete.json")), "complete with unverified files is refused");
        return Task.CompletedTask;
    }
}
