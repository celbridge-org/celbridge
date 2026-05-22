using Celbridge.Commands;
using Celbridge.Utilities;
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
        await Task.CompletedTask;

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(Resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{Resource}'");
        }
        var resourcePath = resolveResult.Value;

        if (File.Exists(resourcePath))
        {
            var fileInfo = new FileInfo(resourcePath);
            var isText = IsTextFile(_textBinarySniffer, resourcePath);
            int? lineCount = null;

            if (isText)
            {
                lineCount = File.ReadAllLines(resourcePath).Length;
            }

            // Surface the paired sidecar's key and current parse state when
            // the registry has recorded one for this file. Sidecars belong to
            // file resources only; folders don't have their own sidecars in v1.
            string? sidecarKey = null;
            SidecarStatus? sidecarStatus = null;
            var resourceResult = resourceRegistry.GetResource(Resource);
            if (resourceResult.IsSuccess
                && resourceResult.Value is IFileResource fileResource
                && fileResource.Sidecar is not null)
            {
                sidecarKey = fileResource.Sidecar.Key.ToString();
                sidecarStatus = fileResource.Sidecar.Status;
            }

            ResultValue = new FileInfoSnapshot(
                Exists: true,
                IsFile: true,
                Size: fileInfo.Length,
                ModifiedUtc: fileInfo.LastWriteTimeUtc,
                Extension: fileInfo.Extension,
                IsText: isText,
                LineCount: lineCount,
                SidecarKey: sidecarKey,
                SidecarStatus: sidecarStatus);

            return Result.Ok();
        }

        if (Directory.Exists(resourcePath))
        {
            var directoryInfo = new DirectoryInfo(resourcePath);

            ResultValue = new FileInfoSnapshot(
                Exists: true,
                IsFile: false,
                Size: 0,
                ModifiedUtc: directoryInfo.LastWriteTimeUtc,
                Extension: string.Empty,
                IsText: false,
                LineCount: null,
                SidecarKey: null,
                SidecarStatus: null);

            return Result.Ok();
        }

        return Result.Fail($"Resource not found: '{Resource}'");
    }

    private static bool IsTextFile(ITextBinarySniffer textBinarySniffer, string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (textBinarySniffer.IsBinaryExtension(extension))
        {
            return false;
        }

        var result = textBinarySniffer.IsTextFile(filePath);
        return result.IsSuccess && result.Value;
    }
}
