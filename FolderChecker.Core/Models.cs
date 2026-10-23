namespace FolderChecker.Core;

public enum EntryKind { File, Directory }

public enum ComparisonStatus
{
    Identical, Different, OnlySource, OnlyTarget, Moved, CommonDirectory, Conflict, Unverified,
    /// <summary>A symbolic link or junction, or a path under one: reported, never followed or compared.</summary>
    Skipped
}

public sealed record ComparisonOptions(bool FindMovedFiles = true)
{
    /// <summary>Reject different large files using small samples before computing full SHA-256. Equal samples never prove equality.</summary>
    public bool UseSamplePrefilter { get; init; } = true;
    public bool ResumeFromCheckpoint { get; init; }
    public string? CheckpointPath { get; init; }
    public int MaxRetries { get; init; } = 3;
    public int InitialRetryDelayMs { get; init; } = 1000;
    /// <summary>Files read at once per disk; null chooses 4 on an internal SSD and 1 elsewhere (hard disk, USB, share).</summary>
    public int? ParallelReads { get; init; }
}

public sealed record CheckpointInfo
{
    public string SourceRoot { get; init; } = "";
    public string TargetRoot { get; init; } = "";
    public bool FindMovedFiles { get; init; }
    public DateTimeOffset SavedAt { get; init; }
    public long CompletedFileCount { get; init; }
}

public sealed record ComparisonRow
{
    public EntryKind Kind { get; init; }
    public ComparisonStatus Status { get; init; }
    public string RelativePath { get; init; } = "";
    public string? SourcePath { get; init; }
    public string? TargetPath { get; init; }
    public long? SourceSize { get; init; }
    public long? TargetSize { get; init; }
    public string Detail { get; init; } = "";
    public string? SourceHash { get; init; }
    public string? TargetHash { get; init; }
    /// <summary>One side is an online-only cloud file. It was not read, because reading would download it.</summary>
    public bool OnlineOnly { get; init; }
}

public sealed record ComparisonProgress
{
    public string Phase { get; init; } = "";
    public string CurrentPath { get; init; } = "";
    public long FilesDiscovered { get; init; }
    public long FilesHashed { get; init; }
    public long BytesHashed { get; init; }
    /// <summary>Additional bytes read by the sample prefilter; not included in BytesHashed.</summary>
    public long SampleBytesRead { get; init; }
    public long TotalHashBytes { get; init; }
    public long FilesReused { get; init; }
    public int RetryAttempt { get; init; }
}

public sealed record ComparisonResult
{
    public string SourceRoot { get; init; } = "";
    public string TargetRoot { get; init; } = "";
    public IReadOnlyList<ComparisonRow> Rows { get; init; } = Array.Empty<ComparisonRow>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public bool Cancelled { get; init; }
    public bool SnapshotVerified { get; init; }
    /// <summary>All source files and directories exist at the same relative paths in the target; file contents were verified with full SHA-256.</summary>
    public bool SourceFullyContained { get; init; }
    /// <summary>All target files and directories exist at the same relative paths in the source; file contents were verified with full SHA-256.</summary>
    public bool TargetFullyContained { get; init; }
    public bool TreesIdentical { get; init; }
    /// <summary>Every source file has a verified byte-identical copy somewhere in the target. This ignores names, directories, and duplicate multiplicity.</summary>
    public bool ContentCovered { get; init; }
    public long SourceFileCount { get; init; }
    public long TargetFileCount { get; init; }
    /// <summary>Number of files successfully hashed across both trees.</summary>
    public long FilesVerified { get; init; }
    /// <summary>Bytes read for full-file SHA-256, including interrupted attempts.</summary>
    public long BytesRead { get; init; }
    /// <summary>Additional bytes read by the sample prefilter; not included in BytesRead.</summary>
    public long SampleBytesRead { get; init; }
    public long FilesReused { get; init; }
    public bool HasCheckpoint { get; init; }
    public bool CheckpointSaved { get; init; }
    public string? CheckpointPath { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
