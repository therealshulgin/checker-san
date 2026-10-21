using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static async Task BidirectionalSourceSubset()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        w.FileAt(w.Target, "extra/report.txt", "only in folder 2");
        w.DirectoryAt(w.Target, "extra/empty");
        await AssertContainmentInBothOrders(w, sourceContained: true, targetContained: false);
    }

    private static async Task BidirectionalDuplicateSubset()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(521);
        w.FileAt(w.Source, "common.bin", data);
        w.FileAt(w.Source, "duplicate.bin", data);
        w.FileAt(w.Target, "common.bin", data);

        var result = await Compare(w);
        var moved = SourceRow(result, w.Source, "duplicate.bin");
        Equal(ComparisonStatus.Moved, moved.Status, "extra duplicate is classified as moved");
        Equal(Path.Combine(w.Target, "common.bin"), moved.TargetPath,
            "moved row points to an existing target file at a different relative path");
        AssertContainment(result, sourceContained: false, targetContained: true);
        await AssertContainmentInBothOrders(w, sourceContained: false, targetContained: true);
    }

    private static async Task BidirectionalSeparateExtras()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        w.FileAt(w.Source, "only-first.txt", "first");
        w.FileAt(w.Target, "only-second.txt", "second");
        w.DirectoryAt(w.Source, "first-empty");
        w.DirectoryAt(w.Target, "second-empty");
        await AssertContainmentInBothOrders(w, sourceContained: false, targetContained: false);
    }

    private static async Task BidirectionalMovedOnly()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(321);
        w.FileAt(w.Source, "original.bin", data);
        w.FileAt(w.Target, "renamed.bin", data);
        var result = await Compare(w);
        Check(result.ContentCovered, "content-only coverage may be true despite different paths");
        AssertContainment(result, sourceContained: false, targetContained: false);
        await AssertContainmentInBothOrders(w, sourceContained: false, targetContained: false);
    }

    private static async Task BidirectionalEmptyDirectories()
    {
        using var w = new TestWorkspace();
        await AssertContainmentInBothOrders(w, sourceContained: true, targetContained: true);
        w.DirectoryAt(w.Target, "empty/nested");
        await AssertContainmentInBothOrders(w, sourceContained: true, targetContained: false);
        w.DirectoryAt(w.Source, "empty/nested");
        await AssertContainmentInBothOrders(w, sourceContained: true, targetContained: true);
    }

    private static async Task BidirectionalContentMismatch()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "same-path.bin", "AAAA");
        w.FileAt(w.Target, "same-path.bin", "BBBB");
        w.FileAt(w.Source, "copy-of-second.bin", "BBBB");
        w.FileAt(w.Target, "copy-of-first.bin", "AAAA");
        var result = await Compare(w);
        Check(result.ContentCovered, "copies elsewhere provide source content coverage");
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "same-path.bin").Status,
            "same-path mismatch remains visible");
        await AssertContainmentInBothOrders(w, sourceContained: false, targetContained: false);
    }

    private static async Task BidirectionalCancelledFinalCheck()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            if (p.Phase == "Contrôle final des modifications") cancellation.Cancel();
        });
        var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target,
            new ComparisonOptions(), progress, cancellation.Token);
        Check(cancellation.IsCancellationRequested, "cancellation occurs after hashes have matched");
        Equal(ComparisonStatus.Identical, SourceRow(result, w.Source, "common.wav").Status,
            "individual content match remains available in partial results");
        AssertCancelled(result);
    }

    private static async Task AssertContainmentInBothOrders(TestWorkspace w,
        bool sourceContained, bool targetContained)
    {
        foreach (var findMoved in new[] { false, true })
        {
            var options = new ComparisonOptions(findMoved);
            var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, options);
            AssertContainment(result, sourceContained, targetContained);
            var reversed = await new ComparisonEngine().CompareAsync(w.Target, w.Source, options);
            AssertContainment(reversed, targetContained, sourceContained);
            Equal(result.SourceFullyContained, reversed.TargetFullyContained,
                "swapping folders preserves the first directional result");
            Equal(result.TargetFullyContained, reversed.SourceFullyContained,
                "swapping folders preserves the second directional result");
        }
    }

    private static void AssertContainment(ComparisonResult result,
        bool sourceContained, bool targetContained)
    {
        Check(result.SnapshotVerified && !result.Cancelled, "complete stable snapshot required by fixture");
        Equal(0, result.Errors.Count, "containment fixture has no errors");
        Equal(sourceContained, result.SourceFullyContained, "folder 1 contained in folder 2");
        Equal(targetContained, result.TargetFullyContained, "folder 2 contained in folder 1");
        Equal(sourceContained && targetContained, result.TreesIdentical,
            "tree identity is mutual strict containment");
    }
}
