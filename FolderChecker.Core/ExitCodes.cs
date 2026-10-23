namespace FolderChecker.Core;

/// <summary>
/// Stable exit codes of the command line and of scheduled checks: 0 nothing to report, 1 proven differences or
/// duplicates, 2 incomplete verification (errors, unreadable or online-only files, real items facing links,
/// changes during the scan, cancellation), 3 invalid command or fatal error.
/// </summary>
public static class ExitCodes
{
    public const int Clean = 0, Differences = 1, Incomplete = 2, Failure = 3;

    /// <summary>Paths that prove the folders differ: missing on one side, different, moved or in conflict.</summary>
    public static int DifferenceCount(ComparisonResult result) => result.Rows.Count(row =>
        row.Status is ComparisonStatus.Different or ComparisonStatus.OnlySource or ComparisonStatus.OnlyTarget or ComparisonStatus.Moved or ComparisonStatus.Conflict);

    /// <summary>Links, junctions and paths under them: listed, never followed, never compared.</summary>
    public static int LinkCount(ComparisonResult result) => result.Rows.Count(row => row.Status == ComparisonStatus.Skipped);

    /// <summary>
    /// Clean only when the trees are proven identical. A real folder facing a junction on the other side has
    /// no difference row, yet its contents were never compared: that is incomplete, not identical.
    /// </summary>
    public static int ForComparison(ComparisonResult result)
    {
        var certain = !result.Cancelled && result.SnapshotVerified && result.Errors.Count == 0;
        return !certain ? Incomplete
            : DifferenceCount(result) > 0 ? Differences
            : result.Rows.Any(row => row.Status == ComparisonStatus.Unverified) || !result.TreesIdentical ? Incomplete
            : Clean;
    }
}
