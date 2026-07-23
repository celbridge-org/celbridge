namespace Celbridge.Projects;

/// <summary>
/// A typed configuration value carried by a project config edit. The five variants mirror the
/// closed config descriptor type vocabulary, and the writer maps each to its TOML value form.
/// </summary>
public abstract record ConfigEditValue;

/// <summary>
/// A boolean config value, written as a TOML true or false literal.
/// </summary>
public sealed record BoolEditValue(bool Value) : ConfigEditValue;

/// <summary>
/// A string config value (also used for enum selections), written as a TOML basic string.
/// </summary>
public sealed record StringEditValue(string Value) : ConfigEditValue;

/// <summary>
/// An integer config value, written as a TOML integer literal.
/// </summary>
public sealed record IntegerEditValue(long Value) : ConfigEditValue;

/// <summary>
/// A floating-point config value, written as a TOML float literal.
/// </summary>
public sealed record FloatEditValue(double Value) : ConfigEditValue;

/// <summary>
/// A string-list config value, written as an inline TOML array of basic strings.
/// </summary>
public sealed record StringListEditValue(IReadOnlyList<string> Values) : ConfigEditValue;

/// <summary>
/// A single targeted edit to the .celbridge project config, applied by parsing the file into its
/// model, mutating it, and serializing back. Each variant corresponds to one Project Settings action
/// or agent tool call. Edits target a contribution by its (package, contribution) identity; the file
/// is normalized on the next load, so an edit only needs to record intent.
/// </summary>
public abstract record ProjectConfigEdit;

/// <summary>
/// Turns a package off (adds it to [celbridge].disabled-packages) or back on (removes it). A disabled
/// package contributes nothing; activation is otherwise discovery-driven.
/// </summary>
public sealed record SetPackageDisabledEdit(string PackageName, bool Disabled) : ProjectConfigEdit;

/// <summary>
/// Turns a default-active contribution off (writes disabled = true on its entry) or back on (clears
/// it). Used to disable a discovered default without losing its config.
/// </summary>
public sealed record SetContributionDisabledEdit(string PackageName, string ContributionId, bool Disabled) : ProjectConfigEdit;

/// <summary>
/// Turns an optional contribution on (writes enabled = true on its entry) or back off (clears it).
/// </summary>
public sealed record SetContributionEnabledEdit(string PackageName, string ContributionId, bool Enabled) : ProjectConfigEdit;

/// <summary>
/// Sets a config key on a contribution's entry to a typed value, creating the entry if none exists.
/// </summary>
public sealed record SetContributionValueEdit(
    string PackageName,
    string ContributionId,
    string PropertyKey,
    ConfigEditValue Value) : ProjectConfigEdit;

/// <summary>
/// Removes a config key from a contribution's entry, reverting it to its descriptor default. A no-op
/// when the key or the entry is absent.
/// </summary>
public sealed record RemoveContributionValueEdit(
    string PackageName,
    string ContributionId,
    string PropertyKey) : ProjectConfigEdit;

/// <summary>
/// Sets the project's own version ([celbridge].project-version).
/// </summary>
public sealed record SetProjectVersionEdit(string ProjectVersion) : ProjectConfigEdit;

/// <summary>
/// Sets the project's human-readable description ([celbridge].description).
/// </summary>
public sealed record SetDescriptionEdit(string Description) : ProjectConfigEdit;

/// <summary>
/// Sets the resource ignore-file path ([celbridge.resources].ignore-file).
/// </summary>
public sealed record SetIgnoreFileEdit(string IgnoreFile) : ProjectConfigEdit;

/// <summary>
/// Associates a file extension with an editor id in [celbridge].editor-associations, replacing any
/// existing entry for that extension.
/// </summary>
public sealed record SetEditorAssociationEdit(string Extension, string EditorId) : ProjectConfigEdit;

/// <summary>
/// Removes a file extension's entry from [celbridge].editor-associations, dropping the map entirely
/// once it is empty. A no-op when the extension has no entry.
/// </summary>
public sealed record RemoveEditorAssociationEdit(string Extension) : ProjectConfigEdit;
