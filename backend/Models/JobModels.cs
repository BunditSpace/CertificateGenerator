using System.Text.Json.Serialization;

namespace CertificateGenerator.Api.Models;

public static class JobStatuses
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";

    public static bool IsTerminal(string status) =>
        status is Completed or Failed;
}

public sealed class DocumentJobRecord
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = JobStatuses.Queued;

    [JsonPropertyName("progress")]
    public int Progress { get; set; }

    [JsonPropertyName("total_records")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("zip_download_url")]
    public string? ZipDownloadUrl { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record JobStatusUpdate(
    Guid JobId,
    string Status,
    int Progress,
    int TotalRecords,
    string? ZipDownloadUrl,
    string? ErrorMessage,
    DateTimeOffset UpdatedAt);

public sealed record GenerateJobQueuedResponse(
    Guid JobId,
    string Status,
    string StreamUrl,
    string StatusUrl);

public sealed record GenerateJobWorkItem(
    Guid JobId,
    string ExcelFilePath,
    TemplateConfig Config);
