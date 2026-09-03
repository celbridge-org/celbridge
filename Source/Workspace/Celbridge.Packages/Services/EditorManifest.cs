using Tomlyn.Serialization;

namespace Celbridge.Packages;

/// <summary>
/// The [editor] section of an editor manifest, naming the contribution and how it activates. Its
/// display-name and description hold localization keys.
/// </summary>
internal sealed partial record ManifestEditorSection
{
    public string? Id { get; init; }

    // "document" for an editor that claims file types, "utility" for a workspace fixture.
    public string? Type { get; init; }

    public string? DisplayName { get; init; }
    public string? Description { get; init; }

    // Package-relative HTML file the editor's WebView loads.
    public string? EntryPoint { get; init; }

    // Content reaches the editor as base64 rather than text.
    public bool? Binary { get; init; }

    // Content is sourced from outside the file bytes, which forbids templates.
    public bool? ExternalContent { get; init; }

    // "required", "recommended" or "optional", naming how much say a project has over the contribution
    // being live.
    public string? Activation { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// One [[file-types]] entry, claiming a file extension for a document editor and describing how files
/// of that type are drawn. Its display-name holds a localization key.
/// </summary>
internal sealed record ManifestFileTypeEntry
{
    public string? Extension { get; init; }

    // Claims a set of extensions from the host file type catalog instead of naming one, so it is
    // mutually exclusive with an extension.
    public string? FromCatalog { get; init; }

    public string? DisplayName { get; init; }

    // Prefixed icon name, "<font>-<name>". The prefix selects the icon font.
    public string? Icon { get; init; }

    // Requires an icon. The host normalises the colour into a legible band for the active theme, so a
    // declared value is adjusted rather than shown exactly.
    public string? IconColor { get; init; }

    // Requires an icon. Enlarges a glyph its font draws small.
    public double? IconScale { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// One [[templates]] entry, naming a starter file the New File dialog can create. Its display-name
/// holds a localization key.
/// </summary>
internal sealed partial record ManifestTemplateEntry
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? TemplateFile { get; init; }
    public bool Default { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// One [[config]] entry, declaring a typed configuration key a project can set on the contribution. Its
/// display-name and description hold localization keys.
/// </summary>
internal sealed partial record ManifestConfigEntry
{
    public string? Key { get; init; }

    // "bool", "string", "number", "enum" or "string-list", naming the values the key accepts.
    public string? Type { get; init; }

    // Null when the key is absent, which is what separates an omitted list from an empty one.
    public List<string>? Values { get; init; }

    public string? DisplayName { get; init; }
    public string? Description { get; init; }

    // The TOML type of a default is chosen by the descriptor's own type key, so the model cannot name
    // it. The value is checked against that declared type when the descriptor is built.
    public object? Default { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The [utility] section of a utility manifest, describing the utility's backing state file and where
/// it docks.
/// </summary>
internal sealed record ManifestUtilitySection
{
    // Extension of the state file, whose full path the host derives as
    // utils:{package}.{contribution}{resource-extension}.
    public string? ResourceExtension { get; init; }

    // Prefixed icon name, "<font>-<name>", for the rail button and the docked tab.
    public string? Icon { get; init; }

    // Package-relative path to a file that seeds the state file when it is absent. An omitted template
    // seeds an empty file.
    public string? Template { get; init; }

    // The document area "Open as document" sends the utility to: "main", "bottom", "side", or "none"
    // for a utility that stays in the Utility Panel.
    public string? DockArea { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The shape of an editor manifest (*.editor.toml), deserialized by Tomlyn. The property names are the
/// manifest's known keys: Tomlyn maps them to their kebab-case spelling and routes every other key into
/// an UnknownKeys bag, which is what the loader reports as an unknown field. The keys under [options] are
/// the editor's own, so they are held as free-form data rather than checked.
/// </summary>
internal sealed record EditorManifest
{
    public ManifestEditorSection? Editor { get; init; }
    public List<ManifestFileTypeEntry> FileTypes { get; init; } = new();
    public List<ManifestTemplateEntry> Templates { get; init; } = new();
    public List<ManifestConfigEntry> Config { get; init; } = new();
    public ManifestUtilitySection? Utility { get; init; }
    public Dictionary<string, object?> Options { get; init; } = new();

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}
