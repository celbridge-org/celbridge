using Celbridge.Utilities;

namespace Celbridge.Projects.Services;

/// <summary>
/// A mutable working copy of a project config. The Project Settings editor holds one for as long as it
/// is open, its sections mutate it as the user works, and the save tick serializes it back to the
/// .celbridge file. Because the file is normalized on every load, a draft does not preserve formatting;
/// it only has to produce a file that reconciles to the intended state.
/// </summary>
public sealed class ProjectConfigDraft
{
    private readonly ProjectConfig _source;

    private readonly List<ContributionOverride> _overrides;
    private readonly List<string> _disabledPackages;
    private readonly Dictionary<string, string> _editorAssociations;
    private readonly Dictionary<string, bool> _features;

    private string _projectVersion;
    private string _description;
    private string _ignoreFile;

    public ProjectConfigDraft(ProjectConfig source)
    {
        _source = source;

        _overrides = source.ContributionOverrides.ToList();
        _disabledPackages = source.Celbridge.DisabledPackages.ToList();
        _editorAssociations = new Dictionary<string, string>(source.Celbridge.EditorAssociations, StringComparer.Ordinal);
        _features = new Dictionary<string, bool>(source.Features, StringComparer.Ordinal);

        // Coerced to empty because an unset key parses as null while the editor binds a text box to it.
        // The serializer skips an empty value, so a field left alone still writes no key.
        _projectVersion = source.Celbridge.ProjectVersion ?? string.Empty;
        _description = source.Celbridge.Description ?? string.Empty;
        _ignoreFile = source.Resources.IgnoreFile;
    }

    public string ProjectVersion
    {
        get => _projectVersion;
        set => _projectVersion = value;
    }

    public string Description
    {
        get => _description;
        set => _description = value;
    }

    public string IgnoreFile
    {
        get => _ignoreFile;
        set => _ignoreFile = value;
    }

    /// <summary>
    /// Turns a package off (adding it to [celbridge].disabled-packages) or back on.
    /// </summary>
    public void SetPackageDisabled(string packageName, bool disabled)
    {
        if (disabled)
        {
            if (!_disabledPackages.Contains(packageName, StringComparer.Ordinal))
            {
                _disabledPackages.Add(packageName);
            }

            return;
        }

        _disabledPackages.RemoveAll(name => string.Equals(name, packageName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Turns a default-active contribution off, or clears the override that turned it off.
    /// </summary>
    public void SetContributionDisabled(string packageName, string contributionId, bool disabled)
    {
        UpdateOverride(packageName, contributionId,
            contributionOverride => contributionOverride with { Disabled = disabled });
    }

    /// <summary>
    /// Turns an optional contribution on, or clears the override that turned it on.
    /// </summary>
    public void SetContributionEnabled(string packageName, string contributionId, bool enabled)
    {
        UpdateOverride(packageName, contributionId,
            contributionOverride => contributionOverride with { Enabled = enabled });
    }

    /// <summary>
    /// Sets a config key on a contribution's entry, creating the entry if none exists.
    /// </summary>
    public void SetContributionValue(string packageName, string contributionId, string propertyKey, ConfigEditValue value)
    {
        var rawValue = ToRawValue(value);

        UpdateOverride(packageName, contributionId,
            contributionOverride =>
            {
                var config = new Dictionary<string, object?>(contributionOverride.Config)
                {
                    [propertyKey] = rawValue
                };

                return contributionOverride with { Config = config };
            });
    }

    /// <summary>
    /// Clears a config key from a contribution's entry, returning it to the manifest default.
    /// </summary>
    public void RemoveContributionValue(string packageName, string contributionId, string propertyKey)
    {
        UpdateOverride(packageName, contributionId,
            contributionOverride =>
            {
                if (!contributionOverride.Config.ContainsKey(propertyKey))
                {
                    return contributionOverride;
                }

                var config = new Dictionary<string, object?>(contributionOverride.Config);
                config.Remove(propertyKey);

                return contributionOverride with { Config = config };
            },
            createIfMissing: false);
    }

    /// <summary>
    /// Points a file extension at an editor. Fails when the extension is not well formed.
    /// </summary>
    public Result SetEditorAssociation(string extension, string editorId)
    {
        var normalizedExtension = extension.ToLowerInvariant();
        if (!FileExtensionUtils.IsWellFormedFileExtension(normalizedExtension))
        {
            return Result.Fail(
                $"Editor association extension '{extension}' must be a well-formed file extension (e.g. \".txt\").");
        }

        _editorAssociations[normalizedExtension] = editorId;

        return Result.Ok();
    }

    /// <summary>
    /// Clears a file extension's editor association, returning it to the resolution default.
    /// </summary>
    public void RemoveEditorAssociation(string extension)
    {
        _editorAssociations.Remove(extension.ToLowerInvariant());
    }

    /// <summary>
    /// Overrides a feature flag for this project.
    /// </summary>
    public void SetFeatureFlag(string flagName, bool enabled)
    {
        _features[flagName] = enabled;
    }

    /// <summary>
    /// Clears a feature flag override, returning the flag to its application-level value.
    /// </summary>
    public void RemoveFeatureFlag(string flagName)
    {
        _features.Remove(flagName);
    }

    /// <summary>
    /// The config this draft now describes.
    /// </summary>
    public ProjectConfig ToConfig()
    {
        // Entries carrying no override at all are dropped, so clearing the last flag or config value on a
        // contribution leaves no empty entry behind.
        var populatedOverrides = _overrides
            .Where(contributionOverride =>
                contributionOverride.Disabled
                || contributionOverride.Enabled
                || contributionOverride.Config.Count > 0)
            .ToList();

        return _source with
        {
            Celbridge = _source.Celbridge with
            {
                DisabledPackages = _disabledPackages.ToList(),
                EditorAssociations = new Dictionary<string, string>(_editorAssociations, StringComparer.Ordinal),
                ProjectVersion = _projectVersion,
                Description = _description,
            },
            Resources = _source.Resources with { IgnoreFile = _ignoreFile },
            Features = new Dictionary<string, bool>(_features, StringComparer.Ordinal),
            ContributionOverrides = populatedOverrides,
        };
    }

    /// <summary>
    /// The canonical .celbridge text for the config this draft describes.
    /// </summary>
    public string Serialize()
    {
        return ProjectConfigSerializer.Serialize(ToConfig());
    }

    // The serializer writes raw TOML values, so the typed value the editor supplies is unwrapped here.
    private static object? ToRawValue(ConfigEditValue value)
    {
        switch (value)
        {
            case BoolEditValue boolValue:
                return boolValue.Value;
            case StringEditValue stringValue:
                return stringValue.Value;
            case IntegerEditValue integerValue:
                return integerValue.Value;
            case FloatEditValue floatValue:
                return floatValue.Value;
            case StringListEditValue stringListValue:
                return stringListValue.Values.ToList();
            default:
                return null;
        }
    }

    // Finds the override entry for a contribution and applies the update, creating an entry when none
    // exists, unless createIfMissing is false for a remove that has nothing to act on.
    private void UpdateOverride(
        string packageName,
        string contributionId,
        Func<ContributionOverride, ContributionOverride> update,
        bool createIfMissing = true)
    {
        var index = _overrides.FindIndex(contributionOverride =>
            string.Equals(contributionOverride.PackageName, packageName, StringComparison.Ordinal)
            && string.Equals(contributionOverride.ContributionId, contributionId, StringComparison.Ordinal));

        if (index >= 0)
        {
            _overrides[index] = update(_overrides[index]);
            return;
        }

        if (!createIfMissing)
        {
            return;
        }

        var created = new ContributionOverride
        {
            PackageName = packageName,
            ContributionId = contributionId,
        };

        _overrides.Add(update(created));
    }
}
