namespace CertificateGenerator.Api.Models;

public sealed class PreviewRequest
{
    public TemplateConfig Config { get; set; } = new();

    public Dictionary<string, string> SampleData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
