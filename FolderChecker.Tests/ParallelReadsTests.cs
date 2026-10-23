using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static async Task ParallelReadsGiveIdenticalResults()
    {
        using var w = new TestWorkspace();
        for (var i = 0; i < 40; i++)
        {
            // Large files take the sample prefilter; some differ, some are missing, some also exist elsewhere.
            var bytes = TestWorkspace.Bytes(i % 3 == 0 ? 5 * 1024 * 1024 + i : 70_000 + i, i);
            w.FileAt(w.Source, $"set{i % 4}/f{i}.bin", bytes);
            if (i % 5 != 0) w.FileAt(w.Target, $"set{i % 4}/f{i}.bin", i % 7 == 0 ? TestWorkspace.Bytes(bytes.Length, i + 1000) : bytes);
            if (i % 6 == 0) w.FileAt(w.Target, $"moved/f{i}.bin", bytes);
        }
        static string Rows(ComparisonResult result) =>
            string.Join("\n", result.Rows.Select(row => $"{row.RelativePath}|{row.Status}|{row.SourceHash}|{row.TargetHash}|{row.TargetPath}")) +
            $"\n{result.SourceFullyContained}|{result.TargetFullyContained}|{result.Errors.Count}";
        var one = await Compare(w, new ComparisonOptions { ParallelReads = 1 });
        var four = await Compare(w, new ComparisonOptions { ParallelReads = 4 });
        Check(one.SnapshotVerified && four.SnapshotVerified, string.Join("; ", one.Errors.Concat(four.Errors)));
        Equal(Rows(one), Rows(four), "reading four files at once changes nothing in the comparison");
        Equal(one.BytesRead, four.BytesRead, "exactly the same contents are read");
        Equal(one.SampleBytesRead, four.SampleBytesRead, "exactly the same samples are read");

        static string Groups(DuplicateResult result) =>
            string.Join("\n", result.Groups.Select(group => group.Hash + ":" + string.Join(",", group.Files.Select(file => file.RelativePath))));
        var dupOne = await new DuplicateEngine().ScanAsync(w.Target, DuplicateOptions() with { ParallelReads = 1 });
        var dupFour = await new DuplicateEngine().ScanAsync(w.Target, DuplicateOptions() with { ParallelReads = 4 });
        Check(dupOne.SnapshotVerified && dupFour.SnapshotVerified && dupOne.Groups.Count > 0, "the fixture holds verified duplicates");
        Equal(Groups(dupOne), Groups(dupFour), "reading four files at once finds the same duplicate groups");
        Equal(dupOne.BytesRead, dupFour.BytesRead, "duplicate search reads the same bytes");
    }
}
