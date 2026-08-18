using System.Text.Json;
using System.Text.Json.Serialization;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Reports;
using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Writes a ResourceKey as its string form so report items carry readable keys.
/// </summary>
internal sealed class ReportResourceKeyConverter : JsonConverter<ResourceKey>
{
    public override void Write(Utf8JsonWriter writer, ResourceKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override ResourceKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var key = reader.GetString() ?? string.Empty;
        if (!ResourceKey.TryCreate(key, out var resourceKey))
        {
            throw new JsonException($"Invalid resource key: '{key}'");
        }

        return resourceKey;
    }
}

/// <summary>
/// Writes a ReportCode as its string form so a reader that does not know the code still renders it.
/// </summary>
internal sealed class ReportCodeConverter : JsonConverter<ReportCode>
{
    public override void Write(Utf8JsonWriter writer, ReportCode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override ReportCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? string.Empty;
        if (!ReportCode.TryParse(value, out var code))
        {
            throw new JsonException($"Invalid report code: '{value}'");
        }

        return code;
    }
}

/// <summary>
/// The serialized form of a report. Owns the JSON settings so writing a report and reading one back
/// cannot disagree about the encoding.
/// </summary>
public static class ReportSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    /// <summary>
    /// Serializes a report to its file content.
    /// </summary>
    public static string Serialize(ReportDocument report)
    {
        return JsonSerializer.Serialize(report, SerializerOptions);
    }

    /// <summary>
    /// Parses report file content. Fails rather than throwing on anything that is not a report this
    /// build can render, since the content can come from a contribution.
    /// </summary>
    public static Result<ReportDocument> Deserialize(string content)
    {
        try
        {
            var report = JsonSerializer.Deserialize<ReportDocument>(content, SerializerOptions);
            if (report is null)
            {
                return Result<ReportDocument>.Fail("Report content is null.");
            }

            return report;
        }
        catch (JsonException ex)
        {
            return Result<ReportDocument>.Fail($"Report content could not be parsed: {ex.Message}");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new ReportResourceKeyConverter());
        options.Converters.Add(new ReportCodeConverter());

        return options;
    }
}

public sealed class ReportWriter : IReportWriter
{
    /// <summary>
    /// How many superseded reports are kept in the history folder, per report id. The current report
    /// is not one of them.
    /// </summary>
    public const int RetainCount = 5;

    /// <summary>
    /// Sub-folder holding the superseded reports for every id.
    /// </summary>
    public const string HistoryFolderName = "history";

    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<ReportWriter> _logger;

    public ReportWriter(
        ILocalFileSystem fileSystem,
        ILogger<ReportWriter> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<Result<string>> WriteReportAsync(ReportDocument report, string folderPath)
    {
        if (!ReportLocation.IsValidReportId(report.Id))
        {
            return Result<string>.Fail(
                $"Invalid report id: '{report.Id}'. Expected lowercase letters, digits, hyphens and dots.");
        }

        var createFolderResult = await _fileSystem.CreateFolderAsync(folderPath);
        if (createFolderResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to create report folder: '{folderPath}'")
                .WithErrors(createFolderResult);
        }

        var filePath = Path.Combine(folderPath, $"{report.Id}{ReportDocument.FileExtension}");

        await ArchiveCurrentReportAsync(report, filePath, folderPath);

        var content = ReportSerializer.Serialize(report);

        var writeResult = await _fileSystem.WriteAllTextAsync(filePath, content);
        if (writeResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to write report: '{filePath}'")
                .WithErrors(writeResult);
        }

        await PruneHistoryAsync(report.Id, folderPath);

        return filePath;
    }

    // Moving rather than overwriting is what keeps the current report at a stable, addressable path
    // without any earlier report being lost. Best effort: a report that cannot be archived is left in
    // place to be overwritten, since failing the write would lose the newer report instead of an older
    // one.
    private async Task ArchiveCurrentReportAsync(ReportDocument report, string filePath, string folderPath)
    {
        var infoResult = await _fileSystem.GetInfoAsync(filePath);
        if (infoResult.IsFailure ||
            infoResult.Value.Kind != StorageItemKind.File)
        {
            return;
        }

        var currentGeneratedAt = await ReadGeneratedAtAsync(filePath, infoResult.Value.ModifiedUtc);

        // A producer that flushes more than once during an operation rewrites one report rather than
        // producing several, so history gains an entry per generation, not per write.
        if (currentGeneratedAt == report.GeneratedAt)
        {
            return;
        }

        var historyFolderPath = Path.Combine(folderPath, HistoryFolderName);

        var createFolderResult = await _fileSystem.CreateFolderAsync(historyFolderPath);
        if (createFolderResult.IsFailure)
        {
            _logger.LogWarning(createFolderResult, $"Failed to create report history folder: '{historyFolderPath}'");
            return;
        }

        var timestamp = FileTimestamp.Compose(currentGeneratedAt);
        var historyFileName = $"{report.Id}-{timestamp}{ReportDocument.FileExtension}";
        var historyFilePath = Path.Combine(historyFolderPath, historyFileName);

        var moveResult = await _fileSystem.MoveFileAsync(filePath, historyFilePath);
        if (moveResult.IsFailure)
        {
            _logger.LogWarning(moveResult, $"Failed to archive the previous report: '{filePath}'");
        }
    }

    // The report's own stamp names the generation being archived. A report that cannot be read falls
    // back to when it was written, so an unreadable file still archives under a plausible name rather
    // than blocking the rotation.
    private async Task<DateTimeOffset> ReadGeneratedAtAsync(string filePath, DateTime modifiedUtc)
    {
        var fallback = new DateTimeOffset(modifiedUtc, TimeSpan.Zero);

        var readResult = await _fileSystem.ReadAllTextAsync(filePath);
        if (readResult.IsFailure)
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(readResult.Value);
            if (document.RootElement.TryGetProperty("generatedAt", out var generatedAt) &&
                generatedAt.TryGetDateTimeOffset(out var parsed))
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            // Falls through to the write time below.
        }

        return fallback;
    }

    // Retention is best effort: a report that was written successfully is not failed because an
    // older one could not be removed.
    private async Task PruneHistoryAsync(string reportId, string folderPath)
    {
        var historyFolderPath = Path.Combine(folderPath, HistoryFolderName);
        var pattern = $"{reportId}-*{ReportDocument.FileExtension}";

        var enumerateResult = await _fileSystem.EnumerateAsync(historyFolderPath, pattern, recursive: false);
        if (enumerateResult.IsFailure)
        {
            // The folder does not exist until the first report is superseded.
            return;
        }

        var entries = enumerateResult.Value;

        // The timestamp is fixed width, so ordering by name descending puts the newest first.
        var staleReports = entries
            .Where(entry => !entry.IsFolder)
            .OrderByDescending(entry => entry.FullPath, StringComparer.Ordinal)
            .Skip(RetainCount)
            .ToList();

        foreach (var staleReport in staleReports)
        {
            var deleteResult = await _fileSystem.DeleteFileAsync(staleReport.FullPath);
            if (deleteResult.IsFailure)
            {
                _logger.LogWarning(deleteResult, $"Failed to delete stale report: '{staleReport.FullPath}'");
            }
        }
    }
}
