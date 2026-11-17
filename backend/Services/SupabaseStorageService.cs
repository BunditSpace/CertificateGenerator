using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CertificateGenerator.Api.Models;

namespace CertificateGenerator.Api.Services;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;
    public string ServiceRoleKey { get; set; } = string.Empty;
    public string TemplatesBucket { get; set; } = "templates";
    public string RawUploadsBucket { get; set; } = "raw-uploads";
    public string OutputsBucket { get; set; } = "outputs";
}

/// <summary>
/// Integrates with Supabase PostgREST (`document_jobs`) and Supabase Storage buckets.
/// </summary>
public sealed class SupabaseStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabaseStorageService> _logger;

    public SupabaseStorageService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<SupabaseStorageService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = configuration.GetSection(SupabaseOptions.SectionName).Get<SupabaseOptions>() ?? new SupabaseOptions();
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Url) &&
        !string.IsNullOrWhiteSpace(_options.ServiceRoleKey);

    public string OutputsBucket => _options.OutputsBucket;
    public string RawUploadsBucket => _options.RawUploadsBucket;
    public string TemplatesBucket => _options.TemplatesBucket;

    public async Task CreateJobAsync(DocumentJobRecord job, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Supabase is not configured. Skipping DB insert for job {JobId}.", job.Id);
            return;
        }

        using var request = CreateSupabaseRequest(HttpMethod.Post, "/rest/v1/document_jobs");
        request.Headers.Add("Prefer", "return=minimal");
        request.Content = JsonContent.Create(job, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateJobAsync(
        Guid jobId,
        string status,
        int progress,
        int totalRecords,
        string? zipDownloadUrl = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        var payload = new
        {
            status,
            progress,
            total_records = totalRecords,
            zip_download_url = zipDownloadUrl,
            error_message = errorMessage
        };

        using var request = CreateSupabaseRequest(HttpMethod.Patch, $"/rest/v1/document_jobs?id=eq.{jobId}");
        request.Headers.Add("Prefer", "return=minimal");
        request.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to update job {JobId} in Supabase ({StatusCode}): {Body}", jobId, response.StatusCode, body);
        }
    }

    public async Task<DocumentJobRecord?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        using var request = CreateSupabaseRequest(HttpMethod.Get, $"/rest/v1/document_jobs?id=eq.{jobId}&select=*");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var records = await response.Content.ReadFromJsonAsync<List<DocumentJobRecord>>(JsonOptions, cancellationToken);
        return records?.FirstOrDefault();
    }

    public async Task<string> UploadFileAsync(
        string bucket,
        string objectPath,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = objectPath.TrimStart('/');

        if (!IsConfigured)
        {
            _logger.LogWarning("Supabase is not configured. Skipping upload to {Bucket}/{ObjectPath}.", bucket, normalizedPath);
            return string.Empty;
        }

        using var request = CreateSupabaseRequest(HttpMethod.Post, $"/storage/v1/object/{bucket}/{normalizedPath}");
        request.Headers.Add("x-upsert", "true");
        request.Content = new StreamContent(content);
        if (content.CanSeek)
        {
            request.Content.Headers.ContentLength = content.Length;
        }
        request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var parsedType)
            ? parsedType
            : new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Supabase storage upload failed ({response.StatusCode}): {body}");
        }

        var baseUrl = _options.Url.TrimEnd('/');
        return $"{baseUrl}/storage/v1/object/public/{bucket}/{normalizedPath}";
    }

    public async Task<byte[]?> DownloadBytesAsync(string? urlOrPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(urlOrPath))
        {
            return null;
        }

        if (urlOrPath.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = urlOrPath.IndexOf(',');
            if (commaIndex > 0)
            {
                var base64Data = urlOrPath[(commaIndex + 1)..];
                return Convert.FromBase64String(base64Data);
            }
            return null;
        }

        if (Uri.TryCreate(urlOrPath, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await _httpClient.GetByteArrayAsync(uri, cancellationToken);
        }

        if (!IsConfigured)
        {
            return null;
        }

        var normalizedPath = urlOrPath.TrimStart('/');
        using var request = CreateSupabaseRequest(HttpMethod.Get, $"/storage/v1/object/authenticated/{normalizedPath}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private HttpRequestMessage CreateSupabaseRequest(HttpMethod method, string relativePath)
    {
        var baseUrl = _options.Url.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{relativePath}");
        request.Headers.Add("apikey", _options.ServiceRoleKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        return request;
    }
}
