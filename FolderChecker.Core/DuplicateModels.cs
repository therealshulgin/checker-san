namespace FolderChecker.Core;

public sealed record DuplicateScanOptions
{
    public string? CheckpointPath { get; init; }
    public bool ResumeFromCheckpoint { get; init; } = true;
    public int MaxRetries { get; init; } = 3;
    public int InitialRetryDelayMs { get; init; } = 1000;
}

public sealed record DuplicateProgress
{
    public string Phase { get; init; } = "";
    public string CurrentPath { get; init; } = "";
    public long FilesScanned { get; init; }
    public long FilesHashed { get; init; }
    public long BytesRead { get; init; }
    public long FilesReused { get; init; }
    public int RetryAttempt { get; init; }
}

public sealed record DuplicateFile
{
    public string FullPath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public long Size { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public DateTime CreationUtc { get; init; }
    public FileAttributes Attributes { get; init; }
    /// <summary>Native volume, file identifier and change time; null when unavailable.</summary>
    public string? Identity { get; init; }
}

public sealed record DuplicateGroup
{
    public string Hash { get; init; } = "";
    public long Size { get; init; }
    public IReadOnlyList<DuplicateFile> Files { get; init; } = Array.Empty<DuplicateFile>();
}

public sealed record DuplicateResult
{
    public string Root { get; init; } = "";
    public string CanonicalRoot { get; init; } = "";
    public IReadOnlyList<DuplicateGroup> Groups { get; init; } = Array.Empty<DuplicateGroup>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool Cancelled { get; init; }
    public bool SnapshotVerified { get; init; }
    public long FilesScanned { get; init; }
    public long FilesHashed { get; init; }
    public long BytesRead { get; init; }
    public long FilesReused { get; init; }
    public bool CheckpointSaved { get; init; }
}
