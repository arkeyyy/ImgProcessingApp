namespace ImageProcessingApp;

/// <summary>Owns the images for one still-image workspace. Preview controls borrow them.</summary>
internal sealed class ImageDocument : IDisposable
{
    private bool _disposed;

    public Bitmap? Source { get; private set; }
    public Bitmap? Result { get; private set; }
    public string? FileName { get; private set; }
    public Bitmap? HistogramSource => Result ?? Source;

    public void SetSource(Bitmap owned, string? fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(owned);
        if (ReferenceEquals(owned, Result))
            throw new ArgumentException("An image cannot be both the source and result.", nameof(owned));

        Result?.Dispose();
        Result = null;
        if (!ReferenceEquals(Source, owned)) Source?.Dispose();
        Source = owned;
        FileName = fileName;
    }

    public void SetResult(Bitmap owned)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(owned);
        if (ReferenceEquals(owned, Source))
            throw new ArgumentException("Results must be a separate bitmap from the original.", nameof(owned));

        if (!ReferenceEquals(Result, owned)) Result?.Dispose();
        Result = owned;
    }

    public void Clear()
    {
        Result?.Dispose();
        Source?.Dispose();
        Result = null;
        Source = null;
        FileName = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
    }
}
