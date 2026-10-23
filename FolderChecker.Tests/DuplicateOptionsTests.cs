using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static async Task DuplicateFiltersLeaveOutNamesHiddenAndSmallFiles()
    {
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(100, 3);
        w.FileAt(w.Source, "a.bin", bytes);
        w.FileAt(w.Source, "sub/b.bin", bytes);
        w.FileAt(w.Source, "empty1.txt", []);
        w.FileAt(w.Source, "sub/empty2.txt", []);
        w.FileAt(w.Source, "x/Thumbs.db", "thumbnail cache");
        w.FileAt(w.Source, "y/Thumbs.db", "thumbnail cache");
        w.FileAt(w.Source, "node_modules/c.bin", bytes);
        var hidden = w.FileAt(w.Source, "hidden.bin", bytes);
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var unfiltered = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Equal(3, unfiltered.Groups.Count, "without filters, empty files and thumbnails are grouped too");

        var filtered = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions() with
        {
            MinimumSize = 1, ExcludedNames = ["thumbs.DB", "node_*"], SkipHiddenAndSystem = true
        });
        Check(filtered.SnapshotVerified && filtered.Errors.Count == 0, string.Join("; ", filtered.Errors));
        Equal(1, filtered.Groups.Count, "only the real duplicates remain");
        Equal("a.bin|" + Path.Combine("sub", "b.bin"), string.Join("|", filtered.Groups[0].Files.Select(f => f.RelativePath)), "excluded folder and hidden file are left out");
        Equal(5L, filtered.FilesFiltered, "two empty files, two thumbnails and one hidden file are counted as filtered");
        Equal(4L, filtered.FilesScanned, "the excluded folder is not listed at all");
    }

    private static Task DuplicateSelectionRulesPickOneKeeper()
    {
        DuplicateFile Copy(string relative, int year) => new()
        {
            RelativePath = relative, FullPath = Path.Combine(@"C:\r", relative), Size = 10,
            LastWriteUtc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var scan = new DuplicateResult
        {
            Root = @"C:\r", SnapshotVerified = true,
            Groups =
            [
                new() { Hash = new string('A', 64), Size = 10, Files = [Copy(@"z\a.txt", 2020), Copy("b.txt", 2018), Copy(@"deep\x\y\c.txt", 2022)] },
                new() { Hash = new string('B', 64), Size = 10, Files = [Copy(@"keep\d.bin", 2019), Copy("e.bin", 2021)] }
            ]
        };
        string[] Kept(KeepRule rule, string? folder = null)
        {
            var plan = DuplicateSelection.Extras(scan, rule, folder);
            return scan.Groups.SelectMany(g => g.Files).Where(f => !plan.Remove.Contains(f.FullPath)).Select(f => f.RelativePath).ToArray();
        }
        Equal("b.txt|e.bin", string.Join("|", Kept(KeepRule.FirstPath)), "first path alphabetically");
        Equal(@"b.txt|keep\d.bin", string.Join("|", Kept(KeepRule.Oldest)), "oldest modification date");
        Equal(@"deep\x\y\c.txt|e.bin", string.Join("|", Kept(KeepRule.Newest)), "newest modification date");
        Equal("b.txt|e.bin", string.Join("|", Kept(KeepRule.ShortestPath)), "fewest folders");
        var inFolder = DuplicateSelection.Extras(scan, KeepRule.InFolder, @"C:\r\keep");
        Equal(1, inFolder.UndecidedGroups, "a group without a copy in the folder is left alone");
        Equal(@"C:\r\e.bin", inFolder.Remove.Single(), "only the decided group loses a copy");
        return Task.CompletedTask;
    }

    private static async Task DuplicateRemovalRecyclesAndLogs()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        if (!DuplicateRemoval.RecycleBinAvailable(w.Source)) throw new SkipTestException("no Recycle Bin on the test drive");
        Check(!DuplicateRemoval.RecycleBinAvailable(@"\\server\share\folder"), "network shares have no Recycle Bin");
        var scan = await ScanRemovalFixture(w);
        var remove = Path.Combine(w.Source, "nested", "copy.bin");
        var log = Path.Combine(w.Root, "logs", "actions.csv");
        var result = await new DuplicateRemoval { LogPath = log }.DeleteAsync(scan, [remove], DuplicateRemovalMode.RecycleBin);
        Check(result.Complete, string.Join("; ", result.Errors));
        Equal(1024L * 1024 + 17, result.BytesRecycled, "recycled bytes reported");
        Check(!File.Exists(remove) && File.Exists(Path.Combine(w.Source, "keep.bin")), "only the selected copy left its folder");
        var lines = File.ReadAllLines(log);
        Equal(2, lines.Length, "a header and one line per action");
        var fields = lines[1].Split(';').Select(field => field.Trim('"')).ToArray();
        Equal(remove, fields[2], "the journal names the removed file");
        Equal(scan.Groups.Single().Hash, fields[3], "the journal proves its contents");
        Equal(Path.Combine(w.Source, "keep.bin"), fields[5], "the journal names the copy kept");
        var recycled = fields[6];
        try
        {
            Check(recycled.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase) && File.Exists(recycled), "the verified file itself is in the Recycle Bin");
            Check(File.ReadAllBytes(recycled).SequenceEqual(TestWorkspace.Bytes(1024 * 1024 + 17)), "recycled contents are intact");
        }
        finally
        {
            // Leave the Recycle Bin as it was before the test.
            if (recycled.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase) && File.Exists(recycled))
            {
                File.Delete(recycled);
                var info = Path.Combine(Path.GetDirectoryName(recycled)!, "$I" + Path.GetFileName(recycled)[2..]);
                if (File.Exists(info)) File.Delete(info);
            }
        }
    }

    private static async Task DuplicateQuarantineRestoresWithoutOverwriting()
    {
        using var w = new TestWorkspace(); AddRemovalPair(w);
        w.FileAt(w.Source, "third/copy.bin", TestWorkspace.Bytes(1024 * 1024 + 17));
        var scan = await ScanRemovalFixture(w);
        var first = Path.Combine(w.Source, "nested", "copy.bin");
        var second = Path.Combine(w.Source, "third", "copy.bin");
        var moved = await new DuplicateRemoval().DeleteAsync(scan, [first, second], DuplicateRemovalMode.Quarantine);
        Check(moved.Complete, string.Join("; ", moved.Errors));
        var session = DuplicateRemoval.QuarantineSessions(w.Source).Single();
        Equal(2, session.Waiting, "both quarantined files can be restored");

        w.FileAt(w.Source, "third/copy.bin", "a new file in the old place");
        var partly = await new DuplicateRemoval().RestoreAsync(session.Path);
        Equal(1, partly.Restored, "the free path is restored");
        Check(!partly.Complete && partly.Errors.Count == 1, "the occupied path is reported");
        Check(File.ReadAllBytes(first).SequenceEqual(TestWorkspace.Bytes(1024 * 1024 + 17)), "restored to its original path");
        Equal("a new file in the old place", File.ReadAllText(second), "nothing is overwritten");
        Equal(1, DuplicateRemoval.QuarantineSessions(w.Source).Single().Waiting, "the blocked file stays restorable");

        File.Delete(second);
        var rest = await new DuplicateRemoval().RestoreAsync(session.Path);
        Check(rest.Complete && rest.Restored == 1, string.Join("; ", rest.Errors));
        Check(!Directory.Exists(session.Path) && DuplicateRemoval.QuarantineSessions(w.Source).Count == 0, "an emptied session is removed");
    }
}
