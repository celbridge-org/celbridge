using Celbridge.Utilities;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.Console.Helpers;

/// <summary>
/// A trigger parsed from a .console file: the resource path pattern it watches and the command template
/// injected when a matching resource changes.
/// </summary>
public sealed record ConsoleDocumentTrigger(
    string Pattern,
    string Command);

/// <summary>
/// The launch-relevant configuration parsed from a .console file. Shortcuts are not represented: they are
/// a client-side toolbar the host never consumes.
/// </summary>
public sealed record ConsoleDocumentConfig(
    string Type,
    string Executable,
    string PythonVersion,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> Dependencies,
    string WorkingDirectory,
    string StartupScript,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<ConsoleRunner> Runners,
    IReadOnlyList<string> DisabledBuiltInRunners,
    IReadOnlyList<ConsoleDocumentTrigger> Triggers);

/// <summary>
/// Parses .console TOML into the launch configuration. The settings form edits the same file with its own
/// parser (console-toml.js); this one is authoritative for launching.
/// </summary>
public static class ConsoleDocumentConfigParser
{
    public static Result<ConsoleDocumentConfig> Parse(string tomlText)
    {
        // Tomlyn rejects bare-\r line terminators, so normalize before parsing.
        var text = LineEndingHelper.ConvertLineEndings(tomlText ?? string.Empty, "\n");

        var parse = SyntaxParser.Parse(text);
        if (parse.HasErrors)
        {
            var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
            return Result<ConsoleDocumentConfig>.Fail($"Invalid .console configuration: {errors}");
        }

        var model = TomlSerializer.Deserialize<TomlTable>(text);
        if (model is null)
        {
            return Result<ConsoleDocumentConfig>.Fail("Invalid .console configuration: failed to deserialize");
        }

        var session = GetTable(model, "session");
        var options = session is null ? null : GetTable(session, "options");
        var environmentTable = session is null ? null : GetTable(session, "environment");

        var environment = new Dictionary<string, string>();
        if (environmentTable is not null)
        {
            foreach (var pair in environmentTable)
            {
                if (pair.Value is string value)
                {
                    environment[pair.Key] = value;
                }
            }
        }

        var runners = new List<ConsoleRunner>();
        if (session is not null &&
            session.TryGetValue("runner", out var runnerValue) &&
            runnerValue is TomlTableArray runnerTables)
        {
            foreach (var runnerTable in runnerTables)
            {
                var extensions = GetStringList(runnerTable, "extensions");
                var command = GetString(runnerTable, "command");
                if (extensions.Count > 0 &&
                    !string.IsNullOrWhiteSpace(command))
                {
                    runners.Add(new ConsoleRunner(extensions, command));
                }
            }
        }

        var triggers = new List<ConsoleDocumentTrigger>();
        if (session is not null &&
            session.TryGetValue("trigger", out var triggerValue) &&
            triggerValue is TomlTableArray triggerTables)
        {
            foreach (var triggerTable in triggerTables)
            {
                var pattern = GetString(triggerTable, "pattern");
                var triggerCommand = GetString(triggerTable, "command");
                if (string.IsNullOrWhiteSpace(pattern) ||
                    string.IsNullOrWhiteSpace(triggerCommand))
                {
                    continue;
                }

                triggers.Add(new ConsoleDocumentTrigger(pattern, triggerCommand));
            }
        }

        var config = new ConsoleDocumentConfig(
            GetString(session, "type", "shell"),
            GetString(options, "executable"),
            GetString(options, "python_version"),
            GetStringList(options, "arguments"),
            GetStringList(options, "dependencies"),
            GetString(session, "working_directory"),
            GetString(session, "startup_script"),
            environment,
            runners,
            GetStringList(session, "disabled_built_in_runners"),
            triggers);

        return config;
    }

    private static TomlTable? GetTable(TomlTable parent, string key)
    {
        if (parent.TryGetValue(key, out var value) &&
            value is TomlTable table)
        {
            return table;
        }

        return null;
    }

    private static string GetString(TomlTable? table, string key, string defaultValue = "")
    {
        if (table is not null &&
            table.TryGetValue(key, out var value) &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return defaultValue;
    }

    private static IReadOnlyList<string> GetStringList(TomlTable? table, string key)
    {
        var items = new List<string>();
        if (table is not null &&
            table.TryGetValue(key, out var value) &&
            value is TomlArray array)
        {
            foreach (var item in array)
            {
                if (item is string text &&
                    !string.IsNullOrWhiteSpace(text))
                {
                    items.Add(text);
                }
            }
        }

        return items;
    }
}
