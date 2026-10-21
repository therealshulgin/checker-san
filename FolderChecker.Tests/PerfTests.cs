using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private const int PrefilterFixtureSize = 4 * 1024 * 1024 + 17;

    private static async Task PrefilterRejectsDifferentSamples()
    {
        using var w = new TestWorkspace();
        var first = TestWorkspace.Bytes(PrefilterFixtureSize);
        var second = (byte[])first.Clone();
        second[0] ^= 0xFF;
        w.FileAt(w.Source, "large.bin", first);
        w.FileAt(w.Target, "large.bin", second);
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "large.bin").Status, "sample mismatch status");
        Equal(0L, result.BytesRead, "different samples avoid both full-file reads");
        Equal(6L * 64 * 1024, result.SampleBytesRead, "three bounded blocks per file, reused by moved search");
        Equal(0L, result.FilesVerified, "sampling must not count as full verification");
        Check(result.SampleBytesRead < 2L * first.Length / 10, "prefilter reads under ten percent of the original work");
        Check(result.SnapshotVerified && result.Errors.Count == 0, "negative evidence still receives a stable final scan");
    }

    private static async Task PrefilterEqualSamplesRequireFullHash()
    {
        using var w = new TestWorkspace();
        var first = TestWorkspace.Bytes(PrefilterFixtureSize);
        var second = (byte[])first.Clone();
        second[1024 * 1024] ^= 0xFF; // Outside the start, middle and end blocks.
        w.FileAt(w.Source, "large.bin", first);
        w.FileAt(w.Target, "large.bin", second);
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "large.bin").Status, "difference outside samples found");
        Equal(2L * first.Length, result.BytesRead, "matching samples require both full SHA-256 hashes");
        Equal(6L * 64 * 1024, result.SampleBytesRead, "sample reads have their own byte counter");
        Equal(2L, result.FilesVerified, "both complete hashes calculated");
        Check(!result.ContentCovered, "matching samples cannot imply content coverage");
    }

    private static async Task PrefilterCanBeDisabled()
    {
        using var w = new TestWorkspace();
        var first = TestWorkspace.Bytes(PrefilterFixtureSize);
        var second = (byte[])first.Clone();
        second[0] ^= 0xFF;
        w.FileAt(w.Source, "large.bin", first);
        w.FileAt(w.Target, "large.bin", second);
        var result = await Compare(w, new ComparisonOptions { UseSamplePrefilter = false });
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "large.bin").Status, "disabled filter keeps verdict");
        Equal(2L * first.Length, result.BytesRead, "disabled filter performs normal SHA-256 reads");
        Equal(0L, result.SampleBytesRead, "disabled filter reads no samples");
    }

    private static async Task PrefilterKeepsMovedMatches()
    {
        using var w = new TestWorkspace();
        var first = TestWorkspace.Bytes(PrefilterFixtureSize);
        var second = (byte[])first.Clone();
        second[1024 * 1024] ^= 0xFF; // Same samples, distinct full digests.
        w.FileAt(w.Source, "source-a.bin", first);
        w.FileAt(w.Source, "source-b.bin", second);
        w.FileAt(w.Target, "target-a.bin", second);
        w.FileAt(w.Target, "target-b.bin", first);
        var result = await Compare(w);
        Equal(ComparisonStatus.Moved, SourceRow(result, w.Source, "source-a.bin").Status, "first moved file");
        Equal(ComparisonStatus.Moved, SourceRow(result, w.Source, "source-b.bin").Status, "second moved file");
        Check(result.ContentCovered && result.SnapshotVerified, "all true moved matches are found");
        Equal(4L * first.Length, result.BytesRead, "each complete file hash is read only once");
        Equal(12L * 64 * 1024, result.SampleBytesRead, "each sample signature is read only once");
        Equal(Path.Combine(w.Target, "target-b.bin"), SourceRow(result, w.Source, "source-a.bin").TargetPath!,
            "collision in sample signature cannot select wrong content");
        NoStrictVerdicts(result);
    }

    private static async Task PrefilterSkipsUnrelatedMovedCandidates()
    {
        using var w = new TestWorkspace();
        var first = TestWorkspace.Bytes(PrefilterFixtureSize);
        w.FileAt(w.Source, "source.bin", first);
        w.FileAt(w.Target, "noise-a.bin", TestWorkspace.Bytes(PrefilterFixtureSize, 123));
        w.FileAt(w.Target, "noise-b.bin", TestWorkspace.Bytes(PrefilterFixtureSize, 456));
        w.FileAt(w.Target, "renamed.bin", first);
        var result = await Compare(w);
        Equal(ComparisonStatus.Moved, SourceRow(result, w.Source, "source.bin").Status, "moved original found");
        Equal(2L * first.Length, result.BytesRead, "unrelated same-size candidates need no full hash");
        Equal(12L * 64 * 1024, result.SampleBytesRead, "four sample signatures");
        Check(result.ContentCovered, "content coverage retained");
    }

    private static async Task PrefilterPreservesCheckpointReuse()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows file identity reuse test");
        using var w = new TestWorkspace();
        var first = TestWorkspace.Bytes(PrefilterFixtureSize);
        w.FileAt(w.Source, "large.bin", first);
        w.FileAt(w.Target, "large.bin", first);
        var checkpoint = Path.Combine(w.Root, "checkpoint.json");
        AssertVerifiedIdentical(await Compare(w, CheckpointOptions(checkpoint, false)));
        var result = await Compare(w, CheckpointOptions(checkpoint));
        AssertVerifiedIdentical(result);
        Equal(2L, result.FilesReused, "both full hashes safely reused");
        Equal(0L, result.BytesRead, "resuming stable files reads no full data");
        Equal(0L, result.SampleBytesRead, "cached full hashes also bypass sample reads");
        var disabled = await Compare(w, CheckpointOptions(checkpoint) with { UseSamplePrefilter = false });
        AssertVerifiedIdentical(disabled);
        Equal(2L, disabled.FilesReused, "changing prefilter preference keeps valid complete hashes");
    }

    private static async Task PrefilterCancellationCannotConfirm()
    {
        using var w = new TestWorkspace();
        var first = TestWorkspace.Bytes(PrefilterFixtureSize);
        w.FileAt(w.Source, "large.bin", first);
        w.FileAt(w.Target, "large.bin", first);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            if (p.SampleBytesRead > 0) cancellation.Cancel();
        });
        var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new(), progress, cancellation.Token);
        AssertCancelled(result);
        Equal(0L, result.FilesVerified, "cancellation during sampling cannot claim a complete hash");
    }
}
