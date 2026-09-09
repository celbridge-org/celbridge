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
/// The launch-relevant configuration parsed from a .console file. SessionTypeOptions is the selected type's
/// [session.&lt;type&gt;] table: a launch only ever runs one type, and preserving the table of a type left
/// behind by a switch is the settings form's job, since it is what writes the file. StartupScript is read
/// out of that same table because the host injects it for every type. Shortcuts are not represented: they
/// are a client-side toolbar the host never consumes.
/// </summary>
public sealed record ConsoleDocumentConfig(
    string SessionType,
    string WorkingDirectory,
    string StartupScript,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, object?> SessionTypeOptions,
    IReadOnlyList<ConsoleRunner> Runners,
    IReadOnlyList<string> DisabledBuiltInRunners,
    IReadOnlyList<ConsoleDocumentTrigger> Triggers)
{
    /// <summary>
    /// Keys the document declared that the host does not define, each named by its section (for example
    /// "session.shell.entrypoint"). The document still launches, so this is advisory.
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
    private const string RunnerSection = "session.runner";
    private const string TriggerSection = "session.trigger";
    private const string ShortcutSection = "session.shortcut";

    private const string DefaultSessionType = "shell";

    /// <summary>
    /// The startup script key, accepted in every type's table. The host splits it into lines and injects
    /// them, so it is defined here rather than by each provider.
    /// </summary>
    public const string ScriptKey = "script";

    // Document keys are the snake_case spelling of the model's property names.
    private static readonly TomlSerializerOptions DocumentOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Parses a .console document. The registered session types are supplied by the caller, so the parser
    /// reports a key a type does not define without knowing what any type's keys mean. A table named for a
    /// type that is not registered is reported the same way.
    /// </summary>
    public static Result<ConsoleDocumentConfig> Parse(
        string tomlText,
        IReadOnlyList<ConsoleSessionType> sessionTypes)
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
        var sessionType = ReadText(session?.Type, DefaultSessionType);
        // A duplicate id is a programming error in a provider, caught as the session service is built. It is
        // reported rather than thrown here, so every way this method can fail reaches the caller alike.
        var optionKeysBySessionType = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var type in sessionTypes)
        {
            if (!optionKeysBySessionType.TryAdd(type.TypeId, type.OptionKeys))
            {
                return Result<ConsoleDocumentConfig>.Fail(
                    $"Console session type '{type.TypeId}' is registered more than once.");
            }
        }
        var sessionTypeOptions = ReadSessionTypeOptions(session, sessionType, optionKeysBySessionType);

        var config = new ConsoleDocumentConfig(
            sessionType,
            ReadText(session?.WorkingDirectory),
            ConfigTableHelper.ReadText(sessionTypeOptions, ScriptKey),
            ReadEnvironment(session),
            sessionTypeOptions,
            ReadRunners(session),
            ReadTextList(session?.DisabledRunners),
            ReadTriggers(session))
        {
            UnknownFields = CollectUnknownFields(document, optionKeysBySessionType)
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

    // The selected type's [session.<type>] table. The tables of every other type the document declares are
    // read only to report their unknown keys: a launch runs one type, and carrying the rest across a save is
    // the settings form's job. A table for a type that is not registered reads as absent, because the
    // session fails on the unknown type before its options could matter.
    private static IReadOnlyDictionary<string, object?> ReadSessionTypeOptions(
        ConsoleSessionSection? session,
        string sessionType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> optionKeysBySessionType)
    {
        var noOptions = new Dictionary<string, object?>();
        if (session is null ||
            !optionKeysBySessionType.ContainsKey(sessionType) ||
            !session.ExtensionKeys.TryGetValue(sessionType, out var value))
        {
            return noOptions;
        }

        return ConfigTableHelper.ReadTable(value) ?? noOptions;
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
    private static IReadOnlyList<string> CollectUnknownFields(
        ConsoleFile document,
        IReadOnlyDictionary<string, IReadOnlyList<string>> optionKeysBySessionType)
    {
        var unknownFields = new List<string>();

        unknownFields.AddRange(document.ExtensionKeys.Keys);

        var session = document.Session;
        if (session is null)
        {
            return unknownFields.AsReadOnly();
        }

        AddUnknownSessionKeys(session.ExtensionKeys, optionKeysBySessionType, unknownFields);

        foreach (var entry in session.Runner)
        {
            AddUnknownKeys(entry.ExtensionKeys, RunnerSection, unknownFields);
        }

        foreach (var entry in session.Trigger)
        {
            AddUnknownKeys(entry.ExtensionKeys, TriggerSection, unknownFields);
        }

        foreach (var entry in session.Shortcut)
        {
            AddUnknownKeys(entry.ExtensionKeys, ShortcutSection, unknownFields);
        }

        return unknownFields.AsReadOnly();
    }

    // The session bag holds each type's own table alongside anything the document declares that nothing
    // defines. A table named for a registered type has its keys checked against that type. Every other
    // entry is unknown, which covers both a stray key and a table named for a type that is not registered.
    private static void AddUnknownSessionKeys(
        Dictionary<string, object?> extensionKeys,
        IReadOnlyDictionary<string, IReadOnlyList<string>> optionKeysBySessionType,
        List<string> unknownFields)
    {
        foreach (var entry in extensionKeys)
        {
            if (!optionKeysBySessionType.TryGetValue(entry.Key, out var optionKeys))
            {
                unknownFields.Add($"{SessionSection}.{entry.Key}");
                continue;
            }

            var table = ConfigTableHelper.ReadTable(entry.Value);
            if (table is null)
            {
                unknownFields.Add($"{SessionSection}.{entry.Key}");
                continue;
            }

            var knownKeys = new HashSet<string>(optionKeys, StringComparer.Ordinal)
            {
                ScriptKey
            };

            foreach (var unknownKey in ConfigSchemaHelper.FindUnknownKeys(table.Keys, knownKeys))
            {
                unknownFields.Add($"{SessionSection}.{entry.Key}.{unknownKey}");
            }
        }
    }

    private static void AddUnknownKeys(
        Dictionary<string, object?>? extensionKeys,
        string sectionName,
        List<string> unknownFields)
    {
        if (extensionKeys is null)
        {
            return;
        }

        foreach (var key in extensionKeys.Keys)
        {
            unknownFields.Add($"{sectionName}.{key}");
        }
    }
}
