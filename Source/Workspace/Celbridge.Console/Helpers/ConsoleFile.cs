using Tomlyn.Serialization;

namespace Celbridge.Console.Helpers;

/// <summary>
/// The [session.options] table, holding the settings that vary by session type.
/// </summary>
internal sealed record ConsoleOptionsSection
{
    // Shell sessions only. Typed into the shell as a command once it starts.
    public string? Executable { get; init; }

    // Python sessions only. Selects the interpreter uv provisions.
    public string? PythonVersion { get; init; }

    public List<string>? Arguments { get; init; }
    public List<string>? Dependencies { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// One [[session.runner]] entry, naming the command that runs a file the Explorer Run menu targets.
/// </summary>
internal sealed record ConsoleRunnerEntry
{
    // File extensions this runner claims. A runner is resolved by the first entry whose extensions
    // match, so the declared order is load-bearing.
    public List<string>? Extensions { get; init; }

    public string? Command { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// One [[session.trigger]] entry, naming the command injected when a matching resource changes.
/// </summary>
internal sealed record ConsoleTriggerEntry
{
    // Resource path pattern the trigger watches.
    public string? Pattern { get; init; }

    public string? Command { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// One [[session.shortcut]] entry. Shortcuts are a client-side toolbar the host never consumes, so this
/// is modelled to keep the keys known rather than to be read.
/// </summary>
internal sealed record ConsoleShortcutEntry
{
    public string? Label { get; init; }
    public string? Icon { get; init; }
    public string? Text { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The [session] table, describing the session a console document launches.
/// </summary>
internal sealed record ConsoleSessionSection
{
    // Names the session provider, defaulting to "shell" when absent.
    public string? Type { get; init; }

    public string? WorkingDirectory { get; init; }
    public string? StartupScript { get; init; }

    // Built-in runner ids the document opts out of.
    public List<string>? DisabledBuiltInRunners { get; init; }

    public ConsoleOptionsSection? Options { get; init; }

    // Environment variables passed to the session process. The names are the user's own, so they are
    // held as free-form data rather than checked.
    public Dictionary<string, object?> Environment { get; init; } = new();

    public List<ConsoleRunnerEntry> Runner { get; init; } = new();
    public List<ConsoleTriggerEntry> Trigger { get; init; } = new();
    public List<ConsoleShortcutEntry> Shortcut { get; init; } = new();

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The shape of a console document (.console), deserialized by Tomlyn. The property names are the
/// document's known keys under their snake_case spelling, and every other key lands in an UnknownKeys bag
/// rather than being dropped. The settings form writes the same file with its own parser
/// (console-toml.js), so the two must agree on this key set.
/// </summary>
internal sealed record ConsoleFile
{
    public ConsoleSessionSection? Session { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}
