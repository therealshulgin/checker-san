using System.ComponentModel;

namespace FolderChecker.Core;

/// <summary>Bounded retries for explicit Windows network failures; never retries permissions or sharing violations.</summary>
public static class FileSystemRetryPolicy
{
    public static bool IsTransient(Exception exception)
    {
        var code = exception is Win32Exception native ? native.NativeErrorCode
            : exception is IOException ? exception.HResult & 0xffff : -1;
        return code is 51 or 53 or 54 or 55 or 58 or 59 or 64 or 67 or 71 or 121 or 1201 or 1222 or 1231 or 1232 or 1236 or 2250;
    }

    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
        int maxRetries, int initialDelayMs, Action<int, TimeSpan>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetries);
        ArgumentOutOfRangeException.ThrowIfNegative(initialDelayMs);
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await operation(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (attempt < maxRetries && IsTransient(exception))
            {
                var delay = TimeSpan.FromMilliseconds(Math.Min(30_000L, (long)initialDelayMs * (1L << Math.Min(attempt, 20))));
                onRetry?.Invoke(attempt + 1, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
