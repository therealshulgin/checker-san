namespace FolderChecker.Core;

/// <summary>The two containment answers given on screen, in reports and on the command line: yes, no, or not verified (null).</summary>
public static class ComparisonVerdicts
{
    /// <summary>Folder 2 holds every item of folder 1 at the same path, with the same contents.</summary>
    public static bool? Folder2HoldsFolder1(ComparisonResult result) =>
        Containment(result, result.SourceFullyContained, ComparisonStatus.OnlySource, ComparisonStatus.Moved);

    /// <summary>Folder 1 holds every item of folder 2 at the same path, with the same contents.</summary>
    public static bool? Folder1HoldsFolder2(ComparisonResult result) =>
        Containment(result, result.TargetFullyContained, ComparisonStatus.OnlyTarget, ComparisonStatus.OnlyTarget);

    /// <summary>
    /// Incidents leave a "yes" unproven, but once the comparison ran to its end, a path on one side only or a proven
    /// difference is a "no" all the same.
    /// </summary>
    private static bool? Containment(ComparisonResult result, bool contained, ComparisonStatus missing, ComparisonStatus elsewhere)
    {
        if (!result.Cancelled && result.SnapshotVerified && result.Errors.Count == 0 && result.Rows.All(row => row.Status != ComparisonStatus.Unverified))
            return contained;
        return result.Finished && result.Rows.Any(row => row.Status == missing || row.Status == elsewhere ||
            row.Status is ComparisonStatus.Different or ComparisonStatus.Conflict) ? false : null;
    }
}
