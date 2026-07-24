using Celbridge.Utilities;

namespace Celbridge.Projects.Services;

/// <summary>
/// Applies a batch of project config edits by parsing the current .celbridge into its model, mutating
/// the [[contribution]] override entries and host-level keys, and serializing it back with
/// ProjectConfigSerializer. Because the file is normalized on every load, edits do not need to
/// preserve formatting; they only need to produce a file that reconciles to the intended state.
/// </summary>
public static class ProjectConfigModifier
{
    public static Result<string> ApplyEdits(string tomlText, IReadOnlyList<ProjectConfigEdit> edits)
    {
        var parseResult = ProjectConfigParser.ParseFromText(tomlText ?? string.Empty);
        if (parseResult.IsFailure)
        {
            return Result<string>.Fail("Cannot edit a project config that does not parse")
                .WithErrors(parseResult);
        }
        var config = parseResult.Value;

        var overrides = config.ContributionOverrides.ToList();
        var disabledPackages = config.Celbridge.DisabledPackages.ToList();
        var editorAssociations = new Dictionary<string, string>(config.Celbridge.EditorAssociations, StringComparer.Ordinal);
        var features = new Dictionary<string, bool>(config.Features, StringComparer.Ordinal);
        var projectVersion = config.Celbridge.ProjectVersion;
        var description = config.Celbridge.Description;
        var ignoreFile = config.Resources.IgnoreFile;

        foreach (var edit in edits)
        {
            // The host-level scalar edits are applied here; every other edit mutates the
            // collections passed to ApplyEdit.
            if (edit is SetProjectVersionEdit setProjectVersion)
            {
                projectVersion = setProjectVersion.ProjectVersion;
                continue;
            }
            if (edit is SetDescriptionEdit setDescription)
            {
                description = setDescription.Description;
                continue;
            }
            if (edit is SetIgnoreFileEdit setIgnoreFile)
            {
                ignoreFile = setIgnoreFile.IgnoreFile;
                continue;
            }

            var applyResult = ApplyEdit(edit, overrides, disabledPackages, editorAssociations, features);
            if (applyResult.IsFailure)
            {
                return Result<string>.Fail("Failed to apply a project config edit")
                    .WithErrors(applyResult);
            }
        }

        // Drop entries that no longer carry any override, so an edit that clears the last flag or
        // config value leaves no empty entry behind.
        overrides.RemoveAll(contributionOverride => !contributionOverride.Disabled && !contributionOverride.Enabled && contributionOverride.Config.Count == 0);

        var updatedConfig = config with
        {
            Celbridge = config.Celbridge with
            {
                DisabledPackages = disabledPackages,
                EditorAssociations = editorAssociations,
                ProjectVersion = projectVersion,
                Description = description,
            },
            Resources = config.Resources with { IgnoreFile = ignoreFile },
            Features = features,
            ContributionOverrides = overrides,
        };

        return Result<string>.Ok(ProjectConfigSerializer.Serialize(updatedConfig));
    }

    private static Result ApplyEdit(
        ProjectConfigEdit edit,
        List<ContributionOverride> overrides,
        List<string> disabledPackages,
        Dictionary<string, string> editorAssociations,
        Dictionary<string, bool> features)
    {
        switch (edit)
        {
            case SetFeatureFlagEdit setFeatureFlag:
                features[setFeatureFlag.FlagName] = setFeatureFlag.Enabled;
                return Result.Ok();

            case RemoveFeatureFlagEdit removeFeatureFlag:
                features.Remove(removeFeatureFlag.FlagName);
                return Result.Ok();

            case SetPackageDisabledEdit setPackageDisabled:
                if (setPackageDisabled.Disabled)
                {
                    if (!disabledPackages.Contains(setPackageDisabled.PackageName, StringComparer.Ordinal))
                    {
                        disabledPackages.Add(setPackageDisabled.PackageName);
                    }
                }
                else
                {
                    disabledPackages.RemoveAll(name => string.Equals(name, setPackageDisabled.PackageName, StringComparison.Ordinal));
                }
                return Result.Ok();

            case SetContributionDisabledEdit setDisabled:
                UpdateOverride(overrides, setDisabled.PackageName, setDisabled.ContributionId,
                    contributionOverride => contributionOverride with { Disabled = setDisabled.Disabled });
                return Result.Ok();

            case SetContributionEnabledEdit setEnabled:
                UpdateOverride(overrides, setEnabled.PackageName, setEnabled.ContributionId,
                    contributionOverride => contributionOverride with { Enabled = setEnabled.Enabled });
                return Result.Ok();

            case SetContributionValueEdit setValue:
                UpdateOverride(overrides, setValue.PackageName, setValue.ContributionId,
                    contributionOverride =>
                    {
                        var config = new Dictionary<string, object?>(contributionOverride.Config) { [setValue.PropertyKey] = ToRawValue(setValue.Value) };
                        return contributionOverride with { Config = config };
                    });
                return Result.Ok();

            case RemoveContributionValueEdit removeValue:
                UpdateOverride(overrides, removeValue.PackageName, removeValue.ContributionId,
                    contributionOverride =>
                    {
                        if (!contributionOverride.Config.ContainsKey(removeValue.PropertyKey))
                        {
                            return contributionOverride;
                        }
                        var config = new Dictionary<string, object?>(contributionOverride.Config);
                        config.Remove(removeValue.PropertyKey);
                        return contributionOverride with { Config = config };
                    },
                    createIfMissing: false);
                return Result.Ok();

            case SetEditorAssociationEdit setAssociation:
                var associationExtension = setAssociation.Extension.ToLowerInvariant();
                if (!FileExtensionUtils.IsWellFormedFileExtension(associationExtension))
                {
                    return Result.Fail(
                        $"Editor association extension '{setAssociation.Extension}' must be a well-formed file extension (e.g. \".txt\").");
                }
                editorAssociations[associationExtension] = setAssociation.EditorId;
                return Result.Ok();

            case RemoveEditorAssociationEdit removeAssociation:
                editorAssociations.Remove(removeAssociation.Extension.ToLowerInvariant());
                return Result.Ok();

            default:
                return Result.Fail($"Unsupported project config edit: {edit.GetType().Name}");
        }
    }

    // Finds the override entry for a contribution and applies the update, creating an entry when none
    // exists (unless createIfMissing is false, for a remove that has nothing to act on).
    private static void UpdateOverride(
        List<ContributionOverride> overrides,
        string packageName,
        string contributionId,
        Func<ContributionOverride, ContributionOverride> update,
        bool createIfMissing = true)
    {
        var index = overrides.FindIndex(contributionOverride =>
            string.Equals(contributionOverride.PackageName, packageName, StringComparison.Ordinal)
            && string.Equals(contributionOverride.ContributionId, contributionId, StringComparison.Ordinal));
        if (index >= 0)
        {
            overrides[index] = update(overrides[index]);
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
        overrides.Add(update(created));
    }

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
}
