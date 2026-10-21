using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static ComparisonOptions CheckpointOptions(string path, bool resume = true) =>
        new() { CheckpointPath = path, ResumeFromCheckpoint = resume, MaxRetries = 0, InitialRetryDelayMs = 1 };

    private static void AddCheckpointFiles(TestWorkspace w)
    {
        for (var index = 0; index < 3; index++)
        {
            var bytes = TestWorkspace.Bytes(256 * 1024 + 17 + index, 700 + index);
            w.FileAt(w.Source, $"file-{index}.bin", bytes);
            w.FileAt(w.Target, $"file-{index}.bin", bytes);
        }
    }

    private static async Task CheckpointCancellationAndResume()
    {
        using var w = new TestWorkspace();
        AddCheckpointFiles(w);
        var checkpoint = Path.Combine(w.Root, "partial-checkpoint.json");
        using var cancellation = new CancellationTokenSource();
        string? completedPath = null;
        var progress = new InlineProgress(p =>
        {
            if (p.FilesHashed == 0 || cancellation.IsCancellationRequested) return;
            completedPath = p.CurrentPath;
            cancellation.Cancel();
        });
        var partial = await new ComparisonEngine().CompareAsync(w.Source, w.Target,
            CheckpointOptions(checkpoint, false), progress, cancellation.Token);
        AssertCancelled(partial);
        Check(partial.FilesVerified > 0 && partial.FilesVerified < 6, "cancellation follows at least one completed hash");
        Check(partial.CheckpointSaved && partial.HasCheckpoint, "completed partial hashes are saved on cancellation");
        Check(File.Exists(checkpoint), "partial checkpoint is persisted to disk");
        Check(!string.IsNullOrWhiteSpace(completedPath), "completed file path was reported");
        var savedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(completedPath!)));
        Check(File.ReadAllText(checkpoint).Contains(savedHash, StringComparison.OrdinalIgnoreCase),
            "checkpoint contains the independently verified completed digest");

        // A distinct engine instance must load persisted state, not retain in-memory hashes.
        var resumed = await new ComparisonEngine().CompareAsync(w.Source, w.Target, CheckpointOptions(checkpoint));
        AssertVerifiedIdentical(resumed);
        Equal(partial.FilesVerified, resumed.FilesReused, "only previously completed files are reused");
        var totalBytes = 2L * Enumerable.Range(0, 3).Sum(index => 256 * 1024 + 17 + index);
        Equal(totalBytes, partial.BytesRead + resumed.BytesRead, "completed file content is not reread after resume");
        Check(resumed.CheckpointSaved, "resumed work updates its persistent checkpoint");
    }

    private static async Task CheckpointInvalidatesChangedFile()
    {
        using var w = new TestWorkspace();
        AddCheckpointFiles(w);
        var checkpoint = Path.Combine(w.Root, "checkpoint.json");
        AssertVerifiedIdentical(await Compare(w, CheckpointOptions(checkpoint, false)));
        var modifiedPath = Path.Combine(w.Source, "file-1.bin");
        var originalWriteTime = File.GetLastWriteTimeUtc(modifiedPath);
        var bytes = File.ReadAllBytes(modifiedPath);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(modifiedPath, bytes);
        File.SetLastWriteTimeUtc(modifiedPath, originalWriteTime.AddSeconds(5));

        var result = await Compare(w, CheckpointOptions(checkpoint));
        AssertOnlyChangedFileRehashed(w, result, bytes.Length);
    }

    private static async Task CheckpointDetectsRestoredTimestamps()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows native ChangeTime fingerprint test");
        using var w = new TestWorkspace();
        AddCheckpointFiles(w);
        var checkpoint = Path.Combine(w.Root, "checkpoint.json");
        AssertVerifiedIdentical(await Compare(w, CheckpointOptions(checkpoint, false)));
        var modifiedPath = Path.Combine(w.Source, "file-1.bin");
        var originalWriteTime = File.GetLastWriteTimeUtc(modifiedPath);
        var originalCreationTime = File.GetCreationTimeUtc(modifiedPath);
        var bytes = File.ReadAllBytes(modifiedPath);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(modifiedPath, bytes);
        File.SetCreationTimeUtc(modifiedPath, originalCreationTime);
        File.SetLastWriteTimeUtc(modifiedPath, originalWriteTime);
        Equal(originalWriteTime, File.GetLastWriteTimeUtc(modifiedPath), "last-write timestamp was restored");
        Equal(originalCreationTime, File.GetCreationTimeUtc(modifiedPath), "creation timestamp was restored");

        var result = await Compare(w, CheckpointOptions(checkpoint));
        AssertOnlyChangedFileRehashed(w, result, bytes.Length);
    }

    private static void AssertOnlyChangedFileRehashed(TestWorkspace w, ComparisonResult result, long length)
    {
        NoStrictVerdicts(result);
        Check(!result.ContentCovered, "changed source bytes are not covered by stale cached hashes");
        Equal(5L, result.FilesReused, "five unchanged files reuse their completed digests");
        Equal(length, result.BytesRead, "only the changed same-size file is read again");
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "file-1.bin").Status,
            "changed content is reported as different");
        Check(result.SnapshotVerified, "current inventory remains stable during comparison");
        Equal(0, result.Errors.Count, "ordinary checkpoint invalidation is not a scan failure");
    }

    private static async Task CheckpointRejectsDifferentRoots()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        var checkpoint = Path.Combine(w.Root, "checkpoint.json");
        AssertVerifiedIdentical(await Compare(w, CheckpointOptions(checkpoint, false)));
        var secondSource = w.DirectoryAt(w.Root, "second-source");
        var secondTarget = w.DirectoryAt(w.Root, "second-target");
        const string content = "one common audio recording";
        w.FileAt(secondSource, "common.wav", content);
        w.FileAt(secondTarget, "common.wav", content);
        var result = await new ComparisonEngine().CompareAsync(secondSource, secondTarget, CheckpointOptions(checkpoint));
        AssertVerifiedIdentical(result);
        Equal(0L, result.FilesReused, "checkpoint entries from different roots are discarded");
        Equal(2L * System.Text.Encoding.UTF8.GetByteCount(content), result.BytesRead,
            "both new roots are freshly hashed");
        Check(result.Warnings.Count > 0, "mismatched checkpoint produces an explanatory warning");
    }

    private static async Task MalformedCheckpointIsSafe()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        var checkpoint = w.FileAt(w.Root, "checkpoint.json", "{\"unfinished\":");
        var result = await Compare(w, CheckpointOptions(checkpoint));
        AssertVerifiedIdentical(result);
        Equal(0L, result.FilesReused, "malformed checkpoint cannot supply reusable hashes");
        Check(result.BytesRead > 0, "all content is freshly read after malformed input");
        Check(result.Warnings.Count > 0, "malformed checkpoint explains the fresh comparison");
    }

    private static async Task CheckpointCannotWriteSelectedTrees()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        foreach (var root in new[] { w.Source, w.Target })
        {
            var checkpoint = Path.Combine(root, "must-not-create", "checkpoint.json");
            await ThrowsAsync<ArgumentException>(() => Compare(w, CheckpointOptions(checkpoint)));
            Check(!File.Exists(checkpoint), "checkpoint file is not written inside an input tree");
            Check(!Directory.Exists(Path.GetDirectoryName(checkpoint)), "checkpoint parent is not created inside an input tree");
        }
    }

    private static async Task CheckpointCannotWriteViaJunction()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows junction output safety test");
        using var w = new TestWorkspace();
        AddCommonFile(w);
        var alias = Path.Combine(w.Root, "output-alias");
        await CreateJunction(alias, w.Source);
        var checkpoint = Path.Combine(alias, "must-not-create", "checkpoint.json");
        await ThrowsAsync<ArgumentException>(() => Compare(w, CheckpointOptions(checkpoint)));
        Check(!File.Exists(checkpoint), "alias cannot direct a checkpoint into a selected tree");
        Check(!Directory.Exists(Path.Combine(w.Source, "must-not-create")), "alias rejection precedes directory creation");
    }

    private static async Task CachedHashDoesNotHideLockedFile()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows sharing-mode test");
        using var w = new TestWorkspace();
        AddCommonFile(w);
        var checkpoint = Path.Combine(w.Root, "checkpoint.json");
        AssertVerifiedIdentical(await Compare(w, CheckpointOptions(checkpoint, false)));
        using var held = new FileStream(Path.Combine(w.Source, "common.wav"), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);
        var result = await Compare(w, CheckpointOptions(checkpoint));
        AssertUncertain(result);
        Equal(ComparisonStatus.Unverified, SourceRow(result, w.Source, "common.wav").Status,
            "cached source digest cannot make an inaccessible file verified");
    }

    private static IOException SimulatedNetworkFailure() =>
        new("Simulated network name no longer available", unchecked((int)0x80070040));

    private static async Task SimulatedNetworkRetries()
    {
        var calls = 0;
        var reportedRetries = new List<(int Attempt, TimeSpan Delay)>();
        var value = await FileSystemRetryPolicy.ExecuteAsync<int>(_ =>
        {
            calls++;
            return calls < 3 ? Task.FromException<int>(SimulatedNetworkFailure()) : Task.FromResult(42);
        }, 3, 1, (attempt, delay) => reportedRetries.Add((attempt, delay)));
        Equal(42, value, "successful retry returns the operation result");
        Equal(3, calls, "two failures followed by one successful attempt");
        Equal(2, reportedRetries.Count, "only failures generate retry callbacks");
        Equal(1, reportedRetries[0].Attempt, "first retry attempt index");
        Equal(2, reportedRetries[1].Attempt, "second retry attempt index");
        Check(reportedRetries[0].Delay > TimeSpan.Zero && reportedRetries[1].Delay >= reportedRetries[0].Delay,
            "retry delays are positive and do not shrink");
    }

    private static async Task SimulatedRetryExhaustion()
    {
        var calls = 0;
        var reportedRetries = 0;
        var failure = SimulatedNetworkFailure();
        try
        {
            await FileSystemRetryPolicy.ExecuteAsync<int>(_ =>
            {
                calls++;
                return Task.FromException<int>(failure);
            }, 2, 1, (_, _) => reportedRetries++);
            throw new InvalidOperationException("Exhausted retries must not return a successful result.");
        }
        catch (IOException actual)
        {
            Check(ReferenceEquals(failure, actual), "the original network failure is preserved after exhaustion");
        }
        Equal(3, calls, "initial attempt plus exactly two retries");
        Equal(2, reportedRetries, "retry count is bounded by configuration");
    }

    private static async Task CancelDuringRetryDelay()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var operation = FileSystemRetryPolicy.ExecuteAsync<int>(_ =>
        {
            calls++;
            return Task.FromException<int>(SimulatedNetworkFailure());
        }, 3, 10_000, (_, _) => cancellation.Cancel(), cancellation.Token);
        await ThrowsAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(2)));
        Equal(1, calls, "cancellation prevents another filesystem attempt");
    }

    private static async Task PermanentFailureNotRetried()
    {
        Check(FileSystemRetryPolicy.IsTransient(SimulatedNetworkFailure()), "known network HRESULT is transient");
        Check(!FileSystemRetryPolicy.IsTransient(new UnauthorizedAccessException("denied")),
            "permission denial is permanent");
        Check(!FileSystemRetryPolicy.IsTransient(new FileNotFoundException("missing")),
            "a missing local file is permanent");
        var calls = 0;
        var retryCallbacks = 0;
        await ThrowsAsync<UnauthorizedAccessException>(() => FileSystemRetryPolicy.ExecuteAsync<int>(_ =>
        {
            calls++;
            return Task.FromException<int>(new UnauthorizedAccessException("simulated permanent denial"));
        }, 3, 1, (_, _) => retryCallbacks++));
        Equal(1, calls, "permanent failure is attempted once");
        Equal(0, retryCallbacks, "permanent failure has no retry delay");
    }
}
