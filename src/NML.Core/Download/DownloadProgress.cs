namespace NML.Core.Download;

/// <summary>
/// Progress information for a single download or an aggregate download batch.
/// Designed to be both bindable to UI progress bars and cheap to aggregate.
/// </summary>
public readonly record struct DownloadProgress(
    long BytesDownloaded,
    long TotalBytes,
    int FilesCompleted,
    int TotalFiles)
{
    /// <summary>Fraction completed, 0..1. <c>NaN</c> when the total is unknown.</summary>
    public double Fraction => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes : double.NaN;

    /// <summary>Per-file fraction completed, 0..1.</summary>
    public double FileFraction => TotalFiles > 0 ? (double)FilesCompleted / TotalFiles : double.NaN;
}

/// <summary>Delegate used to report download progress from background workers.</summary>
public delegate void ProgressReporter(in DownloadProgress progress, string currentFileName);

/// <summary>
/// A cancellation hook passed into downloads so the UI layer (or a batch installer)
/// can stop an in-flight download cleanly. Thin wrapper over <see cref="CancellationToken"/>.
/// </summary>
public sealed class DownloadCancel
{
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken Token => _cts.Token;

    public void Cancel() => _cts.Cancel();

    public bool IsCancellationRequested => _cts.IsCancellationRequested;
}
