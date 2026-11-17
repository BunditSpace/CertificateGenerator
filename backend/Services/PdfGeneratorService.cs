using CertificateGenerator.Api.Models;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace CertificateGenerator.Api.Services;

public sealed record CachedTemplateAssets(
    byte[]? BackgroundBytes,
    byte[]? WatermarkBytes,
    byte[]? LogoBytes);

public sealed class PdfGeneratorService
{
    private readonly SupabaseStorageService _storageService;
    private readonly ILogger<PdfGeneratorService> _logger;

    public PdfGeneratorService(
        SupabaseStorageService storageService,
        ILogger<PdfGeneratorService> logger)
    {
        _storageService = storageService;
        _logger = logger;

        QuestPDF.Settings.License = LicenseType.Community;
        RegisterProjectFonts();
    }

    private void RegisterProjectFonts()
    {
        var fontsDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
        if (!Directory.Exists(fontsDir))
        {
            _logger.LogWarning("Fonts directory not found at {FontsDir}. Falling back to system/default fonts.", fontsDir);
            return;
        }

        var fontFiles = Directory.GetFiles(fontsDir, "*.ttf", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(fontsDir, "*.otf", SearchOption.AllDirectories))
            .ToArray();

        if (fontFiles.Length == 0)
        {
            _logger.LogWarning("No .ttf/.otf font files found in {FontsDir}. Place Thai fonts (e.g. Sarabun-Regular.ttf) there.", fontsDir);
            return;
        }

        foreach (var fontPath in fontFiles)
        {
            using var stream = File.OpenRead(fontPath);
            FontManager.RegisterFont(stream);
            _logger.LogInformation("Registered font: {FontFile}", Path.GetFileName(fontPath));
        }
    }

    /// <summary>
    /// Downloads and pre-processes template images once per job to avoid repeated network/CPU work inside the row loop.
    /// </summary>
    public async Task<CachedTemplateAssets> PrepareAssetsAsync(
        TemplateConfig config,
        CancellationToken cancellationToken = default)
    {
        var bgTask = _storageService.DownloadBytesAsync(config.BackgroundUrl, cancellationToken);
        var logoTask = _storageService.DownloadBytesAsync(config.LogoUrl, cancellationToken);
        var watermarkTask = _storageService.DownloadBytesAsync(config.WatermarkUrl, cancellationToken);

        await Task.WhenAll(bgTask, logoTask, watermarkTask);

        var watermarkBytes = await watermarkTask;
        if (watermarkBytes is { Length: > 0 })
        {
            watermarkBytes = ApplyOpacity(watermarkBytes, config.WatermarkOpacity);
        }

        return new CachedTemplateAssets(
            await bgTask,
            watermarkBytes,
            await logoTask);
    }

    public byte[] GenerateSinglePdf(
        CachedTemplateAssets assets,
        TemplateConfig config,
        IReadOnlyDictionary<string, string> rowData)
    {
        var isLandscape = !string.Equals(config.Orientation, "Portrait", StringComparison.OrdinalIgnoreCase);
        var pageSize = isLandscape ? PageSizes.A4.Landscape() : PageSizes.A4;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(0);
                page.PageColor(Colors.White);

                if (!string.IsNullOrWhiteSpace(config.FontFamily))
                {
                    page.DefaultTextStyle(style => style.FontFamily(config.FontFamily));
                }

                page.Content().Layers(layers =>
                {
                    // Layer 1: Background image (fills page)
                    if (assets.BackgroundBytes is { Length: > 0 })
                    {
                        layers.Layer()
                            .Image(assets.BackgroundBytes)
                            .FitUnproportionally();
                    }

                    // Layer 2: Watermark image (centered, pre-processed with ImageSharp opacity)
                    if (assets.WatermarkBytes is { Length: > 0 })
                    {
                        layers.Layer()
                            .AlignCenter()
                            .AlignMiddle()
                            .Width(pageSize.Width * 0.45f)
                            .Image(assets.WatermarkBytes)
                            .FitWidth();
                    }

                    // Primary Layer: Logo & Text Fields
                    layers.PrimaryLayer().Layers(contentLayers =>
                    {
                        // Base full-page transparent canvas so PrimaryLayer establishes full page bounds
                        contentLayers.PrimaryLayer().Extend();

                        if (assets.LogoBytes is { Length: > 0 })
                        {
                            PlaceLogo(contentLayers.Layer(), assets.LogoBytes, config.LogoPosition, config.LogoWidth);
                        }

                        foreach (var field in config.Fields)
                        {
                            var textValue = ResolveFieldValue(field, rowData);
                            if (string.IsNullOrEmpty(textValue))
                            {
                                continue;
                            }

                            PlaceTextField(contentLayers.Layer(), field, textValue, config.FontFamily);
                        }
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static byte[] ApplyOpacity(byte[] imageBytes, float opacity)
    {
        var clamped = Math.Clamp(opacity, 0f, 1f);
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);
        image.Mutate(ctx => ctx.Opacity(clamped));

        using var output = new MemoryStream();
        image.SaveAsPng(output);
        return output.ToArray();
    }

    private static void PlaceLogo(IContainer container, byte[] logoBytes, string position, float logoWidth)
    {
        var width = Math.Clamp(logoWidth, 24f, 400f);
        const float edgePadding = 28f;

        var positioned = position.Trim().ToLowerInvariant() switch
        {
            "topleft" => container.Padding(edgePadding).AlignTop().AlignLeft(),
            "topright" => container.Padding(edgePadding).AlignTop().AlignRight(),
            "bottomleft" => container.Padding(edgePadding).AlignBottom().AlignLeft(),
            "bottomright" => container.Padding(edgePadding).AlignBottom().AlignRight(),
            _ => container.Padding(edgePadding).AlignTop().AlignCenter()
        };

        positioned.Width(width).Image(logoBytes).FitWidth();
    }

    private static void PlaceTextField(
        IContainer container,
        TemplateFieldConfig field,
        string value,
        string defaultFontFamily)
    {
        var x = Math.Max(0f, field.X);
        var y = Math.Max(0f, field.Y);
        var fontSize = Math.Clamp(field.FontSize, 6f, 144f);
        var color = string.IsNullOrWhiteSpace(field.Color) ? "#111827" : field.Color;

        var box = container.PaddingLeft(x).PaddingTop(y);
        if (field.Width is > 0)
        {
            box = box.Width(field.Width.Value);
        }

        box.Text(text =>
        {
            switch (field.Align.Trim().ToLowerInvariant())
            {
                case "center":
                    text.AlignCenter();
                    break;
                case "right":
                    text.AlignRight();
                    break;
                default:
                    text.AlignLeft();
                    break;
            }

            var span = text.Span(value)
                .FontSize(fontSize)
                .FontColor(color);

            if (!string.IsNullOrWhiteSpace(defaultFontFamily))
            {
                span.FontFamily(defaultFontFamily);
            }

            if (string.Equals(field.FontWeight, "Bold", StringComparison.OrdinalIgnoreCase))
            {
                span.Bold();
            }
        });
    }

    private static string ResolveFieldValue(
        TemplateFieldConfig field,
        IReadOnlyDictionary<string, string> rowData)
    {
        if (rowData.TryGetValue(field.Name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return field.SampleValue ?? string.Empty;
    }
}
