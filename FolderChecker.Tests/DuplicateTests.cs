using System.Runtime.InteropServices;
using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    private static DuplicateScanOptions DuplicateOptions(string? checkpoint = null, bool resume = true) => new()
    {
        CheckpointPath = checkpoint, ResumeFromCheckpoint = resume, MaxRetries = 0, InitialRetryDelayMs = 1
    };

    private static async Task DuplicateUniqueSizesSkipContents()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "first.bin", TestWorkspace.Bytes(1024 * 1024));
        w.FileAt(w.Source, "second.bin", TestWorkspace.Bytes(1024 * 1024 + 1));
        w.FileAt(w.Source, "nested/small.txt", "small");
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified, "unique-size inventory is stable");
        Equal(3L, result.FilesScanned, "all files were inventoried");
        Equal(0L, result.BytesRead, "unique sizes require no content reads");
        Equal(0, result.Groups.Count, "unique sizes cannot be duplicates");
    }

    private static async Task DuplicateDifferentSamplesSkipFullHash()
    {
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(1024 * 1024);
        w.FileAt(w.Source, "a.bin", bytes);
        bytes[0] ^= 0xFF;
        w.FileAt(w.Source, "b.bin", bytes);
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified, "sample scan is stable");
        Equal(0, result.Groups.Count, "different samples cannot be duplicates");
        Equal(0L, result.FilesHashed, "different large samples avoid complete hashing");
        Equal(2L * 3 * 64 * 1024, result.BytesRead, "only three small ranges are read per file");
    }

    private static async Task DuplicateEqualSamplesRequireFullHash()
    {
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(1024 * 1024);
        w.FileAt(w.Source, "a.bin", bytes);
        bytes[256 * 1024] ^= 0xFF; // Outside beginning, central and final 64 KiB ranges.
        w.FileAt(w.Source, "b.bin", bytes);
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified, "full candidate scan is stable");
        Equal(0, result.Groups.Count, "equal samples never establish duplication");
        Equal(2L, result.FilesHashed, "both candidates receive complete SHA-256");
        Equal(2L * (bytes.Length + 3 * 64 * 1024), result.BytesRead, "complete bytes read after samples");
    }

    private static async Task DuplicateAllFormatsUseContents()
    {
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(271);
        w.FileAt(w.Source, "a.txt", bytes);
        w.FileAt(w.Source, "nested/b.mp4", bytes);
        var hidden = w.FileAt(w.Source, "no-extension", bytes);
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        w.FileAt(w.Source, "empty.one", Array.Empty<byte>());
        w.FileAt(w.Source, "nested/empty.two", Array.Empty<byte>());
        w.FileAt(w.Source, "different.txt", "different");
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified && result.Errors.Count == 0,
            "all file types are verified: " + string.Join("; ", result.Errors));
        Equal(6L, result.FilesScanned, "hidden and extensionless entries are inventoried");
        Equal(2, result.Groups.Count, "nonempty and empty byte-identical groups");
        var group = result.Groups.Single(group => group.Size == bytes.Length);
        Equal(3, group.Files.Count, "contents, not extensions, define the group");
        Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), group.Hash, "independent complete digest");
        Check(group.Files.All(file => file.FullPath.StartsWith(w.Source, StringComparison.OrdinalIgnoreCase)),
            "group retains exact paths for later selection");
    }

    private static async Task DuplicateCompletedCacheAvoidsReread()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Native Windows change-time fingerprint");
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(1024 * 1024 + 17);
        w.FileAt(w.Source, "one.bin", bytes);
        w.FileAt(w.Source, "two.bin", bytes);
        var checkpoint = Path.Combine(w.Root, "duplicates.json");
        var first = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint, false));
        Check(first.CheckpointSaved && first.SnapshotVerified, "completed scan persists evidence");
        Equal(w.Source, DuplicateEngine.ReadCheckpointRoot(checkpoint), "resume root can be restored by UI");
        var second = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint));
        Check(second.SnapshotVerified, "cached file metadata and identities revalidated");
        Equal(1, second.Groups.Count, "same group after restart");
        Equal(2L, second.FilesReused, "both complete file digests reused");
        Equal(0L, second.BytesRead, "unchanged cached contents need no reread");
    }

    private static async Task DuplicateSampleCacheAvoidsReread()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Native Windows change-time fingerprint");
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(1024 * 1024);
        w.FileAt(w.Source, "one.bin", bytes);
        bytes[0] ^= 0xFF;
        w.FileAt(w.Source, "two.bin", bytes);
        var checkpoint = Path.Combine(w.Root, "duplicates.json");
        await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint, false));
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint));
        Check(result.SnapshotVerified, "sample-only cache remains safe");
        Equal(0, result.Groups.Count, "unequal cached samples stay unequal");
        Equal(2L, result.FilesReused, "sample evidence reuses native change-time protection");
        Equal(0L, result.BytesRead, "sample cache also avoids rereading unchanged files");
    }

    private static async Task DuplicateCacheDetectsRestoredTimestamps()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Native Windows change-time fingerprint");
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(1024 * 1024 + 11);
        var firstPath = w.FileAt(w.Source, "one.bin", bytes);
        w.FileAt(w.Source, "two.bin", bytes);
        var checkpoint = Path.Combine(w.Root, "duplicates.json");
        await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint, false));
        var modified = File.GetLastWriteTimeUtc(firstPath);
        var created = File.GetCreationTimeUtc(firstPath);
        bytes[256 * 1024] ^= 0xFF;
        File.WriteAllBytes(firstPath, bytes);
        File.SetCreationTimeUtc(firstPath, created);
        File.SetLastWriteTimeUtc(firstPath, modified);
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint));
        Check(result.SnapshotVerified, "modified snapshot itself is stable");
        Equal(0, result.Groups.Count, "restored timestamps cannot hide changed contents");
        Equal(1L, result.FilesReused, "only genuinely unchanged file reused");
        Check(result.BytesRead >= bytes.Length, "changed file receives complete reread");
    }

    private static async Task DuplicateCancellationPreservesResume()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Native Windows change-time fingerprint");
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(2 * 1024 * 1024 + 17);
        for (var index = 0; index < 3; index++) w.FileAt(w.Source, $"file-{index}.bin", bytes);
        var checkpoint = Path.Combine(w.Root, "duplicates.json");
        using var cancellation = new CancellationTokenSource();
        var partial = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint, false),
            new DuplicateInlineProgress(value =>
            {
                if (value.FilesHashed >= 1) cancellation.Cancel();
            }), cancellation.Token);
        Check(partial.Cancelled && !partial.SnapshotVerified, "cancellation prevents complete verdict");
        Check(partial.CheckpointSaved && File.Exists(checkpoint), "completed samples and hashes survive interruption");
        var resumed = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint));
        Check(resumed.SnapshotVerified, "restarted engine resumes current inventory");
        Equal(1, resumed.Groups.Count, "one fully verified duplicate group after resumption");
        Equal(3, resumed.Groups[0].Files.Count, "all three files included");
        Check(resumed.FilesReused > 0, "resume reuses completed work");
        Check(resumed.BytesRead < 3L * bytes.Length, "completed full hash is not reread");
    }

    private static async Task DuplicateFinalChangeBlocksDeletionVerdict()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Native Windows change-time fingerprint");
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(512);
        var path = w.FileAt(w.Source, "one.bin", bytes);
        w.FileAt(w.Source, "two.bin", bytes);
        var changed = false;
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(),
            new DuplicateInlineProgress(value =>
            {
                if (changed || value.Phase != "Contrôle final des modifications") return;
                changed = true;
                var modified = File.GetLastWriteTimeUtc(path);
                var created = File.GetCreationTimeUtc(path);
                bytes[0] ^= 0xFF;
                File.WriteAllBytes(path, bytes);
                File.SetCreationTimeUtc(path, created);
                File.SetLastWriteTimeUtc(path, modified);
            }));
        Check(changed, "mutation happened after hash completion");
        Check(!result.SnapshotVerified && result.Errors.Count > 0, "post-hash mutation blocks removal eligibility");
    }

    private static async Task DuplicateLockedFileIsNotTrusted()
    {
        using var w = new TestWorkspace();
        var path = w.FileAt(w.Source, "one.bin", "same contents");
        w.FileAt(w.Source, "two.bin", "same contents");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(!result.SnapshotVerified && result.Errors.Count > 0, "inaccessible duplicate candidate is reported");
        Equal(0, result.Groups.Count, "a locked candidate cannot become a verified duplicate");
    }

    private static async Task DuplicateCheckpointOutsideTree()
    {
        using var w = new TestWorkspace();
        await ThrowsAsync<ArgumentException>(() => new DuplicateEngine().ScanAsync(w.Source,
            DuplicateOptions(Path.Combine(w.Source, "session.json"))));
        var alias = Path.Combine(w.Root, "source-alias");
        if (OperatingSystem.IsWindows())
        {
            await CreateJunction(alias, w.Source);
            await ThrowsAsync<ArgumentException>(() => new DuplicateEngine().ScanAsync(w.Source,
                DuplicateOptions(Path.Combine(alias, "session.json"))));
        }
        Check(!File.Exists(Path.Combine(w.Source, "session.json")), "scan never writes in its selected root");
    }

    private static async Task DuplicateMalformedCheckpointFallsBack()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "one.bin", "duplicates");
        w.FileAt(w.Source, "two.bin", "duplicates");
        var checkpoint = Path.Combine(w.Root, "duplicates.json");
        File.WriteAllText(checkpoint, "{ invalid json");
        Check(DuplicateEngine.ReadCheckpointRoot(checkpoint) is null, "malformed session has no reusable UI root");
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(checkpoint));
        Check(result.SnapshotVerified && result.Errors.Count == 0, "malformed cache does not compromise scan");
        Equal(1, result.Groups.Count, "fresh bytes establish duplicates");
        Equal(0L, result.FilesReused, "corrupted evidence is not reused");
        Check(result.Warnings.Count > 0, "cache failure is visible");
    }

    private static async Task DuplicateJunctionsAreNotFollowed()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows junctions");
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "one.bin", "duplicate");
        w.FileAt(w.Target, "two.bin", "duplicate");
        await CreateJunction(Path.Combine(w.Source, "external"), w.Target);
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Equal(1L, result.FilesScanned, "junction target was not traversed");
        Equal(0, result.Groups.Count, "external duplicate is not included");
        Check(!result.SnapshotVerified && result.Errors.Count > 0, "skipped links are explicit");
    }

    private static async Task DuplicateHardLinksDoNotInflateSavings()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows hard links");
        using var w = new TestWorkspace();
        var bytes = TestWorkspace.Bytes(781);
        var original = w.FileAt(w.Source, "a.bin", bytes);
        w.FileAt(w.Source, "c.bin", bytes);
        w.FileAt(w.Source, "d.bin", bytes);
        if (!DuplicateCreateHardLink(Path.Combine(w.Source, "b.bin"), original, IntPtr.Zero))
            throw new SkipTestException($"Hard-link creation failed: {Marshal.GetLastWin32Error()}");
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified, "hard links can be safely excluded from savings");
        Equal(1, result.Groups.Count, "two independent physical files share contents");
        Equal(2, result.Groups[0].Files.Count, "only independent files without additional hard links are eligible");
        Check(result.Groups[0].Files.All(file => file.RelativePath is "c.bin" or "d.bin"),
            "every path of a hard-linked file is excluded from recoverable space");
        Check(result.Warnings.Any(warning => warning.Contains("Lien physique", StringComparison.Ordinal)),
            "excluded physical alias is explained");
    }

    private static async Task DuplicatePreCancelledIsIncomplete()
    {
        using var w = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions(), cancellationToken: cancellation.Token);
        Check(result.Cancelled && !result.SnapshotVerified, "pre-cancelled operation never declares a complete scan");
        Equal(0L, result.BytesRead, "pre-cancelled operation reads no file bytes");
    }

    private static async Task DuplicateManagedQuarantineIsExcluded()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "ordinary.bin", "same contents");
        var quarantine = w.DirectoryAt(w.Source, ".CheckerSan-Quarantine");
        w.FileAt(quarantine, "checker-san-quarantine.json",
            """{"Schema":1,"Application":"Checker San","Kind":"DuplicateQuarantine"}""");
        w.FileAt(quarantine, "session/removed.bin", "same contents");
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified, "managed quarantine does not invalidate inventory");
        Equal(1L, result.FilesScanned, "quarantined files are not presented again");
        Equal(0, result.Groups.Count, "quarantined copies are absent from duplicate results");
        Check(result.Warnings.Any(warning => warning.Contains("quarantaine", StringComparison.OrdinalIgnoreCase)),
            "excluded quarantine is disclosed");
    }

    private static async Task DuplicateUnmanagedQuarantineNameIsScanned()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "ordinary.bin", "same contents");
        w.FileAt(w.Source, ".CheckerSan-Quarantine/other.bin", "same contents");
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified, "a folder name alone never hides user data");
        Equal(2L, result.FilesScanned, "unmarked quarantine-like folder is ordinary user data");
        Equal(1, result.Groups.Count, "duplicates inside unmarked folder are detected");
    }

    private sealed class DuplicateInlineProgress(Action<DuplicateProgress> callback) : IProgress<DuplicateProgress>
    {
        public void Report(DuplicateProgress value) => callback(value);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DuplicateCreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
