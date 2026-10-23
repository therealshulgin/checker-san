namespace FolderChecker.Core;

/// <summary>
/// Limits concurrent reads per physical device: several files at once on an internal SSD, one at a time on a hard
/// disk, a share or an unidentified device, while different devices are read at the same time. Folders on the
/// same disk share one limit, so two folders on one hard disk are still read one file at a time.
/// </summary>
internal sealed class DeviceReaders
{
    /// <summary>Measured on an NVMe drive: 4 readers saturate SHA-256 on 4 cores without slowing small files.</summary>
    public const int SolidStateReads = 4;

    private readonly (string Root, SemaphoreSlim Gate)[] _roots;

    /// <summary>Total readers allowed at once; 1 means reading in parallel cannot help.</summary>
    public int Slots { get; }

    public DeviceReaders(IEnumerable<string> roots, int? parallelReads)
    {
        var gates = new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
        var assigned = new List<(string, SemaphoreSlim)>();
        foreach (var root in roots)
        {
            StorageDevice device;
            try { device = FileSafety.Storage(root); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
            { device = new StorageDevice("path:" + root, StorageKind.Unknown); }
            var limit = Math.Clamp(parallelReads ?? (device.Kind == StorageKind.SolidState ? SolidStateReads : 1), 1, 16);
            if (!gates.TryGetValue(device.Key, out var gate)) gates[device.Key] = gate = new SemaphoreSlim(limit, limit);
            assigned.Add((root, gate));
        }
        _roots = assigned.ToArray();
        Slots = gates.Values.Sum(gate => gate.CurrentCount);
    }

    /// <summary>Runs a read on a worker thread once its device has a free reader.</summary>
    public async Task<T> Run<T>(string path, Func<Task<T>> read, CancellationToken token)
    {
        var gate = _roots.FirstOrDefault(root => FileSafety.IsWithin(root.Root, path)).Gate ?? _roots[0].Gate;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(read, token).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
}
