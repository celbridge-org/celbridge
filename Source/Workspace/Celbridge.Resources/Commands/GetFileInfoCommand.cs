using Celbridge.Commands;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class GetFileInfoCommand : CommandBase, IGetFileInfoCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ITextBinarySniffer _textBinarySniffer;

    public override CommandFlags CommandFlags => CommandFlags.Query;

    public ResourceKey Resource { get; set; }

    public FileInfoSnapshot ResultValue { get; private set; }
        = new FileInfoSnapshot(
            Exists: false,
            IsFile: false,
            Size: 0,
            ModifiedUtc: DateTime.MinValue,
            Extension: string.Empty,
            IsText: false,
            LineCount: null);

    public GetFileInfoCommand(
        IWorkspaceWrapper workspaceWrapper,
        ITextBinarySniffer textBinarySniffer)
    {
        _workspaceWrapper = workspaceWrapper;
        _textBinarySniffer = textBinarySniffer;
    }

    public override Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(Resource);
        if (resolveResult.IsFailure)
        {
            return Task.FromResult<Result>(Result.Fail($"Failed to resolve path for resource: '{Resource}'"));
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

            ResultValue = new FileInfoSnapshot(
                Exists: true,
                IsFile: true,
                Size: fileInfo.Length,
                ModifiedUtc: fileInfo.LastWriteTimeUtc,
                Extension: fileInfo.Extension,
                IsText: isText,
                LineCount: lineCount);

            return Task.FromResult(Result.Ok());
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
                LineCount: null);

            return Task.FromResult(Result.Ok());
        }

        return Task.FromResult<Result>(Result.Fail($"Resource not found: '{Resource}'"));
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
