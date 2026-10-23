using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using FolderChecker.Cli;
using FolderChecker.Core;

namespace FolderChecker.Tests;

// Lot A of the adversarial review (revue/ARBITRAGE.md): verdicts that must never be wrong.
internal static partial class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr security);

    private sealed class SnapshotInline(Action<SnapshotProgress> callback) : IProgress<SnapshotProgress>
    {
        public void Report(SnapshotProgress value) => callback(value);
    }

    private static Func<string[], Task<(int Code, string Output)>> CliRunner(string log) => async args =>
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CheckerSanCli.RunAsync([.. args, "--lang", "en", "--log", log], output, error);
        return (code, output + error.ToString());
    };

    // CMP-01: a real folder facing a junction is not identical, whichever side holds the junction.
    private static async Task LinksFacingRealItemsAreNeverClean()
    {
        using var w = new TestWorkspace();
        var run = CliRunner(Path.Combine(w.Root, "activity.log"));
        var outside = w.DirectoryAt(w.Root, "elsewhere");
        w.FileAt(outside, "a.jpg", "photo a");
        w.FileAt(w.Source, "notes.txt", "same");
        w.FileAt(w.Target, "notes.txt", "same");
        w.FileAt(w.Source, "Photos/a.jpg", "photo a");
        await CreateJunction(Path.Combine(w.Target, "Photos"), outside);
        var facing = await run(["compare", w.Source, w.Target]);
        Equal(CheckerSanCli.Incomplete, facing.Code, facing.Output);
        Check(!facing.Output.Contains("identical", StringComparison.Ordinal) && facing.Output.Contains("Real items face links", StringComparison.Ordinal), facing.Output);

        var store = Path.Combine(w.Root, "store", "pairs.json");
        var pair = PairStore.Save(new SavedPair { Name = "Backup", Folder1 = w.Source, Folder2 = w.Target }, store);
        var checkedPair = await PairCheck.RunAsync(pair, store, log: Path.Combine(w.Root, "activity.log"));
        Check(checkedPair.ExitCode == ExitCodes.Incomplete && checkedPair.Links > 0, $"scheduled check: exit {checkedPair.ExitCode}, links {checkedPair.Links}");
        L.SetLanguage("en");
        try { Check(PairStore.Find(pair.Id, store)!.LastRunText.Contains("behind links not followed", StringComparison.Ordinal), "the pair list names the links"); }
        finally { L.SetLanguage("fr"); }

        using var inverse = new TestWorkspace();
        var inverseRun = CliRunner(Path.Combine(inverse.Root, "activity.log"));
        var inverseOutside = inverse.DirectoryAt(inverse.Root, "elsewhere");
        inverse.FileAt(inverseOutside, "a.jpg", "photo a");
        await CreateJunction(Path.Combine(inverse.Source, "Photos"), inverseOutside);
        inverse.FileAt(inverse.Target, "Photos/a.jpg", "photo a");
        Equal(CheckerSanCli.Incomplete, (await inverseRun(["compare", inverse.Source, inverse.Target])).Code, "a junction in folder 1 facing real files is incomplete too");

        using var both = new TestWorkspace();
        var bothRun = CliRunner(Path.Combine(both.Root, "activity.log"));
        await CreateJunction(Path.Combine(both.Source, "Photos"), both.DirectoryAt(both.Root, "one"));
        await CreateJunction(Path.Combine(both.Target, "Photos"), both.DirectoryAt(both.Root, "two"));
        both.FileAt(both.Source, "notes.txt", "same");
        both.FileAt(both.Target, "notes.txt", "same");
        var matching = await bothRun(["compare", both.Source, both.Target]);
        Equal(CheckerSanCli.Clean, matching.Code, matching.Output);
    }

    // CMP-02: writes that keep the directory entry unchanged are caught by the identity check on a handle.
    private static async Task FinalCheckSeesWritesListingsMiss()
    {
        using var w = new TestWorkspace();
        var content = TestWorkspace.Bytes(300_000, 5);
        var track = w.FileAt(w.Source, "album/track01.mp3", content);
        w.FileAt(w.Target, "album/track01.mp3", content);
        var edited = false;
        var inPlace = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(), new InlineProgress(p =>
        {
            if (edited || !p.Phase.Contains("final", StringComparison.OrdinalIgnoreCase)) return;
            edited = true;
            var written = File.GetLastWriteTimeUtc(track);
            using (var stream = new FileStream(track, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { stream.Position = 10; stream.Write([1, 2, 3, 4, 5, 6, 7, 8, 9]); }
            File.SetLastWriteTimeUtc(track, written);
        }));
        Check(edited, "the file was edited during the final check");
        Check(!inPlace.SnapshotVerified && !inPlace.TreesIdentical, "an in-place edit with its date restored is detected");
        Check(inPlace.Rows.Single(row => row.RelativePath.EndsWith("track01.mp3", StringComparison.Ordinal)).Status == ComparisonStatus.Unverified, "the edited file is no longer identical");
        Check(inPlace.Errors.Any(error => error.Contains("track01.mp3", StringComparison.Ordinal)), string.Join("; ", inPlace.Errors));

        using var linked = new TestWorkspace();
        var video = linked.FileAt(linked.Source, "video.mp4", TestWorkspace.Bytes(2 * 1024 * 1024, 6));
        linked.FileAt(linked.Target, "video.mp4", TestWorkspace.Bytes(2 * 1024 * 1024, 6));
        var otherName = Path.Combine(linked.Root, "other-name.mp4");
        if (!CreateHardLinkW(otherName, video, IntPtr.Zero)) throw new SkipTestException("hard links are not available");
        var appended = false;
        var throughLink = await new ComparisonEngine().CompareAsync(linked.Source, linked.Target, new ComparisonOptions(), new InlineProgress(p =>
        {
            if (appended || !p.Phase.Contains("final", StringComparison.OrdinalIgnoreCase)) return;
            appended = true;
            using var stream = new FileStream(otherName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(new byte[1024 * 1024]);
        }));
        Check(appended && !throughLink.SnapshotVerified && !throughLink.TreesIdentical, "a write through another hard link is detected");

        using var snap = new TestWorkspace();
        var doc = snap.FileAt(snap.Source, "doc.bin", TestWorkspace.Bytes(100_000, 7));
        var changed = false;
        var snapshot = await new SnapshotEngine().CreateAsync(snap.Source, null, new SnapshotInline(p =>
        {
            if (changed || p.FilesHashed != p.FilesTotal) return;
            changed = true;
            var written = File.GetLastWriteTimeUtc(doc);
            using (var stream = new FileStream(doc, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { stream.Position = 500; stream.Write([9, 9, 9]); }
            File.SetLastWriteTimeUtc(doc, written);
        }));
        Check(changed && !snapshot.Complete && snapshot.Errors.Any(error => error.Contains("doc.bin", StringComparison.Ordinal)), "a snapshot sees an edit made after the file was read");
        Check(snapshot.Files.Count == 0 && snapshot.Unverified.Contains("doc.bin"), "the edited file has no recorded hash");
    }

    // AUT-02: an unreadable folder is neither missing nor added, and an error is never hidden behind differences.
    private static async Task VerifyTreatsUnreadableFoldersAsUnknown()
    {
        using var w = new TestWorkspace();
        var run = CliRunner(Path.Combine(w.Root, "activity.log"));
        var keep = w.FileAt(w.Source, "keep.txt", "kept");
        for (var i = 1; i <= 3; i++) w.FileAt(w.Source, $"Photos/img{i}.jpg", $"photo {i}");
        var photos = Path.Combine(w.Source, "Photos");
        var reference = Path.Combine(w.Root, "reference.json");
        Equal(CheckerSanCli.Clean, (await run(["snapshot", w.Source, reference])).Code, "the reference snapshot is complete");
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        async Task Acl(params string[] args)
        {
            var start = new ProcessStartInfo("icacls.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            start.ArgumentList.Add(photos);
            foreach (var argument in args) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new SkipTestException("icacls cannot change the test folder");
        }
        await Acl("/deny", user + ":(RD)");
        try
        {
            var unreadable = await run(["verify", w.Source, reference]);
            Equal(CheckerSanCli.Incomplete, unreadable.Code, unreadable.Output);
            Check(!unreadable.Output.Contains("MISSING", StringComparison.Ordinal) && unreadable.Output.Contains("! ", StringComparison.Ordinal), unreadable.Output);
            var during = Path.Combine(w.Root, "during.json");
            Equal(CheckerSanCli.Incomplete, (await run(["snapshot", w.Source, during])).Code, "a snapshot with an unreadable folder is incomplete");
            Equal("Photos", SnapshotEngine.Load(during).UnreadableFolders.Single(), "the unreadable folder is recorded");
            await Acl("/remove:d", user);
            var later = await run(["verify", w.Source, during]);
            Equal(CheckerSanCli.Incomplete, later.Code, later.Output);
            Check(!later.Output.Contains("ADDED", StringComparison.Ordinal) && later.Output.Contains("snapshot itself is incomplete", StringComparison.Ordinal), later.Output);
        }
        finally { await Acl("/remove:d", user); }
        Equal(CheckerSanCli.Clean, (await run(["verify", w.Source, reference])).Code, "readable again, the folder matches");

        // A file that is only online leaves the result uncertain, without hiding a change that was proven.
        var cloud = w.FileAt(w.Source, "cloud.bin", "online only");
        File.SetAttributes(cloud, FileAttributes.Offline);
        try
        {
            var withCloud = Path.Combine(w.Root, "cloud.json");
            Equal(CheckerSanCli.Incomplete, (await run(["snapshot", w.Source, withCloud])).Code, "an online-only file is not proven");
            File.WriteAllText(keep, "changed since the snapshot");
            var changed = await run(["verify", w.Source, withCloud]);
            Equal(CheckerSanCli.Differences, changed.Code, changed.Output);
        }
        finally { File.SetAttributes(cloud, FileAttributes.Normal); }
    }

    // Review follow-up: the same folder under two spellings, such as a NAS by name and by address.
    private static async Task RootsAreRecognizedByIdentity()
    {
        using var w = new TestWorkspace();
        var alias = Path.Combine(w.Root, "alias");
        await CreateJunction(alias, w.Source);
        w.DirectoryAt(w.Source, "sub");
        var method = typeof(ComparisonEngine).GetMethod("RelatedByIdentity", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Related(string first, string second) => (bool)method.Invoke(null, [first, second])!;
        Check(Related(w.Source, alias), "one folder under two names is recognized");
        Check(Related(Path.Combine(alias, "sub"), w.Source), "a folder inside the other, under another name, is recognized");
        Check(Related(w.Source, Path.Combine(alias, "sub")), "in both orders");
        Check(!Related(w.Source, w.Target), "separate folders stay separate");
    }

    // DUP-07: names of one character used to fail the rename by handle (error 24).
    private static async Task OneCharacterNamesAreCopiedAndMovedAside()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "a", "one letter");
        w.FileAt(w.Source, "sub/x", "one letter in a folder");
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
        Check(copy.Complete, string.Join("; ", copy.Errors));
        Check(File.Exists(Path.Combine(w.Target, "a")) && File.Exists(Path.Combine(w.Target, "sub", "x")), "one-letter files are copied");
        Equal(0, Directory.GetFiles(w.Target, "*.partial", SearchOption.AllDirectories).Length, "no temporary file is left");

        using var d = new TestWorkspace();
        d.FileAt(d.Source, "keep.bin", TestWorkspace.Bytes(5000, 3));
        d.FileAt(d.Source, "b", TestWorkspace.Bytes(5000, 3));
        var scan = await new DuplicateEngine().ScanAsync(d.Source, new DuplicateScanOptions { MaxRetries = 0 });
        var single = scan.Groups.Single().Files.Single(file => file.RelativePath == "b").FullPath;
        var moved = await new DuplicateRemoval { LogPath = Path.Combine(d.Root, "actions.csv") }.DeleteAsync(scan, [single], DuplicateRemovalMode.Quarantine);
        Check(moved.Complete && !File.Exists(single), string.Join("; ", moved.Errors));
    }

    // CMP-05: two names that differ only by case must not merge into one snapshot entry.
    private static async Task SnapshotRefusesCaseCollisions()
    {
        using var w = new TestWorkspace();
        var folder = w.DirectoryAt(w.Source, "sensitive");
        var start = new ProcessStartInfo("fsutil.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "file", "setCaseSensitiveInfo", folder, "enable" }) start.ArgumentList.Add(argument);
        using (var process = Process.Start(start)!)
        {
            await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new SkipTestException("case-sensitive folders are not available");
        }
        var upper = Path.Combine(folder, "Report.pdf");
        var lower = Path.Combine(folder, "report.pdf");
        File.WriteAllBytes(upper, TestWorkspace.Bytes(1000, 1));
        File.WriteAllBytes(lower, TestWorkspace.Bytes(1000, 2));
        var when = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        foreach (var path in new[] { upper, lower }) { File.SetCreationTimeUtc(path, when); File.SetLastWriteTimeUtc(path, when); }
        var snapshot = await new SnapshotEngine().CreateAsync(w.Source);
        Check(!snapshot.Complete && snapshot.Errors.Any(error => error.Contains("Report.pdf", StringComparison.Ordinal) && error.Contains("report.pdf", StringComparison.Ordinal)),
            "a case collision makes the snapshot incomplete: " + string.Join("; ", snapshot.Errors));
    }
}
