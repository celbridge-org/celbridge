using Celbridge.Utilities;
using System.Text.Json;
using Tomlyn;

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
    IReadOnlyList<ConsoleDocumentTrigger> Triggers)
{
    /// <summary>
    /// Keys the document declared that the host does not define, each named by its section (for example
    /// "session.startup-script"). The document still launches, so this is advisory.
    /// </summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Parses .console TOML into the launch configuration. The settings form edits the same file with its own
/// parser (console-toml.js); this one is authoritative for launching.
/// </summary>
public static class ConsoleDocumentConfigParser
{
    private const string SessionSection = "session";
    private const string OptionsSection = "session.options";
    private const string RunnerSection = "session.runner";
    private const string TriggerSection = "session.trigger";
    private const string ShortcutSection = "session.shortcut";

    private const string DefaultSessionType = "shell";

    // Document keys are the snake_case spelling of the model's property names.
    private static readonly TomlSerializerOptions DocumentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static Result<ConsoleDocumentConfig> Parse(string tomlText)
    {
        // Tomlyn rejects bare-\r line terminators, so normalize before parsing.
        var text = LineEndingHelper.ConvertLineEndings(tomlText ?? string.Empty, "\n");

        ConsoleFile? document;
        try
        {
            document = TomlSerializer.Deserialize<ConsoleFile>(text, DocumentOptions);
        }
        catch (TomlException exception)
        {
            // A shape error carries no diagnostic, only a message, so fall back to it.
            var detail = exception.Message;
            if (exception.Diagnostics.Count > 0)
            {
                detail = string.Join("; ", exception.Diagnostics.Select(diagnostic => diagnostic.ToString()));
            }

            return Result<ConsoleDocumentConfig>.Fail($"Invalid .console configuration: {detail}");
        }

        if (document is null)
        {
            return Result<ConsoleDocumentConfig>.Fail("Invalid .console configuration: failed to deserialize");
        }

        var session = document.Session;
        var options = session?.Options;

        var config = new ConsoleDocumentConfig(
            ReadText(session?.Type, DefaultSessionType),
            ReadText(options?.Executable),
            ReadText(options?.PythonVersion),
            ReadTextList(options?.Arguments),
            ReadTextList(options?.Dependencies),
            ReadText(session?.WorkingDirectory),
            ReadText(session?.StartupScript),
            ReadEnvironment(session),
            ReadRunners(session),
            ReadTextList(session?.DisabledBuiltInRunners),
            ReadTriggers(session))
        {
            UnknownFields = CollectUnknownFields(document)
        };

        return config;
    }

    // A blank value carries no more meaning than an absent one, so both fall back to the default.
    private static string ReadText(string? value, string defaultValue = "")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value;
    }

    private static IReadOnlyList<string> ReadTextList(List<string>? values)
    {
        var items = new List<string>();
        if (values is null)
        {
            return items;
        }

        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                items.Add(value);
            }
        }

        return items;
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment(ConsoleSessionSection? session)
    {
        var environment = new Dictionary<string, string>();
        if (session is null)
        {
            return environment;
        }

        foreach (var entry in session.Environment)
        {
            if (entry.Value is string value)
            {
                environment[entry.Key] = value;
            }
        }

        return environment;
    }

    // A runner with no extensions or no command can never be selected, so it is dropped rather than
    // failing the document.
    private static IReadOnlyList<ConsoleRunner> ReadRunners(ConsoleSessionSection? session)
    {
        var runners = new List<ConsoleRunner>();
        if (session is null)
        {
            return runners;
        }

        foreach (var entry in session.Runner)
        {
            var extensions = ReadTextList(entry.Extensions);
            var command = ReadText(entry.Command);
            if (extensions.Count > 0 &&
                command.Length > 0)
            {
                runners.Add(new ConsoleRunner(extensions, command));
            }
        }

        return runners;
    }

    private static IReadOnlyList<ConsoleDocumentTrigger> ReadTriggers(ConsoleSessionSection? session)
    {
        var triggers = new List<ConsoleDocumentTrigger>();
        if (session is null)
        {
            return triggers;
        }

        foreach (var entry in session.Trigger)
        {
            var pattern = ReadText(entry.Pattern);
            var command = ReadText(entry.Command);
            if (pattern.Length > 0 &&
                command.Length > 0)
            {
                triggers.Add(new ConsoleDocumentTrigger(pattern, command));
            }
        }

        return triggers;
    }

    // Every key the document declares that the host does not define, named by its section. The names
    // under [session.environment] are the user's own, so they never appear here.
    private static IReadOnlyList<string> CollectUnknownFields(ConsoleFile document)
    {
        var unknownFields = new List<string>();

        unknownFields.AddRange(document.UnknownKeys.Keys);

        var session = document.Session;
        if (session is null)
        {
            return unknownFields.AsReadOnly();
        }

        AddUnknownKeys(session.UnknownKeys, SessionSection, unknownFields);
        AddUnknownKeys(session.Options?.UnknownKeys, OptionsSection, unknownFields);

        foreach (var entry in session.Runner)
        {
            AddUnknownKeys(entry.UnknownKeys, RunnerSection, unknownFields);
        }

        foreach (var entry in session.Trigger)
        {
            AddUnknownKeys(entry.UnknownKeys, TriggerSection, unknownFields);
        }

        foreach (var entry in session.Shortcut)
        {
            AddUnknownKeys(entry.UnknownKeys, ShortcutSection, unknownFields);
        }

        return unknownFields.AsReadOnly();
    }

    private static void AddUnknownKeys(
        Dictionary<string, object?>? unknownKeys,
        string sectionName,
        List<string> unknownFields)
    {
        if (unknownKeys is null)
        {
            return;
        }

        foreach (var key in unknownKeys.Keys)
        {
            unknownFields.Add($"{sectionName}.{key}");
        }
    }
}
