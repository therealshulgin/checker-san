namespace FolderChecker.Core;

/// <summary>Which copy each duplicate group keeps when extra copies are selected automatically.</summary>
public enum KeepRule { FirstPath, Oldest, Newest, ShortestPath, InFolder }

/// <summary>Copies to remove, and the groups the rule could not decide, which are left entirely unselected.</summary>
public sealed record DuplicateSelectionPlan(IReadOnlyList<string> Remove, int UndecidedGroups);

public static class DuplicateSelection
{
    /// <summary>
    /// Keeps exactly one copy per group according to the rule. Ties are broken by path, so the result never
    /// depends on scan order. With <see cref="KeepRule.InFolder"/>, a group without a copy in that folder is undecided.
    /// </summary>
    public static DuplicateSelectionPlan Extras(DuplicateResult scan, KeepRule rule, string? folder = null)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (rule == KeepRule.InFolder && string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException(L.T("Choisissez le dossier dont les copies sont conservées.", "Choose the folder whose copies are kept."), nameof(folder));
        var remove = new List<string>();
        var undecided = 0;
        foreach (var group in scan.Groups)
        {
            var candidates = rule == KeepRule.InFolder
                ? group.Files.Where(file => FileSafety.IsWithin(folder!, file.FullPath)).ToArray()
                : group.Files.ToArray();
            if (candidates.Length == 0) { undecided++; continue; }
            var byPath = candidates.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
            var keeper = (rule switch
            {
                KeepRule.Oldest => byPath.OrderBy(file => file.LastWriteUtc).ThenBy(file => file.CreationUtc),
                KeepRule.Newest => byPath.OrderByDescending(file => file.LastWriteUtc).ThenByDescending(file => file.CreationUtc),
                KeepRule.ShortestPath => byPath.OrderBy(file => file.RelativePath.Count(c => c is '\\' or '/')).ThenBy(file => file.RelativePath.Length),
                _ => byPath
            }).First();
            remove.AddRange(group.Files.Where(file => !ReferenceEquals(file, keeper)).Select(file => file.FullPath));
        }
        return new DuplicateSelectionPlan(remove, undecided);
    }
}
