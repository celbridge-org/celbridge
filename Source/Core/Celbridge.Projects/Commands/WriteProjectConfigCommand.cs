using Celbridge.Commands;
using Celbridge.Projects.Services;
using Celbridge.Utilities;

namespace Celbridge.Projects.Commands;

public class WriteProjectConfigCommand : CommandBase, IWriteProjectConfigCommand
{
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;

    public IReadOnlyList<ProjectConfigEdit> Edits { get; set; } = Array.Empty<ProjectConfigEdit>();

    public WriteProjectConfigCommand(
        IProjectService projectService,
        ILocalFileSystem fileSystem)
    {
        _projectService = projectService;
        _fileSystem = fileSystem;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Edits.Count == 0)
        {
            return Result.Ok();
        }

        var currentProject = _projectService.CurrentProject;
        if (currentProject is null)
        {
            return Result.Fail("Cannot edit the project config because no project is loaded.");
        }

        var projectFilePath = currentProject.ProjectFilePath;

        var readResult = await _fileSystem.ReadAllTextAsync(projectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read the project config: '{projectFilePath}'")
                .WithErrors(readResult);
        }
        var originalText = readResult.Value;

        var applyResult = ProjectConfigModifier.ApplyEdits(originalText, Edits);
        if (applyResult.IsFailure)
        {
            return Result.Fail("Failed to apply edits to the project config")
                .WithErrors(applyResult);
        }
        var updatedText = applyResult.Value;

        // The serializer always emits LF, so normalize the on-disk text before comparing; otherwise a
        // CRLF file would be rewritten for a no-op edit. The write below normalizes it to LF anyway.
        var normalizedOriginalText = LineEndingHelper.ConvertLineEndings(originalText, "\n");
        if (updatedText == normalizedOriginalText)
        {
            return Result.Ok();
        }

        var writeResult = await _fileSystem.WriteAllTextAsync(projectFilePath, updatedText);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write the project config: '{projectFilePath}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }
}
