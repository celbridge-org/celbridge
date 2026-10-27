using System.Text.RegularExpressions;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Projects.Services;
using Tomlyn.Model;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Names one setting to move, from SourceKey on its old table to NewKey on the [celbridge.TableKey] table.
/// </summary>
internal sealed record SettingMove(TomlTable SourceTable, string SourceKey, string TableKey, string NewKey);

/// <summary>
/// Collects the key lines of one new [celbridge.TableKey] table.
/// </summary>
internal sealed record NewTable(string TableKey, List<string> KeyLines);

/// <summary>
/// Moves the settings from the [celbridge.resources] table and the disabled-packages key on the [celbridge] table
/// into the [celbridge.explorer], [celbridge.search], [celbridge.downloads] and [celbridge.packages] tables. The
/// values are copied unchanged, and the rest of the file stays the same. Packages load only from the subfolders of
/// a package folder. The step therefore lists every folder that contains a package, other than the packages folder.
/// </summary>
public sealed class ResourceSettingsMigrationStep : IMigrationStep
{
    private const string CelbridgeTableKey = "celbridge";
    private const string ResourcesTableKey = "resources";
    private const string DisabledPackagesKey = "disabled-packages";

    private static readonly Regex TableHeaderRegex = new(@"^\s*\[");
    private static readonly Regex CelbridgeHeaderRegex = new(@"^\s*\[\s*celbridge\s*\]\s*(#.*)?$");
    private static readonly Regex ResourcesHeaderRegex = new(@"^\s*\[\s*celbridge\s*\.\s*resources\s*\]\s*(#.*)?$");
    private static readonly Regex CommentLineRegex = new(@"^\s*#");
    private static readonly Regex DisabledPackagesLineRegex = new($@"^\s*{DisabledPackagesKey}\s*=");

    public SemanticVersion TargetVersion { get; } = new SemanticVersion(1, 6, 0);

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        if (!context.Configuration.TryGetValue(CelbridgeTableKey, out var celbridgeObject)
            || celbridgeObject is not TomlTable celbridgeTable)
        {
            return Result.Ok();
        }

        var packageFolders = await FindPackageFoldersAsync(context);

        var hasResourcesTable = FindResourcesTable(celbridgeTable) is not null;
        var hasDisabledPackagesKey = celbridgeTable.ContainsKey(DisabledPackagesKey);
        if (!hasResourcesTable
            && !hasDisabledPackagesKey
            && packageFolders.Count == 0)
        {
            return Result.Ok();
        }

        var tableLines = BuildTableLines(context, celbridgeTable, packageFolders);

        var readResult = await context.FileSystem.ReadAllTextAsync(context.ProjectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail("Failed to read the project file to move its resource settings")
                .WithErrors(readResult);
        }

        var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
        var endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline)
        {
            text = text[..^1];
        }
        var lines = text.Split('\n').ToList();

        RemoveDisabledPackagesKey(lines);
        var insertIndex = RemoveResourcesTable(lines) ?? FindCelbridgeTableEnd(lines);
        InsertTableLines(lines, insertIndex, tableLines);

        var migratedText = string.Join('\n', lines);
        if (endsWithNewline)
        {
            migratedText += '\n';
        }

        var writeResult = await context.WriteProjectFileAsync(migratedText);
        if (writeResult.IsFailure)
        {
            return Result.Fail("Failed to move the resource settings in the project file")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }

    private static TomlTable? FindResourcesTable(TomlTable celbridgeTable)
    {
        if (!celbridgeTable.TryGetValue(ResourcesTableKey, out var resourcesObject))
        {
            return null;
        }

        return resourcesObject as TomlTable;
    }

    // Returns the package folders to list in the project file, as paths from the project root. A package is a folder
    // with a package.toml file. The search skips the .git folder and links, and stops at each package.
    private static async Task<List<string>> FindPackageFoldersAsync(MigrationContext context)
    {
        var packageFolders = new List<string>();
        await CollectPackageFoldersAsync(context, context.ProjectFolderPath, packageFolders);

        return packageFolders;
    }

    private static async Task CollectPackageFoldersAsync(MigrationContext context, string folderPath, List<string> packageFolders)
    {
        var enumerateResult = await context.FileSystem.EnumerateAsync(folderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            context.Logger.LogWarning(enumerateResult, $"Failed to list a folder while looking for packages: '{folderPath}'");
            return;
        }
        var entries = enumerateResult.Value;

        var hasManifest = entries.Any(entry => !entry.IsFolder
            && !entry.IsLink
            && string.Equals(Path.GetFileName(entry.FullPath), PackageConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase));
        if (hasManifest)
        {
            AddPackageFolder(context, folderPath, packageFolders);
            return;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry.FullPath);
            if (!entry.IsFolder
                || entry.IsLink
                || string.Equals(name, ProjectConstants.GitFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await CollectPackageFoldersAsync(context, entry.FullPath, packageFolders);
        }
    }

    // Adds the folder that contains the package. Skips the packages folder and a folder already in the list. Logs a
    // warning for a package directly inside the project folder. Loading the project drops the root as a package folder.
    private static void AddPackageFolder(MigrationContext context, string packagePath, List<string> packageFolders)
    {
        var relativePath = Path.GetRelativePath(context.ProjectFolderPath, packagePath)
            .Replace(Path.DirectorySeparatorChar, '/');

        var parentPath = string.Empty;
        var separatorIndex = relativePath.LastIndexOf('/');
        if (separatorIndex >= 0)
        {
            parentPath = relativePath[..separatorIndex];
        }

        var problem = PackageFolderPath.FindProblem(parentPath, packageFolders, out var folder);
        switch (problem)
        {
            case PackageFolderProblem.None:
                packageFolders.Add(folder.Path);
                break;

            case PackageFolderProblem.AlreadyListed:
                break;

            default:
                context.Logger.LogWarning($"The package at '{relativePath}' is outside every package folder. Move it into the packages folder to load it.");
                break;
        }
    }

    // Returns the lines of each new table, with a blank line between tables. Drops a table when the project file
    // already has it, since TOML refuses a second header for a table.
    private static List<string> BuildTableLines(MigrationContext context, TomlTable celbridgeTable, IReadOnlyList<string> packageFolders)
    {
        var tables = new List<NewTable>();

        var resourcesTable = FindResourcesTable(celbridgeTable);
        if (resourcesTable is not null)
        {
            AddSetting(context, tables, new SettingMove(resourcesTable, "hide", "explorer", "exclude"));
            AddSetting(context, tables, new SettingMove(resourcesTable, "search-exclude", "search", "exclude"));
            AddSetting(context, tables, new SettingMove(resourcesTable, "downloads-folder", "downloads", "folder"));
        }

        if (packageFolders.Count > 0)
        {
            AddKeyLine(tables, "packages", $"folders = {RenderStringList(packageFolders)}");
        }

        AddSetting(context, tables, new SettingMove(celbridgeTable, DisabledPackagesKey, "packages", "disabled"));

        var tableLines = new List<string>();
        foreach (var table in tables)
        {
            var tableName = $"{CelbridgeTableKey}.{table.TableKey}";
            if (celbridgeTable.ContainsKey(table.TableKey))
            {
                context.Logger.LogWarning($"Dropped the settings for the [{tableName}] table because the project file already has that table");
                continue;
            }

            if (tableLines.Count > 0)
            {
                tableLines.Add(string.Empty);
            }
            tableLines.Add($"[{tableName}]");
            tableLines.AddRange(table.KeyLines);

            context.Logger.LogInformation($"Added the [{tableName}] table to the project file");
        }

        return tableLines;
    }

    // Adds the setting to its new table. Drops the setting when the value is neither a string nor a list.
    private static void AddSetting(MigrationContext context, List<NewTable> tables, SettingMove move)
    {
        if (!move.SourceTable.TryGetValue(move.SourceKey, out var value))
        {
            return;
        }

        var renderedValue = RenderValue(value);
        if (renderedValue is null)
        {
            context.Logger.LogWarning($"Dropped the '{move.SourceKey}' setting because its value is neither a string nor a list");
            return;
        }

        AddKeyLine(tables, move.TableKey, $"{move.NewKey} = {renderedValue}");
    }

    private static void AddKeyLine(List<NewTable> tables, string tableKey, string keyLine)
    {
        var table = tables.Find(existingTable => existingTable.TableKey == tableKey);
        if (table is null)
        {
            table = new NewTable(tableKey, new List<string>());
            tables.Add(table);
        }

        table.KeyLines.Add(keyLine);
    }

    // Renders a string or a list of strings as TOML. Skips any list entry that is not a string.
    private static string? RenderValue(object value)
    {
        if (value is string stringValue)
        {
            return TomlStringEncoder.EncodeBasicString(stringValue);
        }

        if (value is TomlArray array)
        {
            return RenderStringList(array.OfType<string>());
        }

        return null;
    }

    private static string RenderStringList(IEnumerable<string> values)
    {
        var items = values.Select(TomlStringEncoder.EncodeBasicString);

        return $"[{string.Join(", ", items)}]";
    }

    // Removes the disabled-packages key from the [celbridge] table. Package names never contain a bracket. A list
    // that spans lines therefore ends at the first line with a closing bracket.
    private static void RemoveDisabledPackagesKey(List<string> lines)
    {
        var inCelbridgeTable = false;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (TableHeaderRegex.IsMatch(line))
            {
                inCelbridgeTable = CelbridgeHeaderRegex.IsMatch(line);
                continue;
            }

            if (!inCelbridgeTable
                || !DisabledPackagesLineRegex.IsMatch(line))
            {
                continue;
            }

            var endIndex = index;
            while (endIndex < lines.Count - 1
                && !lines[endIndex].Contains(']'))
            {
                endIndex++;
            }

            lines.RemoveRange(index, endIndex - index + 1);
            return;
        }
    }

    // Removes the [celbridge.resources] table and the comment lines directly above its header. Returns the index
    // where the table started, or null when the file has no [celbridge.resources] header.
    private static int? RemoveResourcesTable(List<string> lines)
    {
        var headerIndex = lines.FindIndex(line => ResourcesHeaderRegex.IsMatch(line));
        if (headerIndex < 0)
        {
            return null;
        }

        var startIndex = headerIndex;
        while (startIndex > 0
            && CommentLineRegex.IsMatch(lines[startIndex - 1]))
        {
            startIndex--;
        }

        var endIndex = FindNextTableHeader(lines, headerIndex + 1);
        lines.RemoveRange(startIndex, endIndex - startIndex);

        return startIndex;
    }

    // Returns the index of the line after the [celbridge] table, or the end of the file when the file has no
    // [celbridge] header.
    private static int FindCelbridgeTableEnd(List<string> lines)
    {
        var headerIndex = lines.FindIndex(line => CelbridgeHeaderRegex.IsMatch(line));
        if (headerIndex < 0)
        {
            return lines.Count;
        }

        return FindNextTableHeader(lines, headerIndex + 1);
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

    // Inserts the tables, and adds a blank line before and after them where they would touch other lines.
    private static void InsertTableLines(List<string> lines, int insertIndex, List<string> tableLines)
    {
        if (tableLines.Count == 0)
        {
            return;
        }

        var block = new List<string>();
        if (insertIndex > 0
            && !string.IsNullOrWhiteSpace(lines[insertIndex - 1]))
        {
            block.Add(string.Empty);
        }
        block.AddRange(tableLines);
        if (insertIndex < lines.Count
            && !string.IsNullOrWhiteSpace(lines[insertIndex]))
        {
            block.Add(string.Empty);
        }

        lines.InsertRange(insertIndex, block);
    }
}
