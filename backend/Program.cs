using System.Text.Json;
using CertificateGenerator.Api.Models;
using CertificateGenerator.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:4200"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<SupabaseStorageService>();
builder.Services.AddSingleton<IBackgroundTaskQueue>(_ => new BackgroundTaskQueue(capacity: 100));
builder.Services.AddSingleton<JobProgressTracker>();
builder.Services.AddSingleton<PdfGeneratorService>();
builder.Services.AddHostedService<DocumentWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }))
    .WithName("HealthCheck");

app.MapPost("/api/documents/preview", async (
    PreviewRequest request,
    PdfGeneratorService pdfGenerator,
    CancellationToken cancellationToken) =>
{
    if (request.Config is null)
    {
        return Results.BadRequest(new { error = "Template config is required." });
    }

    var assets = await pdfGenerator.PrepareAssetsAsync(request.Config, cancellationToken);
    var pdfBytes = pdfGenerator.GenerateSinglePdf(assets, request.Config, request.SampleData);

    return Results.File(pdfBytes, "application/pdf", "preview.pdf");
})
.WithName("PreviewDocument");

app.MapPost("/api/documents/generate", async (
    HttpRequest httpRequest,
    IBackgroundTaskQueue taskQueue,
    JobProgressTracker progressTracker,
    SupabaseStorageService storageService,
    CancellationToken cancellationToken) =>
{
    if (!httpRequest.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data request." });
    }

    var form = await httpRequest.ReadFormAsync(cancellationToken);
    var file = form.Files.GetFile("file");
    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "Excel file ('file') is required." });
    }

    if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Only .xlsx Excel files are supported." });
    }

    var configJson = form["config"].ToString();
    if (string.IsNullOrWhiteSpace(configJson))
    {
        return Results.BadRequest(new { error = "Template configuration ('config') JSON is required." });
    }

    TemplateConfig? config;
    try
    {
        config = JsonSerializer.Deserialize<TemplateConfig>(configJson, jsonOptions);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"Invalid config JSON: {ex.Message}" });
    }

    if (config is null)
    {
        return Results.BadRequest(new { error = "Template configuration cannot be null." });
    }

    var jobId = Guid.NewGuid();
    var tempExcelPath = Path.Combine(Path.GetTempPath(), $"certgen_input_{jobId:N}.xlsx");

    await using (var fs = new FileStream(tempExcelPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
    {
        await file.CopyToAsync(fs, cancellationToken);
    }

    // Optionally archive raw upload to Supabase `raw-uploads` bucket
    if (storageService.IsConfigured)
    {
        await using var uploadStream = File.OpenRead(tempExcelPath);
        await storageService.UploadFileAsync(
            storageService.RawUploadsBucket,
            $"{jobId:N}/{Path.GetFileName(file.FileName)}",
            uploadStream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            cancellationToken);
    }

    var now = DateTimeOffset.UtcNow;
    var jobRecord = new DocumentJobRecord
    {
        Id = jobId,
        Status = JobStatuses.Queued,
        Progress = 0,
        TotalRecords = 0,
        CreatedAt = now,
        UpdatedAt = now
    };

    await storageService.CreateJobAsync(jobRecord, cancellationToken);

    progressTracker.Publish(new JobStatusUpdate(
        jobId,
        JobStatuses.Queued,
        0,
        0,
        null,
        null,
        now));

    await taskQueue.QueueWorkItemAsync(new GenerateJobWorkItem(jobId, tempExcelPath, config), cancellationToken);

    var response = new GenerateJobQueuedResponse(
        jobId,
        JobStatuses.Queued,
        $"/api/documents/stream/{jobId}",
        $"/api/documents/status/{jobId}");

    return Results.Accepted($"/api/documents/status/{jobId}", response);
})
.WithName("GenerateDocuments")
.DisableAntiforgery();

app.MapGet("/api/documents/stream/{jobId:guid}", async (
    Guid jobId,
    HttpContext context,
    JobProgressTracker progressTracker,
    SupabaseStorageService storageService,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";

    // If the job is not in memory (e.g. after server restart), check Supabase DB first
    if (progressTracker.GetLatestStatus(jobId) is null)
    {
        var dbJob = await storageService.GetJobAsync(jobId, cancellationToken);
        if (dbJob is not null && JobStatuses.IsTerminal(dbJob.Status))
        {
            var terminalUpdate = new JobStatusUpdate(
                dbJob.Id,
                dbJob.Status,
                dbJob.Progress,
                dbJob.TotalRecords,
                dbJob.ZipDownloadUrl,
                dbJob.ErrorMessage,
                dbJob.UpdatedAt);

            var payload = JsonSerializer.Serialize(terminalUpdate, jsonOptions);
            await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            return;
        }
    }

    await foreach (var update in progressTracker.SubscribeAsync(jobId, cancellationToken))
    {
        var payload = JsonSerializer.Serialize(update, jsonOptions);
        await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }
})
.WithName("StreamJobProgress");

app.MapGet("/api/documents/status/{jobId:guid}", async (
    Guid jobId,
    JobProgressTracker progressTracker,
    SupabaseStorageService storageService,
    CancellationToken cancellationToken) =>
{
    var latest = progressTracker.GetLatestStatus(jobId);
    if (latest is not null)
    {
        return Results.Ok(latest);
    }

    var dbJob = await storageService.GetJobAsync(jobId, cancellationToken);
    if (dbJob is null)
    {
        return Results.NotFound(new { error = $"Job {jobId} not found." });
    }

    return Results.Ok(new JobStatusUpdate(
        dbJob.Id,
        dbJob.Status,
        dbJob.Progress,
        dbJob.TotalRecords,
        dbJob.ZipDownloadUrl,
        dbJob.ErrorMessage,
        dbJob.UpdatedAt));
})
.WithName("GetJobStatus");

app.Run();
