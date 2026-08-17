using System.Globalization;
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

public sealed class ReportWriter : IReportWriter
{
    /// <summary>
    /// How many reports sharing an id are kept.
    /// </summary>
    public const int RetainCount = 5;

    private const string TimestampFormat = "yyyyMMdd'T'HHmmss'Z'";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

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
        if (string.IsNullOrWhiteSpace(report.Id))
        {
            return Result<string>.Fail("Report id is empty.");
        }

        var createFolderResult = await _fileSystem.CreateFolderAsync(folderPath);
        if (createFolderResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to create report folder: '{folderPath}'")
                .WithErrors(createFolderResult);
        }

        var fileName = ComposeFileName(report.Id, report.GeneratedAt);
        var filePath = Path.Combine(folderPath, fileName);

        var content = JsonSerializer.Serialize(report, SerializerOptions);

        var writeResult = await _fileSystem.WriteAllTextAsync(filePath, content);
        if (writeResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to write report: '{filePath}'")
                .WithErrors(writeResult);
        }

        await PruneOldReportsAsync(report.Id, folderPath);

        return filePath;
    }

    private static string ComposeFileName(string reportId, DateTimeOffset generatedAt)
    {
        var timestamp = generatedAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        return $"{reportId}-{timestamp}{ReportDocument.FileExtension}";
    }

    // Retention is best effort: a report that was written successfully is not failed because an
    // older one could not be removed.
    private async Task PruneOldReportsAsync(string reportId, string folderPath)
    {
        var pattern = $"{reportId}-*{ReportDocument.FileExtension}";

        var enumerateResult = await _fileSystem.EnumerateAsync(folderPath, pattern, recursive: false);
        if (enumerateResult.IsFailure)
        {
            _logger.LogWarning(enumerateResult, $"Failed to enumerate reports for pruning in '{folderPath}'");
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
