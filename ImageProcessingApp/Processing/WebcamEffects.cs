namespace ImageProcessingApp;

internal sealed record WebcamEffect(string Name, string Description, bool UsesConvolution = false)
{
    public string? Category => Name switch
    {
        "None" => null,
        "Grayscale" or "Invert" or "Sepia" => "Color & tone",
        "Subtract" => "Background",
        "Gaussian Blur" or "Sharpen" => "Smooth & sharpen",
        "Motion Blur Horizontal" or "Motion Blur Vertical" or "Motion Blur Diagonal" => "Motion blur",
        "Sobel Edges" or "Prewitt Edges" or "Scharr Edges" => "Edge detection",
        "Emboss NW–SE" or "Emboss NE–SW" => "Emboss",
        _ => throw new InvalidOperationException("Unknown webcam effect category.")
    };
}

/// <summary>Live effects share the still-image algorithms and return independently owned frames.</summary>
internal static class WebcamEffects
{
    public static IReadOnlyList<WebcamEffect> All { get; } = Array.AsReadOnly<WebcamEffect>(
    [
        new("None", "Show each original camera frame without an effect."),
        new("Grayscale", "Average red, green, and blue for a grayscale image."),
        new("Invert", "Replace each color with its opposite for a negative image."),
        new("Sepia", "Blend colors into warm brown tones."),
        new("Subtract", "Replace pixels near average RGB brightness 85 with black."),
        new("Gaussian Blur", "Soften details with a weighted blur.", true),
        new("Sharpen", "Increase contrast around details and edges.", true),
        new("Motion Blur Horizontal", "Blur three pixels from left to right.", true),
        new("Motion Blur Vertical", "Blur three pixels from top to bottom.", true),
        new("Motion Blur Diagonal", "Blur three pixels from top-left to bottom-right.", true),
        new("Sobel Edges", "Reveal grayscale edges using horizontal and vertical intensity changes.", true),
        new("Prewitt Edges", "Reveal grayscale edges with uniform neighboring weights.", true),
        new("Scharr Edges", "Reveal grayscale edges with stronger central weighting.", true),
        new("Emboss NW–SE", "Create diagonal relief from top-left to bottom-right around neutral gray.", true),
        new("Emboss NE–SW", "Create diagonal relief from top-right to bottom-left around neutral gray.", true)
    ]);

    private static readonly Dictionary<string, WebcamEffect> ByName = All.ToDictionary(effect => effect.Name, StringComparer.Ordinal);

    public static Bitmap Apply(Bitmap source, string effect, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(effect);
        if (!ByName.TryGetValue(effect, out var preset))
            throw new ArgumentException("Unknown webcam effect.", nameof(effect));

        return preset.UsesConvolution
            ? ImageOperations.ApplyConvolution(source, effect, cancellationToken)
            : ImageOperations.ApplyBasic(source, effect == "None" ? "Copy Original" : effect, cancellationToken);
    }
}
