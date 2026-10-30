namespace ImageProcessingApp;

/// <summary>Display metadata for the existing fixed convolution operations.</summary>
internal static class ConvolutionFilters
{
    public static IReadOnlyList<FilterChoice> All { get; } = Array.AsReadOnly<FilterChoice>(
    [
        new("Smooth", "Smooth & sharpen", "Average neighboring pixels to soften fine detail."),
        new("Gaussian Blur", "Smooth & sharpen", "Use weighted neighboring pixels for a softer blur."),
        new("Sharpen", "Smooth & sharpen", "Increase contrast around details and edges."),
        new("Mean Removal", "Smooth & sharpen", "Emphasize fine details by subtracting nearby values."),
        new("Motion Blur Horizontal", "Motion blur", "Average three pixels left to right for horizontal motion blur."),
        new("Motion Blur Vertical", "Motion blur", "Average three pixels top to bottom for vertical motion blur."),
        new("Motion Blur Diagonal", "Motion blur", "Average three pixels from top-left to bottom-right for diagonal motion blur."),
        new("Laplacian", "Edge detection", "Highlight sudden changes in intensity to reveal edges."),
        new("Sobel Edges", "Edge detection", "Combine horizontal and vertical intensity changes into grayscale edges with weighted smoothing."),
        new("Prewitt Edges", "Edge detection", "Combine horizontal and vertical intensity changes into grayscale edges with uniform smoothing."),
        new("Scharr Edges", "Edge detection", "Combine horizontal and vertical intensity changes into grayscale edges with stronger central weighting."),
        new("Horizontal + Vertical", "Emboss", "Create an embossed effect from horizontal and vertical edges."),
        new("All Directions", "Emboss", "Reveal edge relief in all directions."),
        new("Lossy", "Emboss", "Apply the original lossy emboss kernel for a stylized relief."),
        new("Horizontal Only", "Emboss", "Emphasize horizontal edge relief."),
        new("Vertical Only", "Emboss", "Emphasize vertical edge relief."),
        new("Emboss NW–SE", "Emboss", "Create diagonal relief from top-left to bottom-right, centered on neutral gray."),
        new("Emboss NE–SW", "Emboss", "Create diagonal relief from top-right to bottom-left, centered on neutral gray.")
    ]);
}
