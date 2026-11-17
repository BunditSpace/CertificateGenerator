using System.Text.Json.Serialization;

namespace CertificateGenerator.Api.Models;

public sealed class TemplateConfig
{
    /// <summary>
    /// Page orientation: "Landscape" or "Portrait".
    /// </summary>
    public string Orientation { get; set; } = "Landscape";

    public string? BackgroundUrl { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>
    /// Predefined logo placement: "TopLeft", "TopCenter", "TopRight", "BottomLeft", "BottomRight".
    /// </summary>
    public string LogoPosition { get; set; } = "TopCenter";

    public float LogoWidth { get; set; } = 120f;

    public string? WatermarkUrl { get; set; }

    /// <summary>
    /// Opacity between 0.0 and 1.0.
    /// </summary>
    public float WatermarkOpacity { get; set; } = 0.15f;

    public string FontFamily { get; set; } = "Sarabun";

    public List<TemplateFieldConfig> Fields { get; set; } = [];
}

public sealed class TemplateFieldConfig
{
    /// <summary>
    /// Column name / placeholder key (e.g., "FullName", "CourseName", "Date").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Horizontal position in points from the left edge.
    /// </summary>
    public float X { get; set; }

    /// <summary>
    /// Vertical position in points from the top edge.
    /// </summary>
    public float Y { get; set; }

    public float FontSize { get; set; } = 24f;

    public string Color { get; set; } = "#111827";

    /// <summary>
    /// "Normal" or "Bold".
    /// </summary>
    public string FontWeight { get; set; } = "Normal";

    /// <summary>
    /// "Left", "Center", or "Right".
    /// </summary>
    public string Align { get; set; } = "Center";

    /// <summary>
    /// Optional width in points for text wrapping/alignment box.
    /// </summary>
    public float? Width { get; set; }

    /// <summary>
    /// Optional fallback/sample text used when generating a single-page preview.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SampleValue { get; set; }
}
