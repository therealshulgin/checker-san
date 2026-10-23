using FolderChecker.Core;

namespace FolderChecker.Tests;

internal static partial class Program
{
    // OneDrive placeholders cannot be created without a sync provider. The offline bit marks the same
    // "data not stored locally" state, and the engines must treat both alike: never read the file.
    private static string MarkOnlineOnly(string path)
    {
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline);
        return path;
    }

    private static async Task OnlineOnlyFilesAreNeverRead()
    {
        using var w = new TestWorkspace();
        var video = TestWorkspace.Bytes(5 * 1024 * 1024 + 9, 12);
        MarkOnlineOnly(w.FileAt(w.Source, "cloud/video.mp4", video));
        w.FileAt(w.Target, "cloud/video.mp4", video);
        MarkOnlineOnly(w.FileAt(w.Source, "cloud/voice.wav", "online version"));
        w.FileAt(w.Target, "cloud/voice.wav", "a longer local version");
        MarkOnlineOnly(w.FileAt(w.Source, "cloud/missing.txt", "only in folder 1"));
        var result = await Compare(w);
        Check(result.SnapshotVerified && result.Errors.Count == 0, "online-only files are not errors: " + string.Join("; ", result.Errors));
        Equal(0L, result.BytesRead + result.SampleBytesRead, "online-only files are never read, so never downloaded");
        var same = SourceRow(result, w.Source, "cloud/video.mp4");
        Check(same.Status == ComparisonStatus.Unverified && same.OnlineOnly, "an equal size is not proof of equal contents");
        Equal(ComparisonStatus.Different, SourceRow(result, w.Source, "cloud/voice.wav").Status, "different sizes need no download");
        Equal(ComparisonStatus.OnlySource, SourceRow(result, w.Source, "cloud/missing.txt").Status, "presence is known without reading");
        Check(!result.SourceFullyContained, "an unread file cannot confirm containment");
        Check(CopyEngine.CanCreatePlan(result), "unread files present on both sides do not prevent copying");
        Equal(0, CopyEngine.CreatePlan(result, true).Items.Count, "an online-only file is never copied, which would download it");
    }

    private static async Task DuplicateOnlineOnlyFilesAreSkipped()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "a.bin", "same contents");
        w.FileAt(w.Source, "b.bin", "same contents");
        MarkOnlineOnly(w.FileAt(w.Source, "c.bin", "same contents"));
        var result = await new DuplicateEngine().ScanAsync(w.Source, DuplicateOptions());
        Check(result.SnapshotVerified && result.Errors.Count == 0, "online-only files do not block removal: " + string.Join("; ", result.Errors));
        Equal(1, result.Groups.Count, "local copies are still grouped");
        Equal(2, result.Groups[0].Files.Count, "the online-only copy is left out");
        Check(result.Warnings.Any(warning => warning.Contains("en ligne", StringComparison.Ordinal) || warning.Contains("online-only", StringComparison.Ordinal)),
            "skipped online-only files are reported");
    }

    private static async Task CopyPreservesDatesAndAttributes()
    {
        using var w = new TestWorkspace();
        var file = w.FileAt(w.Source, "docs/report.pdf", TestWorkspace.Bytes(70_000, 5));
        var folder = Path.Combine(w.Source, "docs");
        var copied = Path.Combine(w.Target, "docs", "report.pdf");
        DateTime fileCreated = new(2019, 3, 4, 5, 6, 7, DateTimeKind.Utc), fileWritten = new(2020, 8, 9, 10, 11, 12, DateTimeKind.Utc);
        DateTime folderCreated = new(2017, 1, 2, 3, 4, 5, DateTimeKind.Utc), folderWritten = new(2018, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        File.SetCreationTimeUtc(file, fileCreated);
        File.SetLastWriteTimeUtc(file, fileWritten);
        File.SetAttributes(file, FileAttributes.ReadOnly | FileAttributes.Hidden);
        Directory.SetCreationTimeUtc(folder, folderCreated);
        Directory.SetLastWriteTimeUtc(folder, folderWritten);
        try
        {
            var plan = CopyEngine.CreatePlan(await Compare(w), true);
            var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
            Check(copy.Complete, string.Join(";", copy.Errors));
            Equal(fileWritten, File.GetLastWriteTimeUtc(copied), "modification date kept");
            Equal(fileCreated, File.GetCreationTimeUtc(copied), "creation date kept");
            Equal(FileAttributes.ReadOnly | FileAttributes.Hidden,
                File.GetAttributes(copied) & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System), "attributes kept");
            Equal(folderWritten, Directory.GetLastWriteTimeUtc(Path.Combine(w.Target, "docs")), "folder date kept although files were added later");
            Equal(folderCreated, Directory.GetCreationTimeUtc(Path.Combine(w.Target, "docs")), "folder creation date kept");
            AssertVerifiedIdentical(await Compare(w));
        }
        finally
        {
            // Read-only files would block the workspace cleanup.
            foreach (var path in new[] { file, copied })
                if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    private static async Task CopyRefusesWithoutFreeSpace()
    {
        using var w = new TestWorkspace();
        w.FileAt(w.Source, "huge.bin", "small on disk, huge in the plan");
        var plan = CopyEngine.CreatePlan(await Compare(w), true);
        plan.Items.Single(i => i.Kind == EntryKind.File).Size = 1L << 50;
        var need = CopyEngine.SpaceNeeds(plan).Single();
        Check(!need.Sufficient && need.Required >= 1L << 50, "a petabyte cannot fit on the test volume");
        var copy = await new CopyEngine().CopyAsync(plan, Path.Combine(w.Root, "copy.json"));
        Check(!copy.Complete && copy.Errors.Any(error => error.Contains("Espace insuffisant", StringComparison.Ordinal) || error.Contains("Not enough space", StringComparison.Ordinal)),
            "missing space is reported: " + string.Join(";", copy.Errors));
        Check(!Directory.EnumerateFileSystemEntries(w.Target).Any(), "nothing is written when space is missing");

        w.FileAt(w.Target, "other.bin", "12345");
        var shared = CopyEngine.SpaceNeeds(CopyEngine.CreateBidirectionalPlan(await Compare(w)));
        Equal(1, shared.Count, "both directions on one volume are added together");
        Equal(2L * 4096, shared[0].Required, "each file needs at least one whole cluster");
    }
}
