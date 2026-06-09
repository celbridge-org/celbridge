using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class GetFileInfoCommand : CommandBase, IGetFileInfoCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ITextBinarySniffer _textBinarySniffer;

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }

    public FileInfoSnapshot ResultValue { get; private set; }
        = new FileInfoSnapshot(
            Exists: false,
            IsFile: false,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            Extension: string.Empty,
            IsText: false,
            LineCount: null,
            SidecarKey: null,
            SidecarStatus: null);

    public GetFileInfoCommand(
        IWorkspaceWrapper workspaceWrapper,
        ITextBinarySniffer textBinarySniffer)
    {
        _workspaceWrapper = workspaceWrapper;
        _textBinarySniffer = textBinarySniffer;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;

        var resolveResult = resourceRegistry.ResolveResourcePath(Resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{Resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        var infoResult = await resourceFileSystem.GetInfoAsync(Resource);
        if (infoResult.IsFailure)
        {
            return Result.Fail($"Failed to probe resource: '{Resource}'")
                .WithErrors(infoResult);
        }
        var info = infoResult.Value;

        if (info.Kind == StorageItemKind.File)
        {
            var extension = Path.GetExtension(resourcePath);
            var isText = !_textBinarySniffer.IsBinaryExtension(extension)
                && _textBinarySniffer.IsTextFile(resourcePath).IsSuccess
                && _textBinarySniffer.IsTextFile(resourcePath).Value;
            int? lineCount = null;

            if (isText)
            {
                lineCount = await CountLinesAsync(resourceFileSystem, Resource);
            }

            // Surface the paired sidecar's key and current parse state when
            // the registry has recorded one for this file. Sidecars belong to
            // file resources only; folders don't have their own sidecars in v1.
            ResourceKey? sidecarKey = null;
            CelParseStatus? sidecarStatus = null;
            var resourceResult = resourceRegistry.GetResource(Resource);
            if (resourceResult.IsSuccess
                && resourceResult.Value is IFileResource fileResource
                && fileResource.Sidecar is not null)
            {
                sidecarKey = fileResource.Sidecar.Key;
                sidecarStatus = fileResource.Sidecar.Status;
            }

            ResultValue = new FileInfoSnapshot(
                Exists: true,
                IsFile: true,
                Size: info.Size,
                ModifiedUtc: info.ModifiedUtc,
                Extension: extension,
                IsText: isText,
                LineCount: lineCount,
                SidecarKey: sidecarKey,
                SidecarStatus: sidecarStatus);

            return Result.Ok();
        }

        if (info.Kind == StorageItemKind.Folder)
        {
            ResultValue = new FileInfoSnapshot(
                Exists: true,
                IsFile: false,
                Size: 0,
                ModifiedUtc: info.ModifiedUtc,
                Extension: string.Empty,
                IsText: false,
                LineCount: null,
                SidecarKey: null,
                SidecarStatus: null);

            return Result.Ok();
        }

        return Result.Fail($"Resource not found: '{Resource}'");
    }

    // Streams the file via the gateway and counts lines without loading
    // the entire content into memory. Used for the LineCount field on the
    // FileInfoSnapshot when the resource is text.
    private static async Task<int> CountLinesAsync(IResourceFileSystem resourceFileSystem, ResourceKey resource)
    {
        var openResult = await resourceFileSystem.OpenReadAsync(resource);
        if (openResult.IsFailure)
        {
            return 0;
        }

        int count = 0;
        await using var stream = openResult.Value;
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync() is not null)
        {
            count++;
        }
        return count;
    }
}
