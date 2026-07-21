using Celbridge.Packages;

namespace Celbridge.Projects.Services;

/// <summary>
/// Reconciles a parsed .celbridge against the packages discovered on disk. Required and recommended
/// contributions are live by default; a disabled marker turns a recommended one off, an enabled marker
/// turns an optional one on, and a marker naming a required contribution is dropped with a warning.
/// Override entries whose contribution is gone are dropped,
/// config keys are validated against their descriptors (unknown dropped, default-valued dropped), and
/// the result is two views: the resolved active set (discovery order, for the runtime) and the
/// normalized overrides (sorted, overrides only, for persistence). The always-active built-in
/// packages are host furniture served separately, so they never appear as overrides or active
/// contributions here. The pass is idempotent: reconciling its own output returns the same config.
/// </summary>
public static class ProjectConfigReconciler
{
    public static ProjectConfigReconcileResult Reconcile(
        ProjectConfig parsedConfig,
        IReadOnlyList<EditorContribution> discoveredContributions)
    {
        var warnings = new List<string>();

        var discoveredPackageNames = discoveredContributions
            .Select(contribution => contribution.Package.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Keep only disable markers for packages still present; a package that is gone is no longer
        // something to keep disabled. An always-active built-in cannot be disabled, so drop it with a
        // warning rather than persisting an entry that has no effect.
        var disabledPackages = new List<string>();
        foreach (var packageName in parsedConfig.Celbridge.DisabledPackages
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            if (BuiltInEditors.IsAlwaysActivePackage(packageName))
            {
                warnings.Add(
                    $"'{packageName}' was dropped from disabled-packages: built-in packages are always active and cannot be disabled.");
                continue;
            }

            if (!discoveredPackageNames.Contains(packageName))
            {
                continue;
            }

            disabledPackages.Add(packageName);
        }
        var disabledPackageSet = disabledPackages.ToHashSet(StringComparer.Ordinal);

        // Contributions available for override and activation: everything discovered whose package is
        // neither disabled nor an always-active built-in package (built-ins are served separately).
        var contributionsByRef = new Dictionary<(string Package, string Contribution), EditorContribution>();
        foreach (var contribution in discoveredContributions)
        {
            if (disabledPackageSet.Contains(contribution.Package.Name)
                || BuiltInEditors.IsAlwaysActivePackage(contribution.Package.Name))
            {
                continue;
            }

            contributionsByRef[(contribution.Package.Name, contribution.Id)] = contribution;
        }

        // Index the parsed overrides, dropping ones that no longer apply.
        var overridesByRef = new Dictionary<(string Package, string Contribution), ContributionOverride>();
        foreach (var contributionOverride in parsedConfig.ContributionOverrides)
        {
            var reference = (contributionOverride.PackageName, contributionOverride.ContributionId);
            var displayRef = $"{contributionOverride.PackageName}/{contributionOverride.ContributionId}";

            if (disabledPackageSet.Contains(contributionOverride.PackageName))
            {
                // The whole package is off, so its overrides are moot; the package disable records
                // the intent.
                continue;
            }

            if (BuiltInEditors.IsAlwaysActivePackage(contributionOverride.PackageName))
            {
                warnings.Add(
                    $"Override for '{displayRef}' was dropped: built-in editors are always active and not configurable.");
                continue;
            }

            if (!contributionsByRef.ContainsKey(reference))
            {
                warnings.Add(
                    $"Override for '{displayRef}' was dropped: package '{contributionOverride.PackageName}' has no contribution '{contributionOverride.ContributionId}'.");
                continue;
            }

            if (!overridesByRef.TryAdd(reference, contributionOverride))
            {
                warnings.Add($"Duplicate override for '{displayRef}' was dropped.");
            }
        }

        // Walk the discovered contributions in discovery order, resolving each one's active state and
        // validated config. The active set follows discovery order (editor precedence); the normalized
        // overrides are sorted by the serializer.
        var activeContributions = new List<ResolvedContribution>();
        var normalizedOverrides = new List<ContributionOverride>();

        foreach (var contribution in discoveredContributions)
        {
            var packageName = contribution.Package.Name;
            var contributionId = contribution.Id;

            if (disabledPackageSet.Contains(packageName)
                || BuiltInEditors.IsAlwaysActivePackage(packageName))
            {
                continue;
            }

            overridesByRef.TryGetValue((packageName, contributionId), out var contributionOverride);
            var overrideRef = $"{packageName}/{contributionId}";

            bool disabledFlag = false;
            bool enabledFlag = false;
            bool active;
            switch (contribution.Activation)
            {
                case ActivationPolicy.Required:
                    // Always active; a required contribution has no per-project off switch, so any
                    // activation marker on it is dropped.
                    active = true;
                    if (contributionOverride is not null
                        && (contributionOverride.Disabled || contributionOverride.Enabled))
                    {
                        warnings.Add(
                            $"Override for '{overrideRef}' dropped its activation marker: a required contribution cannot be enabled or disabled.");
                    }
                    break;

                case ActivationPolicy.Recommended:
                    // Live by default; a disable marker turns it off, an enable marker restates the default.
                    disabledFlag = contributionOverride?.Disabled ?? false;
                    if (contributionOverride?.Enabled == true)
                    {
                        warnings.Add(
                            $"Override for '{overrideRef}' dropped a redundant enable marker: it is active by default.");
                    }
                    active = !disabledFlag;
                    break;

                default:
                    // Optional: inert until an enable marker turns it on; a disable marker restates the default.
                    enabledFlag = contributionOverride?.Enabled ?? false;
                    if (contributionOverride?.Disabled == true)
                    {
                        warnings.Add(
                            $"Override for '{overrideRef}' dropped a redundant disable marker: it is inactive by default.");
                    }
                    active = enabledFlag;
                    break;
            }

            var validatedConfig = ValidateConfig(contribution, contributionOverride, warnings);

            if (active)
            {
                activeContributions.Add(new ResolvedContribution(contribution, validatedConfig));
            }

            var hasOverride = disabledFlag || enabledFlag || validatedConfig.Count > 0;
            if (hasOverride)
            {
                normalizedOverrides.Add(new ContributionOverride
                {
                    PackageName = packageName,
                    ContributionId = contributionId,
                    Disabled = disabledFlag,
                    Enabled = enabledFlag,
                    Config = validatedConfig,
                });
            }
        }

        var normalizedConfig = parsedConfig with
        {
            Celbridge = parsedConfig.Celbridge with { DisabledPackages = disabledPackages },
            ContributionOverrides = normalizedOverrides,
            EntryErrors = Array.Empty<ProjectConfigEntryError>(),
        };

        return new ProjectConfigReconcileResult(normalizedConfig, activeContributions, warnings);
    }

    // Rebuilds an override's config to hold only the keys that are valid and differ from their
    // descriptor default. Unknown keys, keys that fail type-checking, and keys equal to the default
    // are dropped, each with a warning.
    private static IReadOnlyDictionary<string, object?> ValidateConfig(
        EditorContribution contribution,
        ContributionOverride? contributionOverride,
        List<string> warnings)
    {
        var config = new Dictionary<string, object?>();
        if (contributionOverride is null)
        {
            return config;
        }

        var displayRef = $"{contributionOverride.PackageName}/{contributionOverride.ContributionId}";

        foreach (var (key, rawValue) in contributionOverride.Config)
        {
            var descriptor = contribution.ConfigDescriptors
                .FirstOrDefault(candidate => candidate.Key.Equals(key, StringComparison.Ordinal));
            if (descriptor is null)
            {
                warnings.Add($"'{displayRef}' dropped unknown config key '{key}'.");
                continue;
            }

            var encodeResult = ConfigValueEncoder.Encode(rawValue, descriptor);
            if (encodeResult.IsFailure)
            {
                warnings.Add($"'{displayRef}' dropped config key '{key}': {encodeResult.FirstErrorMessage}");
                continue;
            }

            // A value equal to the descriptor default is redundant, so it is not persisted.
            if (string.Equals(encodeResult.Value, descriptor.DefaultValue, StringComparison.Ordinal))
            {
                continue;
            }

            config[key] = rawValue;
        }

        return config;
    }
}
