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
            IsReadOnly: false,
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
        var rootHandlerRegistry = workspaceService.ResourceService.RootHandlers;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;

        var resolveResult = rootHandlerRegistry.ResolveResourcePath(Resource);
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

            // Surfaces the paired sidecar's key and parse state when the file has a sidecar on disk. Sidecars pair
            // with files other than .cel files, and this branch runs for files only.
            ResourceKey? sidecarKey = null;
            CelParseStatus? sidecarStatus = null;
            var sidecarService = workspaceService.ResourceService.Sidecars;
            var sidecarKeyResult = sidecarService.GetSidecarKey(Resource);
            if (sidecarKeyResult.IsSuccess)
            {
                var readResult = await sidecarService.ReadAsync(Resource);
                if (readResult.IsSuccess
                    && readResult.Value.Outcome != SidecarReadOutcome.NoSidecar)
                {
                    sidecarKey = sidecarKeyResult.Value;
                    sidecarStatus = readResult.Value.Outcome == SidecarReadOutcome.Healthy
                        ? CelParseStatus.Healthy
                        : CelParseStatus.Broken;
                }
            }

            ResultValue = new FileInfoSnapshot(
                Exists: true,
                IsFile: true,
                Size: info.Size,
                ModifiedUtc: info.ModifiedUtc,
                Extension: extension,
                IsText: isText,
                LineCount: lineCount,
                IsReadOnly: (info.Attributes & FileSystemAttributes.ReadOnly) != 0,
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
                IsReadOnly: (info.Attributes & FileSystemAttributes.ReadOnly) != 0,
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
