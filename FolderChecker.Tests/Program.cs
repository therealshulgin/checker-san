using System.Diagnostics;
using System.Security.Cryptography;
using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static readonly (string Name, Func<Task> Run)[] Tests =
    [
        ("Identical nested media files are verified by full SHA-256", IdenticalNestedTrees),
        ("Every file type, including hidden and extensionless files, is compared by bytes", AllFileTypesCompared),
        ("Two empty roots are identical", EmptyRoots),
        ("A source-only file blocks source containment", ExtraSourceFile),
        ("A target-only file preserves source containment", ExtraTargetFile),
        ("A source subset is detected consistently in either folder order", BidirectionalSourceSubset),
        ("Extra source duplicates cannot conceal target containment", BidirectionalDuplicateSubset),
        ("Unique entries on each side prevent containment in both directions", BidirectionalSeparateExtras),
        ("Moved contents never imply strict containment in either direction", BidirectionalMovedOnly),
        ("Empty roots and empty directories retain directional inclusion", BidirectionalEmptyDirectories),
        ("A same-path content mismatch blocks both directions despite copies elsewhere", BidirectionalContentMismatch),
        ("Cancellation after matching files blocks both directional verdicts", BidirectionalCancelledFinalCheck),
        ("Same name, size and timestamp do not imply equal contents", SameMetadataDifferentContents),
        ("Different lengths are reported as different", DifferentLengths),
        ("Renamed files retain their own source rows", MovedFile),
        ("Same-size candidates need matching content to count as moved", DifferentContentNotMoved),
        ("A copy elsewhere never hides different content at the same path", DifferentSamePathWithCopyElsewhere),
        ("Moved-file search can be disabled", MovedFileSearchDisabled),
        ("Missing empty source directories block strict containment", MissingEmptySourceDirectory),
        ("Extra empty target directories block tree identity", ExtraEmptyTargetDirectory),
        ("File versus directory collisions are conflicts", FileDirectoryConflict),
        ("Empty files are content-verified", ZeroLengthFiles),
        ("Duplicate content does not erase source path multiplicity", DuplicateMultiplicity),
        ("Windows paths compare case-insensitively", CaseInsensitivePaths),
        ("Unicode and punctuation in names survive comparison", UnicodePaths),
        ("The same root is rejected", SameRootRejected),
        ("Nested roots are rejected in both directions", NestedRootsRejected),
        ("Missing roots are rejected", MissingRootRejected),
        ("Pre-cancelled scans never return a positive verdict", PreCancelled),
        ("Cancellation during hashing never returns a positive verdict", CancelDuringHash),
        ("A locked source file prevents a positive verdict", LockedSourceFile),
        ("A locked target file prevents a positive verdict", LockedTargetFile),
        ("Inventory changes during a scan invalidate the snapshot", MutationDuringScan),
        ("A target appearing during hashing invalidates its original missing row", TargetAppearingDuringScan),
        ("Directory junctions are reported and skipped without blocking verdicts", JunctionSkipped),
        ("Junction aliases cannot disguise identical or nested roots", JunctionAliasesRejected),
        ("Cancelled work persists completed hashes for a verified resumed scan", CheckpointCancellationAndResume),
        ("Resume invalidates only a changed same-size file", CheckpointInvalidatesChangedFile),
        ("Restoring timestamps cannot disguise changed content from resume", CheckpointDetectsRestoredTimestamps),
        ("A checkpoint from different roots is never reused", CheckpointRejectsDifferentRoots),
        ("Malformed checkpoint data falls back to a complete fresh comparison", MalformedCheckpointIsSafe),
        ("Checkpoint output cannot be written inside either selected tree", CheckpointCannotWriteSelectedTrees),
        ("A junction alias cannot redirect checkpoint output into a selected tree", CheckpointCannotWriteViaJunction),
        ("Cached hashes cannot conceal a newly inaccessible file", CachedHashDoesNotHideLockedFile),
        ("Simulated transient network failures retry within the configured bound", SimulatedNetworkRetries),
        ("Exhausted simulated network retries preserve the original failure", SimulatedRetryExhaustion),
        ("Cancellation interrupts a retry delay before another attempt", CancelDuringRetryDelay),
        ("Permanent filesystem failures are never retried", PermanentFailureNotRetried),
        ("Missing paths copy in both directions while existing conflicts remain untouched", CopyMissingBothDirections),
        ("Interrupted copies resume from a checked partial file after restart", CopyResumePartial),
        ("A destination appearing after preview is never overwritten", CopyNeverOverwrites),
        ("A changed source cannot be copied using a stale plan", CopyRejectsChangedSource),
        ("A corrupted copied prefix is never committed as complete", CopyChecksExistingPrefix),
        ("Copy journals must remain outside the compared trees", CopyJournalOutsideTrees),
        ("Copy plans cannot traverse outside their roots", CopyRejectsTraversal),
        ("A completed file is retained when the remaining copy resumes", CopyKeepsCompletedFiles),
        ("Copy journals append item states and recover them after a crash", CopyJournalRecoversAppendedStates),
        ("Copy journal writes stay proportional to the plan size", CopyJournalWritesStayProportional),
        ("Copy progress within a file is limited to ten updates per second", CopyProgressIsThrottledWithinFiles),
        ("Copies keep the source's dates and attributes, folders included", CopyPreservesDatesAndAttributes),
        ("A copy that cannot fit stops before writing anything", CopyRefusesWithoutFreeSpace),
        ("Online-only cloud files are compared by metadata and never read", OnlineOnlyFilesAreNeverRead),
        ("Online-only cloud files are left out of duplicate groups", DuplicateOnlineOnlyFilesAreSkipped),
        ("One synchronization fills missing files and empty directories on both sides", SyncCompletesBothTrees),
        ("Synchronization restores absent paths even when their content exists elsewhere", SyncRestoresMovedPaths),
        ("One-way copies restore moved paths in either chosen direction", SyncOneWayMovedPaths),
        ("Synchronization leaves content and file-directory conflicts untouched", SyncPreservesConflicts),
        ("Reverse-direction interrupted files resume into the correct root after restart", SyncResumesReverseFile),
        ("Legacy schema-one copy journals still resume in either original direction", SyncResumesLegacyJournal),
        ("Ambiguous journal direction flags are rejected before any copying", SyncRejectsAmbiguousJournal),
        ("Swapping compared roots preserves synchronization behavior and originals", SyncSwappedRoots),
        ("A corrupted reverse-direction partial file cannot be committed", SyncRejectsCorruptedReversePartial),
        ("A reverse destination appearing after interruption is never overwritten", SyncRejectsAppearingReverseDestination),
        ("PrefilterRejectsDifferentSamples", PrefilterRejectsDifferentSamples),
        ("PrefilterEqualSamplesRequireFullHash", PrefilterEqualSamplesRequireFullHash),
        ("PrefilterCanBeDisabled", PrefilterCanBeDisabled),
        ("PrefilterKeepsMovedMatches", PrefilterKeepsMovedMatches),
        ("PrefilterSkipsUnrelatedMovedCandidates", PrefilterSkipsUnrelatedMovedCandidates),
        ("PrefilterPreservesCheckpointReuse", PrefilterPreservesCheckpointReuse),
        ("PrefilterCancellationCannotConfirm", PrefilterCancellationCannotConfirm),
        ("DuplicateUniqueSizesSkipContents", DuplicateUniqueSizesSkipContents),
        ("DuplicateDifferentSamplesSkipFullHash", DuplicateDifferentSamplesSkipFullHash),
        ("DuplicateEqualSamplesRequireFullHash", DuplicateEqualSamplesRequireFullHash),
        ("DuplicateAllFormatsUseContents", DuplicateAllFormatsUseContents),
        ("DuplicateCompletedCacheAvoidsReread", DuplicateCompletedCacheAvoidsReread),
        ("DuplicateSampleCacheAvoidsReread", DuplicateSampleCacheAvoidsReread),
        ("DuplicateCacheDetectsRestoredTimestamps", DuplicateCacheDetectsRestoredTimestamps),
        ("DuplicateCancellationPreservesResume", DuplicateCancellationPreservesResume),
        ("DuplicateFinalChangeBlocksDeletionVerdict", DuplicateFinalChangeBlocksDeletionVerdict),
        ("DuplicateLockedFileIsNotTrusted", DuplicateLockedFileIsNotTrusted),
        ("DuplicateCheckpointOutsideTree", DuplicateCheckpointOutsideTree),
        ("DuplicateMalformedCheckpointFallsBack", DuplicateMalformedCheckpointFallsBack),
        ("DuplicateJunctionsAreNotFollowed", DuplicateJunctionsAreNotFollowed),
        ("DuplicateHardLinksDoNotInflateSavings", DuplicateHardLinksDoNotInflateSavings),
        ("DuplicatePreCancelledIsIncomplete", DuplicatePreCancelledIsIncomplete),
        ("DuplicateManagedQuarantineIsExcluded", DuplicateManagedQuarantineIsExcluded),
        ("DuplicateUnmanagedQuarantineNameIsScanned", DuplicateUnmanagedQuarantineNameIsScanned),
        ("DuplicateRemovalRejectsEntireGroup", DuplicateRemovalRejectsEntireGroup),
        ("DuplicateRemovalPermanentKeepsOne", DuplicateRemovalPermanentKeepsOne),
        ("DuplicateRemovalQuarantineIsReversible", DuplicateRemovalQuarantineIsReversible),
        ("DuplicateRemovalRejectsChangedKeeper", DuplicateRemovalRejectsChangedKeeper),
        ("DuplicateRemovalRejectsChangedTarget", DuplicateRemovalRejectsChangedTarget),
        ("DuplicateRemovalRehashesContent", DuplicateRemovalRehashesContent),
        ("DuplicateRemovalRejectsTraversal", DuplicateRemovalRejectsTraversal),
        ("DuplicateRemovalRejectsNewJunction", DuplicateRemovalRejectsNewJunction),
        ("DuplicateRemovalCancellationBeforeMutation", DuplicateRemovalCancellationBeforeMutation),
        ("DuplicateRemovalCancellationKeepsRemaining", DuplicateRemovalCancellationKeepsRemaining),
        ("DuplicateRemovalLocksKeeper", DuplicateRemovalLocksKeeper),
        ("DuplicateRemovalRefusesUnmanagedQuarantine", DuplicateRemovalRefusesUnmanagedQuarantine),
        ("DuplicateRemovalRejectsUnverifiedAndUnknown", DuplicateRemovalRejectsUnverifiedAndUnknown),
        ("DuplicateRemovalRequiresNativeIdentity", DuplicateRemovalRequiresNativeIdentity),
        ("DuplicateRemovalRejectsHardlinkReplacement", DuplicateRemovalRejectsHardlinkReplacement),
        ("DuplicateRemovalMissingKeeperPreservesLastCopy", DuplicateRemovalMissingKeeperPreservesLastCopy),
        ("DuplicateRemovalLocksSelectedIdentity", DuplicateRemovalLocksSelectedIdentity),
        ("DuplicateRemovalValidatesWholeGroupBeforeMutation", DuplicateRemovalValidatesWholeGroupBeforeMutation),
        ("DuplicateRemovalDoesNotTrustEmptyQuarantineMarker", DuplicateRemovalDoesNotTrustEmptyQuarantineMarker),
        ("DuplicateRemovalQuarantineCancellationKeepsProgress", DuplicateRemovalQuarantineCancellationKeepsProgress),
        ("LocalizationComparisonEnglishPreservesPaths", LocalizationComparisonEnglishPreservesPaths),
        ("LocalizationComparisonErrorsEnglish", LocalizationComparisonErrorsEnglish),
        ("LocalizationDuplicatesEnglish", LocalizationDuplicatesEnglish),
        ("LocalizationCopyEnglishPreservesPaths", LocalizationCopyEnglishPreservesPaths),
        ("LocalizationRemovalErrorsEnglish", LocalizationRemovalErrorsEnglish),
        ("LocalizationFrenchCanBeRestored", LocalizationFrenchCanBeRestored),
    ];

    public static async Task<int> Main(string[] args)
    {
        var failures = new List<string>();
        var skipped = 0;
        var timer = Stopwatch.StartNew();
        var selected = args.Length == 0 ? Tests : Tests.Where(test => args.Any(filter => test.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (selected.Length == 0) { Console.WriteLine("No tests match the provided filters."); return 1; }
        foreach (var (name, run) in selected)
        {
            try
            {
                await run();
                Console.WriteLine($"PASS {name}");
            }
            catch (SkipTestException ex)
            {
                skipped++;
                Console.WriteLine($"SKIP {name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                failures.Add(name);
                Console.WriteLine($"FAIL {name}\n{ex}");
            }
        }

        Console.WriteLine($"\n{selected.Length - failures.Count - skipped} passed; {failures.Count} failed; "
                          + $"{skipped} skipped; {timer.Elapsed.TotalSeconds:F2}s.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static async Task IdenticalNestedTrees()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(3 * 1024 * 1024 + 37);
        var otherData = TestWorkspace.Bytes(4097, 42);
        w.FileAt(w.Source, "audio/live/song.wav", data);
        w.FileAt(w.Target, "audio/live/song.wav", data);
        w.FileAt(w.Source, "video/clip.mp4", otherData);
        w.FileAt(w.Target, "video/clip.mp4", otherData);
        w.DirectoryAt(w.Source, "empty");
        w.DirectoryAt(w.Target, "empty");
        var result = await Compare(w);
        AssertVerifiedIdentical(result);
        Equal(2L, result.SourceFileCount, "source file count");
        Equal(2L, result.TargetFileCount, "target file count");
        Equal(4L, result.FilesVerified, "all four files hashed");
        Equal(2L * (data.Length + otherData.Length), result.BytesRead, "all bytes read");
        var row = SourceRow(result, w.Source, "audio/live/song.wav");
        Equal(ComparisonStatus.Identical, row.Status, "identical status");
        var expectedHash = Convert.ToHexString(SHA256.HashData(data));
        Check(string.Equals(expectedHash, row.SourceHash, StringComparison.OrdinalIgnoreCase),
            "source SHA-256 must match an independently calculated digest");
        Check(string.Equals(expectedHash, row.TargetHash, StringComparison.OrdinalIgnoreCase),
            "target SHA-256 must match an independently calculated digest");
        Check(result.Rows.Any(r => r.Kind == EntryKind.Directory && r.Status == ComparisonStatus.CommonDirectory
                                  && Normalize(r.RelativePath) == "empty"), "empty directory must be listed");
    }

    private static async Task EmptyRoots()
    {
        using var w = new TestWorkspace();
        AssertVerifiedIdentical(await Compare(w));
    }

    private static async Task AllFileTypesCompared()
    {
        using var w = new TestWorkspace();
        string[] names =
        [
            "notes.txt", "report.pdf", "document.docx", "photo.jpg", "archive.zip",
            "unknown.custom", "README", ".hidden-data"
        ];
        var expectedHashes = new Dictionary<string, string>();
        long expectedBytesRead = 0;
        for (var index = 0; index < names.Length; index++)
        {
            // Deliberately arbitrary bytes: extensions and MIME formats must not control comparison.
            var bytes = TestWorkspace.Bytes(257 + index * 101, 100 + index);
            var relative = Path.Combine("mixed", names[index]);
            var sourcePath = w.FileAt(w.Source, relative, bytes);
            var targetPath = w.FileAt(w.Target, relative, bytes);
            if (names[index] == ".hidden-data" && OperatingSystem.IsWindows())
            {
                File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.Hidden);
                File.SetAttributes(targetPath, File.GetAttributes(targetPath) | FileAttributes.Hidden);
                Check((File.GetAttributes(sourcePath) & FileAttributes.Hidden) != 0,
                    "source fixture must have the Windows hidden attribute");
            }
            expectedHashes.Add(relative, Convert.ToHexString(SHA256.HashData(bytes)));
            expectedBytesRead += 2L * bytes.Length;
        }

        var original = TestWorkspace.Bytes(4099, 999);
        var changed = (byte[])original.Clone();
        changed[^1] ^= 0xFF;
        w.FileAt(w.Source, "mixed/different.bin", original);
        w.FileAt(w.Target, "mixed/different.bin", changed);
        expectedBytesRead += original.Length + changed.Length;

        var result = await Compare(w);
        Equal(9L, result.SourceFileCount, "all source file types are inventoried");
        Equal(9L, result.TargetFileCount, "all target file types are inventoried");
        Equal(18L, result.FilesVerified, "every file is hashed, including hidden and extensionless files");
        Equal(expectedBytesRead, result.BytesRead, "every byte is read regardless of extension");
        foreach (var (relative, expectedHash) in expectedHashes)
        {
            var row = SourceRow(result, w.Source, relative);
            Equal(ComparisonStatus.Identical, row.Status, $"identical contents for {relative}");
            Check(string.Equals(expectedHash, row.SourceHash, StringComparison.OrdinalIgnoreCase),
                $"independent source SHA-256 for {relative}");
            Check(string.Equals(expectedHash, row.TargetHash, StringComparison.OrdinalIgnoreCase),
                $"independent target SHA-256 for {relative}");
        }
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "mixed/different.bin").Status,
            "same-size differing generic binary file is detected");
        NoStrictVerdicts(result);
        Check(!result.ContentCovered, "different generic source content is not covered");
        Check(result.SnapshotVerified, "stable mixed-file inventory is verified");
        Equal(0, result.Errors.Count, "no file type is rejected");
    }

    private static async Task ExtraSourceFile()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        w.FileAt(w.Source, "missing.wav", "source only");
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(!result.ContentCovered, "missing source content must not be covered");
        Equal(ComparisonStatus.OnlySource, SourceRow(result, w.Source, "missing.wav").Status, "source-only status");
    }

    private static async Task ExtraTargetFile()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        w.FileAt(w.Target, "extra.mp4", "target only");
        var result = await Compare(w);
        Check(result.SourceFullyContained, "extra target files must not block source containment");
        Check(result.ContentCovered, "source content is present");
        Check(!result.TreesIdentical, "extra target files block tree identity");
        Check(result.Rows.Any(r => r.Status == ComparisonStatus.OnlyTarget
                                  && Normalize(r.RelativePath) == "extra.mp4"), "extra target row missing");
    }

    private static async Task SameMetadataDifferentContents()
    {
        using var w = new TestWorkspace();
        var left = TestWorkspace.Bytes(1024 * 1024 + 41);
        var right = (byte[])left.Clone();
        right[right.Length / 2] ^= 0xFF;
        var source = w.FileAt(w.Source, "track.wav", left);
        var target = w.FileAt(w.Target, "track.wav", right);
        var timestamp = new DateTime(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, timestamp);
        File.SetLastWriteTimeUtc(target, timestamp);
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(!result.ContentCovered, "different source bytes are not covered");
        var row = SourceRow(result, w.Source, "track.wav");
        Equal(ComparisonStatus.Different, row.Status, "different status");
        Check(!string.IsNullOrEmpty(row.SourceHash) && !string.IsNullOrEmpty(row.TargetHash), "both full hashes required");
        Check(row.SourceHash != row.TargetHash, "different contents must produce different digests");
    }

    private static async Task DifferentLengths()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "track.wav", "short");
        w.FileAt(w.Target, "track.wav", "a longer recording");
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "track.wav").Status, "different length status");
    }

    private static async Task MovedFile()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(8193);
        w.FileAt(w.Source, "old/name.wav", data);
        var target = w.FileAt(w.Target, "new/renamed.wav", data);
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(result.ContentCovered, "renamed matching content is covered");
        var row = SourceRow(result, w.Source, "old/name.wav");
        Equal(ComparisonStatus.Moved, row.Status, "renamed status");
        Equal(target, row.TargetPath, "renamed target location");
        Check(!string.IsNullOrEmpty(row.SourceHash) && row.SourceHash == row.TargetHash, "moved match requires equal full hashes");
    }

    private static async Task MovedFileSearchDisabled()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "old.wav", "same audio");
        w.FileAt(w.Target, "new.wav", "same audio");
        var result = await Compare(w, new ComparisonOptions(false));
        NoStrictVerdicts(result);
        Check(!result.ContentCovered, "disabled search cannot confirm content coverage");
        Equal(ComparisonStatus.OnlySource, SourceRow(result, w.Source, "old.wav").Status, "source-only without move search");
    }

    private static async Task DifferentContentNotMoved()
    {
        using var w = new TestWorkspace();
        var sourceBytes = TestWorkspace.Bytes(2057, 1);
        var targetBytes = TestWorkspace.Bytes(2057, 2);
        w.FileAt(w.Source, "old.wav", sourceBytes);
        w.FileAt(w.Target, "renamed.wav", targetBytes);
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(!result.ContentCovered, "same size with different content is not coverage");
        Equal(ComparisonStatus.OnlySource, SourceRow(result, w.Source, "old.wav").Status,
            "different candidate must not be reported as moved");
    }

    private static async Task DifferentSamePathWithCopyElsewhere()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "original.wav", "source recording");
        w.FileAt(w.Target, "original.wav", "changed recording");
        w.FileAt(w.Target, "archive.wav", "source recording");
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(result.ContentCovered, "alternate exact copy provides content coverage");
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "original.wav").Status,
            "a mismatched original path must remain different even if a copy exists elsewhere");
    }

    private static async Task MissingEmptySourceDirectory()
    {
        using var w = new TestWorkspace();
        w.DirectoryAt(w.Source, "empty/deeper");
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(result.ContentCovered, "there are no uncovered source files");
        Check(result.Rows.Any(r => r.Status == ComparisonStatus.OnlySource && r.Kind == EntryKind.Directory
                                  && Normalize(r.RelativePath) == "empty/deeper"), "missing empty directory row required");
    }

    private static async Task ExtraEmptyTargetDirectory()
    {
        using var w = new TestWorkspace();
        w.DirectoryAt(w.Target, "empty/deeper");
        var result = await Compare(w);
        Check(result.SourceFullyContained, "empty source is contained");
        Check(!result.TreesIdentical, "extra empty target directories matter");
    }

    private static async Task FileDirectoryConflict()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "collision", "audio");
        w.DirectoryAt(w.Target, "collision");
        w.FileAt(w.Target, "collision/inside.wav", "other content");
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(result.Rows.Any(r => r.Status == ComparisonStatus.Conflict
                                  && Normalize(r.RelativePath) == "collision"), "file/directory conflict required");
    }

    private static async Task ZeroLengthFiles()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "empty.wav", []);
        w.FileAt(w.Target, "empty.wav", []);
        var result = await Compare(w);
        AssertVerifiedIdentical(result);
        var row = SourceRow(result, w.Source, "empty.wav");
        Check(string.Equals(Convert.ToHexString(SHA256.HashData([])), row.SourceHash,
            StringComparison.OrdinalIgnoreCase), "zero-byte file needs a valid SHA-256 digest");
    }

    private static async Task DuplicateMultiplicity()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(521);
        w.FileAt(w.Source, "copy1.wav", data);
        w.FileAt(w.Source, "copy2.wav", data);
        w.FileAt(w.Target, "copy1.wav", data);
        var result = await Compare(w);
        NoStrictVerdicts(result);
        Check(result.ContentCovered, "content coverage explicitly ignores duplicate multiplicity");
        Equal(ComparisonStatus.Identical, SourceRow(result, w.Source, "copy1.wav").Status, "first duplicate");
        Equal(ComparisonStatus.Moved, SourceRow(result, w.Source, "copy2.wav").Status, "missing duplicate path retained");
        Equal(2L, result.SourceFileCount, "duplicate source file count");
        Equal(1L, result.TargetFileCount, "duplicate target file count");
    }

    private static async Task CaseInsensitivePaths()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows semantics test");
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "Audio/Track.WAV", "sound");
        w.FileAt(w.Target, "audio/track.wav", "sound");
        AssertVerifiedIdentical(await Compare(w));
    }

    private static async Task UnicodePaths()
    {
        using var w = new TestWorkspace();
        const string relative = "Été à Berlin 🎵/prise #2 [finale] 音声.wav";
        w.FileAt(w.Source, relative, "audio");
        w.FileAt(w.Target, relative, "audio");
        AssertVerifiedIdentical(await Compare(w));
    }

    private static async Task SameRootRejected()
    {
        using var w = new TestWorkspace();
        await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(w.Source, w.Source,
            new ComparisonOptions()));
        if (OperatingSystem.IsWindows())
            await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(w.Source,
                w.Source.ToUpperInvariant() + Path.DirectorySeparatorChar, new ComparisonOptions()));
    }

    private static async Task NestedRootsRejected()
    {
        using var w = new TestWorkspace();
        var nested = w.DirectoryAt(w.Source, "nested");
        await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(w.Source, nested,
            new ComparisonOptions()));
        await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(nested, w.Source,
            new ComparisonOptions()));
    }

    private static async Task MissingRootRejected()
    {
        using var w = new TestWorkspace();
        await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(w.Source,
            Path.Combine(w.Root, "not-created"), new ComparisonOptions()));
    }

    private static async Task PreCancelled()
    {
        using var w = new TestWorkspace();
        AddCommonFile(w);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(),
            cancellationToken: cancellation.Token);
        AssertCancelled(result);
    }

    private static async Task CancelDuringHash()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(4 * 1024 * 1024 + 13);
        w.FileAt(w.Source, "video.mp4", data);
        w.FileAt(w.Target, "video.mp4", data);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            if (p.BytesHashed > 0) cancellation.Cancel();
        });
        var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(),
            progress, cancellation.Token);
        Check(cancellation.IsCancellationRequested, "hash progress was reported");
        AssertCancelled(result);
    }

    private static async Task LockedSourceFile()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows sharing-mode test");
        using var w = new TestWorkspace();
        AddCommonFile(w);
        using var held = new FileStream(Path.Combine(w.Source, "common.wav"), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);
        AssertUncertain(await Compare(w));
    }

    private static async Task LockedTargetFile()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows sharing-mode test");
        using var w = new TestWorkspace();
        AddCommonFile(w);
        using var held = new FileStream(Path.Combine(w.Target, "common.wav"), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None);
        AssertUncertain(await Compare(w));
    }

    private static async Task MutationDuringScan()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(2 * 1024 * 1024 + 13);
        w.FileAt(w.Source, "video.mp4", data);
        w.FileAt(w.Target, "video.mp4", data);
        var changed = false;
        var progress = new InlineProgress(p =>
        {
            if (changed || p.BytesHashed == 0) return;
            changed = true;
            w.FileAt(w.Source, "arrived-during-scan.wav", "new recording");
        });
        var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(), progress);
        Check(changed, "mutation was triggered during hashing");
        AssertUncertain(result);
        Check(!result.SnapshotVerified, "a changing inventory cannot be a verified snapshot");
    }

    private static async Task JunctionSkipped()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows junction test");
        using var w = new TestWorkspace();
        AddCommonFile(w);
        var unrelated = w.DirectoryAt(w.Root, "junction-destination");
        w.FileAt(unrelated, "must-not-follow.wav", "outside selected roots");
        var junction = Path.Combine(w.Source, "linked");
        await CreateJunction(junction, unrelated);
        Check((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0, "fixture is a junction");
        var result = await Compare(w);
        Check(result.SnapshotVerified && result.Errors.Count == 0, "a junction is reported, not an error: " + string.Join("; ", result.Errors));
        Equal(ComparisonStatus.Skipped, result.Rows.Single(r => r.RelativePath == "linked").Status, "junction row is skipped");
        Check(result.SourceFullyContained && result.TargetFullyContained, "a link is not content, so it does not block either verdict");
        Equal(1L, result.SourceFileCount, "links are not counted as files");
        Check(result.Rows.All(r => !Normalize(r.RelativePath).Contains("must-not-follow", StringComparison.Ordinal)),
            "scanner must not traverse junction destination");

        // A real folder facing that link is not contained: its files are not compared through the link.
        w.FileAt(w.Target, "linked/real.wav", "only reachable through the link in folder 1");
        var facing = await Compare(w);
        Check(facing.SnapshotVerified && facing.Errors.Count == 0, "facing a link is not an error");
        Equal(ComparisonStatus.Skipped, facing.Rows.Single(r => Normalize(r.RelativePath) == "linked/real.wav").Status, "path under a link is skipped");
        Check(facing.SourceFullyContained && !facing.TargetFullyContained, "the real folder in folder 2 is not contained in folder 1");
        Equal(0, CopyEngine.CreatePlan(facing, false).Items.Count, "nothing is ever copied through a link");
    }

    private static async Task TargetAppearingDuringScan()
    {
        using var w = new TestWorkspace();
        var data = TestWorkspace.Bytes(2 * 1024 * 1024 + 13);
        w.FileAt(w.Source, "video.mp4", data);
        w.FileAt(w.Target, "video.mp4", data);
        w.FileAt(w.Source, "missing.bin", "arrives during scan");
        string? appearedPath = null;
        var progress = new InlineProgress(p =>
        {
            if (appearedPath is not null || p.BytesHashed == 0) return;
            appearedPath = w.FileAt(w.Target, "missing.bin", "arrives during scan");
        });
        var result = await new ComparisonEngine().CompareAsync(w.Source, w.Target, new ComparisonOptions(), progress);
        Check(appearedPath is not null, "target file appeared during hashing");
        AssertUncertain(result);
        Check(!result.SnapshotVerified, "target mutation invalidates the snapshot");
        var row = SourceRow(result, w.Source, "missing.bin");
        Equal(ComparisonStatus.Unverified, row.Status, "formerly missing target must be marked unverified");
        Equal(appearedPath, row.TargetPath, "appearing target path is retained for inspection");
    }

    private static async Task JunctionAliasesRejected()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows junction test");
        using var w = new TestWorkspace();
        var realParent = w.DirectoryAt(w.Root, "real-parent");
        var realChild = w.DirectoryAt(realParent, "child");
        w.FileAt(realChild, "recording.wav", "one physical copy");
        var aliasParent = Path.Combine(w.Root, "alias-parent");
        await CreateJunction(aliasParent, realParent);
        var aliasChild = Path.Combine(aliasParent, "child");
        await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(realChild, aliasChild,
            new ComparisonOptions()));
        await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(realParent, aliasChild,
            new ComparisonOptions()));
        await ThrowsAsync<ArgumentException>(() => new ComparisonEngine().CompareAsync(w.Source, aliasParent,
            new ComparisonOptions()));
    }

    private static async Task CreateJunction(string junction, string destination)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/d /c mklink /J \"{junction}\" \"{destination}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo) ?? throw new Exception("Cannot launch junction creation");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0) throw new SkipTestException($"Cannot create junction: {stdout} {stderr}");
    }

    private static Task<ComparisonResult> Compare(TestWorkspace w, ComparisonOptions? options = null) =>
        new ComparisonEngine().CompareAsync(w.Source, w.Target, options ?? new ComparisonOptions());

    private static void AddCommonFile(TestWorkspace w)
    {
        w.FileAt(w.Source, "common.wav", "one common audio recording");
        w.FileAt(w.Target, "common.wav", "one common audio recording");
    }

    private static ComparisonRow SourceRow(ComparisonResult result, string sourceRoot, string relative)
    {
        var expected = Path.GetFullPath(Path.Combine(sourceRoot, relative));
        var rows = result.Rows.Where(r => r.Kind == EntryKind.File
                                         && string.Equals(r.SourcePath, expected, StringComparison.OrdinalIgnoreCase)).ToArray();
        Equal(1, rows.Length, $"one source row for {relative}");
        return rows[0];
    }

    private static void AssertVerifiedIdentical(ComparisonResult result)
    {
        Check(result.SourceFullyContained, "source fully contained: " + string.Join("; ", result.Errors));
        Check(result.TargetFullyContained, "target fully contained");
        Check(result.TreesIdentical, "trees identical");
        Check(result.ContentCovered, "all source file content covered");
        Check(result.SnapshotVerified, "final inventory verified");
        Check(!result.Cancelled, "not cancelled");
        Equal(0, result.Errors.Count, "no errors");
    }

    private static void NoStrictVerdicts(ComparisonResult result)
    {
        Check(!result.SourceFullyContained, "source must not be declared strictly contained");
        Check(!result.TreesIdentical, "trees must not be declared identical");
    }

    private static void AssertCancelled(ComparisonResult result)
    {
        NoStrictVerdicts(result);
        Check(!result.TargetFullyContained, "cancelled comparison must not confirm target containment");
        Check(result.Cancelled, "cancelled flag required");
        Check(!result.ContentCovered, "cancelled comparison must not confirm content coverage");
        Check(!result.SnapshotVerified, "cancelled comparison cannot verify snapshot");
    }

    private static void AssertUncertain(ComparisonResult result)
    {
        NoStrictVerdicts(result);
        Check(!result.TargetFullyContained, "uncertainty must block target containment");
        Check(!result.ContentCovered, "uncertainty must block positive content verdicts");
        Check(result.Errors.Count > 0, "uncertainty must have an explanatory error");
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Assertion failed: {message}");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Assertion failed: {message}. Expected {expected}, got {actual}.");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
    }

    private sealed class InlineProgress(Action<ComparisonProgress> callback) : IProgress<ComparisonProgress>
    {
        public void Report(ComparisonProgress value) => callback(value);
    }

    private sealed class SkipTestException(string message) : Exception(message);
}
