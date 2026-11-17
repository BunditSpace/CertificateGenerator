using System.IO.Compression;
using CertificateGenerator.Api.Models;
using ClosedXML.Excel;

namespace CertificateGenerator.Api.Services;

public sealed class DocumentWorker : BackgroundService
{
    private const int ProgressBatchInterval = 10;

    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly JobProgressTracker _progressTracker;
    private readonly PdfGeneratorService _pdfGenerator;
    private readonly SupabaseStorageService _storageService;
    private readonly ILogger<DocumentWorker> _logger;

    public DocumentWorker(
        IBackgroundTaskQueue taskQueue,
        JobProgressTracker progressTracker,
        PdfGeneratorService pdfGenerator,
        SupabaseStorageService storageService,
        ILogger<DocumentWorker> logger)
    {
        _taskQueue = taskQueue;
        _progressTracker = progressTracker;
        _pdfGenerator = pdfGenerator;
        _storageService = storageService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentWorker started and listening for queued jobs.");

        while (!stoppingToken.IsCancellationRequested)
        {
            GenerateJobWorkItem workItem;
            try
            {
                workItem = await _taskQueue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessJobAsync(workItem, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(GenerateJobWorkItem workItem, CancellationToken cancellationToken)
    {
        var jobId = workItem.JobId;
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"certgen_{jobId:N}.zip");
        var totalRecords = 0;
        var processed = 0;

        try
        {
            await PublishAndPersistAsync(jobId, JobStatuses.Processing, 0, 0, null, null, cancellationToken);

            // 1. Download & pre-process template images once per job
            var assets = await _pdfGenerator.PrepareAssetsAsync(workItem.Config, cancellationToken);

            // 2. Open Excel workbook and read header + data rows
            using var workbook = new XLWorkbook(workItem.ExcelFilePath);
            var worksheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidOperationException("Excel workbook contains no worksheets.");

            var usedRange = worksheet.RangeUsed();
            if (usedRange is null || usedRange.RowCount() < 2)
            {
                throw new InvalidOperationException("Excel worksheet must contain a header row and at least one data row.");
            }

            var headerRow = usedRange.FirstRow();
            var columnMap = new Dictionary<int, string>();
            foreach (var cell in headerRow.CellsUsed())
            {
                var headerName = cell.GetString().Trim();
                if (!string.IsNullOrEmpty(headerName))
                {
                    columnMap[cell.Address.ColumnNumber] = headerName;
                }
            }

            if (columnMap.Count == 0)
            {
                throw new InvalidOperationException("Excel header row has no column names.");
            }

            var dataRows = usedRange.RowsUsed().Skip(1)
                .Where(r => r.CellsUsed().Any(c => !string.IsNullOrWhiteSpace(c.GetFormattedString())))
                .ToList();

            totalRecords = dataRows.Count;
            if (totalRecords == 0)
            {
                throw new InvalidOperationException("Excel worksheet contains no non-empty data rows.");
            }

            await PublishAndPersistAsync(jobId, JobStatuses.Processing, 0, totalRecords, null, null, cancellationToken);

            // 3. Stream PDFs directly into a FileStream-backed ZipArchive (memory-safe)
            await using (var zipFileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var archive = new ZipArchive(zipFileStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var row in dataRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (colNum, colName) in columnMap)
                    {
                        var cellValue = row.Cell(colNum).GetFormattedString().Trim();
                        rowData[colName] = cellValue;
                    }

                    var pdfBytes = _pdfGenerator.GenerateSinglePdf(assets, workItem.Config, rowData);
                    processed++;

                    var entryFileName = BuildPdfFileName(processed, rowData);
                    var entry = archive.CreateEntry(entryFileName, CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(pdfBytes, cancellationToken);

                    if (processed % ProgressBatchInterval == 0 || processed == totalRecords)
                    {
                        await PublishAndPersistAsync(
                            jobId,
                            JobStatuses.Processing,
                            processed,
                            totalRecords,
                            null,
                            null,
                            cancellationToken);
                    }
                }
            }

            // 4. Upload completed ZIP to Supabase Storage `outputs` bucket
            string? zipDownloadUrl = null;
            if (_storageService.IsConfigured)
            {
                await using var uploadStream = File.OpenRead(tempZipPath);
                zipDownloadUrl = await _storageService.UploadFileAsync(
                    _storageService.OutputsBucket,
                    $"{jobId:N}/documents.zip",
                    uploadStream,
                    "application/zip",
                    cancellationToken);
            }

            await PublishAndPersistAsync(
                jobId,
                JobStatuses.Completed,
                totalRecords,
                totalRecords,
                zipDownloadUrl,
                null,
                cancellationToken);

            _logger.LogInformation("Job {JobId} completed ({Count} PDFs).", jobId, totalRecords);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Job {JobId} cancelled due to application shutdown.", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed after processing {Processed}/{Total} records.", jobId, processed, totalRecords);
            await PublishAndPersistAsync(
                jobId,
                JobStatuses.Failed,
                processed,
                totalRecords,
                null,
                ex.Message,
                CancellationToken.None);
        }
        finally
        {
            TryDeleteFile(workItem.ExcelFilePath);
            TryDeleteFile(tempZipPath);
        }
    }

    private async Task PublishAndPersistAsync(
        Guid jobId,
        string status,
        int progress,
        int totalRecords,
        string? zipDownloadUrl,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var update = new JobStatusUpdate(
            jobId,
            status,
            progress,
            totalRecords,
            zipDownloadUrl,
            errorMessage,
            DateTimeOffset.UtcNow);

        _progressTracker.Publish(update);

        try
        {
            await _storageService.UpdateJobAsync(
                jobId,
                status,
                progress,
                totalRecords,
                zipDownloadUrl,
                errorMessage,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist status update for job {JobId} to Supabase.", jobId);
        }
    }

    private static string BuildPdfFileName(int index, IReadOnlyDictionary<string, string> rowData)
    {
        var candidate = rowData.TryGetValue("FullName", out var fullName) && !string.IsNullOrWhiteSpace(fullName)
            ? fullName
            : rowData.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "document";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(candidate
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "document";
        }

        if (sanitized.Length > 60)
        {
            sanitized = sanitized[..60];
        }

        return $"{index:D4}_{sanitized}.pdf";
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temporary file {Path}", path);
        }
    }
}
