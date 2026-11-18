using System.Text.RegularExpressions;
using Celbridge.Logging;
using Celbridge.Projects.Services;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Enables the python feature in the project file, because every project could run Python before the feature
/// existed. The step adds python = true to the features table on the [celbridge] table, and adds the table when the
/// file has none. A file that already names the python feature stays the same, and so does the rest of the file.
/// </summary>
public sealed class PythonFeatureMigrationStep : IMigrationStep
{
    private const string PythonFeatureKey = "python";

    private static readonly Regex TableHeaderRegex = new(@"^\s*\[");
    private static readonly Regex CelbridgeHeaderRegex = new(@"^\s*\[\s*celbridge\s*\]\s*(#.*)?$");
    private static readonly Regex FeaturesHeaderRegex = new(@"^\s*\[\s*celbridge\s*\.\s*features\s*\]\s*(#.*)?$");
    private static readonly Regex KeyLineRegex = new(@"^\s*[^\s#]");

    // Matches a features inline table written on one line, which is the form Celbridge writes. Feature names never
    // contain a brace.
    private static readonly Regex InlineFeaturesRegex = new(@"^(?<indent>\s*)features\s*=\s*\{(?<entries>[^{}]*)\}(?<rest>.*)$");

    // Matches one feature written as a dotted key under the [celbridge] header, for example
    // features.note-editor = true.
    private static readonly Regex DottedFeatureRegex = new(@"^(?<indent>\s*)features\s*\.");

    public SemanticVersion TargetVersion { get; } = new SemanticVersion(1, 8, 0);

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var hasFeaturesTable = JsonPointerToml.TryResolve(context.Configuration, "/celbridge/features", out var featuresObject, out _);
        if (hasFeaturesTable)
        {
            // A features value that is not a table is reported when the project loads.
            if (featuresObject is not TomlTable featuresTable
                || featuresTable.ContainsKey(PythonFeatureKey))
            {
                return Result.Ok();
            }
        }

        var readResult = await context.FileSystem.ReadAllTextAsync(context.ProjectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail("Failed to read the project file to enable the python feature")
                .WithErrors(readResult);
        }

        var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
        var endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline)
        {
            text = text[..^1];
        }
        var lines = text.Split('\n').ToList();

        var added = hasFeaturesTable
            ? AddToFeaturesTable(lines)
            : AddFeaturesTable(lines);

        var migratedText = string.Join('\n', lines);
        if (endsWithNewline)
        {
            migratedText += '\n';
        }

        // The edit is checked before the write. The step leaves the file unchanged when it does not recognize how the
        // table is written. The python feature then stays disabled, and the user can enable it in Project Settings.
        if (!added
            || SyntaxParser.Parse(migratedText).HasErrors)
        {
            context.Logger.LogWarning("Could not enable the python feature in the project file. It can be enabled in the Features section of Project Settings.");
            return Result.Ok();
        }

        var writeResult = await context.WriteProjectFileAsync(migratedText);
        if (writeResult.IsFailure)
        {
            return Result.Fail("Failed to enable the python feature in the project file")
                .WithErrors(writeResult);
        }

        context.Logger.LogInformation("Enabled the python feature in the project file");

        return Result.Ok();
    }

    // Adds a features inline table after the last key of the [celbridge] table. The comment lines directly above the
    // next header stay with the next table. Returns false when the file has no [celbridge] header.
    private static bool AddFeaturesTable(List<string> lines)
    {
        var headerIndex = lines.FindIndex(line => CelbridgeHeaderRegex.IsMatch(line));
        if (headerIndex < 0)
        {
            return false;
        }

        var insertIndex = headerIndex + 1;
        var endIndex = FindNextTableHeader(lines, headerIndex + 1);
        for (var index = headerIndex + 1; index < endIndex; index++)
        {
            if (KeyLineRegex.IsMatch(lines[index]))
            {
                insertIndex = index + 1;
            }
        }

        lines.Insert(insertIndex, $"features = {{ {PythonFeatureKey} = true }}");

        return true;
    }

    // Adds the python feature to the features table on the [celbridge] table. The table can be an inline table, a
    // set of dotted keys, or a [celbridge.features] header. Returns false when the file writes the table in another
    // form.
    private static bool AddToFeaturesTable(List<string> lines)
    {
        var headerIndex = lines.FindIndex(line => CelbridgeHeaderRegex.IsMatch(line));
        if (headerIndex >= 0)
        {
            var endIndex = FindNextTableHeader(lines, headerIndex + 1);
            var lastDottedIndex = -1;
            for (var index = headerIndex + 1; index < endIndex; index++)
            {
                if (DottedFeatureRegex.IsMatch(lines[index]))
                {
                    lastDottedIndex = index;
                    continue;
                }

                var match = InlineFeaturesRegex.Match(lines[index]);
                if (!match.Success)
                {
                    continue;
                }

                var entries = match.Groups["entries"].Value.Trim();
                var newEntries = entries.Length == 0
                    ? $"{PythonFeatureKey} = true"
                    : $"{entries}, {PythonFeatureKey} = true";

                lines[index] = $"{match.Groups["indent"].Value}features = {{ {newEntries} }}{match.Groups["rest"].Value}";

                return true;
            }

            if (lastDottedIndex >= 0)
            {
                var lastDottedMatch = DottedFeatureRegex.Match(lines[lastDottedIndex]);
                var indent = lastDottedMatch.Groups["indent"].Value;

                lines.Insert(lastDottedIndex + 1, $"{indent}features.{PythonFeatureKey} = true");

                return true;
            }
        }

        var featuresHeaderIndex = lines.FindIndex(line => FeaturesHeaderRegex.IsMatch(line));
        if (featuresHeaderIndex >= 0)
        {
            lines.Insert(featuresHeaderIndex + 1, $"{PythonFeatureKey} = true");

            return true;
        }

        return false;
    }

    private static int FindNextTableHeader(List<string> lines, int startIndex)
    {
        for (var index = startIndex; index < lines.Count; index++)
        {
            if (TableHeaderRegex.IsMatch(lines[index]))
            {
                return index;
            }
        }

        return lines.Count;
    }
}
