using System.Text;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Rewrites "project:" reference literals across the project tree when a
/// resource is renamed or moved. Reads and writes go back through IFileStorage
/// so referencer files inherit the chokepoint's atomic-write semantics.
/// </summary>
internal sealed class ReferenceRewriter
{
    private readonly ILogger _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileStorage _fileStorage;

    public ReferenceRewriter(ILogger logger, IWorkspaceWrapper workspaceWrapper, IFileStorage fileStorage)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Rewrites every "project:<source>" literal (and, for folders, every
    /// "project:<source>/<rest>" literal) in every referencer of source.
    /// Successful rewrites land in updatedReferencers; failures land in
    /// skippedReferencers with a reason. The parent move always proceeds —
    /// data_check_project surfaces residuals; the chokepoint is idempotent so
    /// a rerun completes them.
    /// </summary>
    public async Task<Result> RewriteForMoveAsync(
        ResourceKey source,
        ResourceKey dest,
        bool sourceIsFolder,
        List<ResourceKey> updatedReferencers,
        List<SkippedReferencer> skippedReferencers)
    {
        var scanner = _workspaceWrapper.WorkspaceService.ResourceScanner;

        var referencerSet = new HashSet<ResourceKey>();
        foreach (var referencer in await scanner.FindReferencersAsync(source))
        {
            referencerSet.Add(referencer);
        }

        if (sourceIsFolder)
        {
            // Folder rename also rewrites every "project:<source>/<child>" form,
            // so gather referencers of every descendant target too.
            foreach (var target in await scanner.FindAllReferencedTargetsAsync())
            {
                if (target.IsDescendantOf(source))
                {
                    foreach (var referencer in await scanner.FindReferencersAsync(target))
                    {
                        referencerSet.Add(referencer);
                    }
                }
            }
        }

        var orderedReferencers = referencerSet
            .OrderBy(r => r.ToString(), StringComparer.Ordinal)
            .ToList();

        foreach (var referencer in orderedReferencers)
        {
            var readResult = await _fileStorage.ReadAllTextAsync(referencer);
            if (readResult.IsFailure)
            {
                var message = $"read failed for '{referencer}'";
                _logger.LogWarning($"Could not rewrite references in '{referencer}' for rename of '{source}' to '{dest}': {message}. The reference is left as-is and will surface via data_check_project.");
                skippedReferencers.Add(new SkippedReferencer(referencer, ReferencerSkipReason.ReadFailed, message));
                continue;
            }
            var originalText = readResult.Value;

            var rewritten = RewriteReferenceLiterals(originalText, source, dest, sourceIsFolder);
            if (rewritten == originalText)
            {
                continue;
            }

            // Pre-check the DOS read-only attribute. Windows surfaces it as a
            // write failure but POSIX rename would silently succeed, so the
            // pre-check honors "don't modify this file" identically on both.
            if (IsReferencerReadOnly(referencer))
            {
                const string readOnlyMessage = "file is read-only";
                _logger.LogWarning($"Could not rewrite references in '{referencer}' for rename of '{source}' to '{dest}': {readOnlyMessage}. The reference is left as-is and will surface via data_check_project.");
                skippedReferencers.Add(new SkippedReferencer(referencer, ReferencerSkipReason.ReadOnly, readOnlyMessage));
                continue;
            }

            var writeResult = await _fileStorage.WriteAllTextAsync(referencer, rewritten);
            if (writeResult.IsFailure)
            {
                var classification = ClassifyReferencerWriteFailure(referencer, writeResult);
                _logger.LogWarning($"Could not rewrite references in '{referencer}' for rename of '{source}' to '{dest}': {classification.Message}. The reference is left as-is and will surface via data_check_project.");
                skippedReferencers.Add(new SkippedReferencer(referencer, classification.Reason, classification.Message));
                continue;
            }

            updatedReferencers.Add(referencer);
        }

        return Result.Ok();
    }

    private bool IsReferencerReadOnly(ResourceKey referencer)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = registry.ResolveResourcePath(referencer);
        if (resolveResult.IsFailure)
        {
            return false;
        }

        try
        {
            var info = new FileInfo(resolveResult.Value);
            return info.Exists
                && info.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private (ReferencerSkipReason Reason, string Message) ClassifyReferencerWriteFailure(ResourceKey referencer, Result writeResult)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = registry.ResolveResourcePath(referencer);
        if (resolveResult.IsFailure)
        {
            return (ReferencerSkipReason.WriteFailed, "write failed (could not resolve path)");
        }

        // ReadOnly is split from PermissionDenied because the fix is different:
        // read-only is trivially clearable; an ACL deny needs the right account.
        try
        {
            var info = new FileInfo(resolveResult.Value);
            if (info.Exists
                && info.IsReadOnly)
            {
                return (ReferencerSkipReason.ReadOnly, "file is read-only");
            }
        }
        catch
        {
        }

        if (writeResult.FirstException is UnauthorizedAccessException)
        {
            return (ReferencerSkipReason.PermissionDenied, "permission denied (no write access to file)");
        }

        return (ReferencerSkipReason.WriteFailed, "write failed (file may be locked or another IO issue)");
    }

    // Replaces every reference whose parsed key matches sourceKey, or
    // (for folders) begins with sourceKey/, with the equivalent literal
    // targeting destKey. Detection and rewrite both go through
    // ResourceReferenceParser.TryParseReferenceAt so the parse contract cannot
    // drift between them.
    private static string RewriteReferenceLiterals(string text, ResourceKey sourceKey, ResourceKey destKey, bool sourceIsFolder)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var sourceKeyString = sourceKey.FullKey;
        var destKeyString = destKey.FullKey;
        var sourceFolderPrefix = sourceKeyString + "/";

        var builder = new StringBuilder(text.Length);
        int cursor = 0;

        while (cursor < text.Length)
        {
            int markerIndex = text.IndexOf(ResourceReferenceParser.ReferenceMarker, cursor, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                builder.Append(text, cursor, text.Length - cursor);
                break;
            }

            var parsed = ResourceReferenceParser.TryParseReferenceAt(text, markerIndex);
            if (parsed is null)
            {
                // Not a tracked reference. Advance one char so a later
                // overlapping match can still hit.
                builder.Append(text, cursor, markerIndex - cursor + 1);
                cursor = markerIndex + 1;
                continue;
            }

            var parsedKeyString = parsed.Key.FullKey;
            string? rewrittenKeyString = null;

            if (string.Equals(parsedKeyString, sourceKeyString, StringComparison.Ordinal))
            {
                rewrittenKeyString = destKeyString;
            }
            else if (sourceIsFolder
                && parsedKeyString.StartsWith(sourceFolderPrefix, StringComparison.Ordinal))
            {
                rewrittenKeyString = destKeyString + parsedKeyString.Substring(sourceKeyString.Length);
            }

            // Emit everything up to (but not including) the marker.
            builder.Append(text, cursor, markerIndex - cursor);

            if (rewrittenKeyString is not null)
            {
                builder.Append(rewrittenKeyString);
            }
            else
            {
                // The parsed key didn't match; preserve the original literal.
                builder.Append(text, markerIndex, parsed.EndIndex - markerIndex);
            }

            cursor = parsed.EndIndex;
        }

        return builder.ToString();
    }
}
