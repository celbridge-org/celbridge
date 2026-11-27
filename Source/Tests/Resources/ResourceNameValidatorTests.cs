using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Validators;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Resources;

/// <summary>
/// A name validator checks the name of a resource against the folder that will contain the resource. The kind of
/// resource sets the rules. A file to save may replace an existing file. A folder name may end in the sidecar
/// extension.
/// </summary>
[TestFixture]
public class ResourceNameValidatorTests
{
    private static readonly ResourceKey ParentFolder = new("docs");
    private static readonly ResourceKey MissingFolder = new("missing");

    private IResourceOperationService _operations = null!;
    private ResourceNameValidatorFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        // docs/images/
        // docs/locked.md   (read-only)
        // docs/notes.md
        var folderChildren = new List<FolderChild>
        {
            new(new ResourceKey("docs/images"), true, 0, default, FileSystemAttributes.None),
            new(new ResourceKey("docs/locked.md"), false, 1, default, FileSystemAttributes.ReadOnly),
            new(new ResourceKey("docs/notes.md"), false, 1, default, FileSystemAttributes.None)
        };

        var fileSystem = Substitute.For<IResourceFileSystem>();
        IReadOnlyList<FolderChild> listing = folderChildren;
        fileSystem.EnumerateFolderAsync(ParentFolder).Returns(Result<IReadOnlyList<FolderChild>>.Ok(listing));
        fileSystem.EnumerateFolderAsync(MissingFolder).Returns(Result<IReadOnlyList<FolderChild>>.Fail("Missing"));

        var sidecars = Substitute.For<ISidecarService>();
        sidecars.IsSidecarFileName(Arg.Any<string>())
            .Returns(callInfo => callInfo.Arg<string>().EndsWith(".cel", StringComparison.OrdinalIgnoreCase));

        _operations = Substitute.For<IResourceOperationService>();
        _operations.CanCreateResource(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(Result.Ok());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.FileSystem.Returns(fileSystem);
        resourceService.Sidecars.Returns(sidecars);
        resourceService.Operations.Returns(_operations);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // A formatted string comes back as the string's name and first argument, so a test can read the argument.
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));
        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            var arguments = callInfo.Arg<object[]>();
            return new LocalizedString(name, $"{name}:{arguments[0]}");
        });

        _factory = new ResourceNameValidatorFactory(stringLocalizer, workspaceWrapper);
    }

    [Test]
    public async Task AFile_RefusesTheNameOfAnExistingFile_AndAcceptsAnEmptyName()
    {
        var validator = await CreateValidatorAsync(ResourceNameKind.File);

        var existingResult = validator.Validate("NOTES.md");
        existingResult.IsValid.Should().BeFalse();
        existingResult.Errors.Should().Equal("Validation_FileNameAlreadyExists:notes.md");

        validator.Validate(string.Empty).IsValid.Should().BeTrue();
    }

    [Test]
    public async Task TheCurrentName_NeverCountsAsTaken()
    {
        var validator = await CreateValidatorAsync(ResourceNameKind.File, currentName: "notes.md");

        validator.Validate("notes.md").IsValid.Should().BeTrue();
        validator.Validate("NOTES.md").IsValid.Should().BeTrue();
        validator.Validate("locked.md").Errors.Should().Equal("Validation_FileNameAlreadyExists:locked.md");
    }

    [Test]
    public async Task AFolder_AcceptsASidecarName_AndAsksThePolicyAboutAFolder()
    {
        var fileValidator = await CreateValidatorAsync(ResourceNameKind.File);
        fileValidator.Validate("data.cel").Errors.Should().Equal("Validation_CelExtensionReserved");

        var folderValidator = await CreateValidatorAsync(ResourceNameKind.Folder);
        folderValidator.Validate("data.cel").IsValid.Should().BeTrue();
        _operations.Received().CanCreateResource(new ResourceKey("docs/data.cel"), true);

        folderValidator.Validate("notes.md").Errors.Should().Equal("Validation_FileNameAlreadyExists:notes.md");
    }

    [Test]
    public async Task AFileToSave_AcceptsTheNameOfAnExistingFile()
    {
        var validator = await CreateValidatorAsync(ResourceNameKind.FileToSave);

        validator.Validate("notes.md").IsValid.Should().BeTrue();
        validator.Validate("NOTES.md").IsValid.Should().BeTrue();
        validator.Validate("summary.md").IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AFileToSave_RefusesTheNameOfAFolder_AReadOnlyFile_AndAnEmptyName()
    {
        var validator = await CreateValidatorAsync(ResourceNameKind.FileToSave);

        validator.Validate("images").Errors.Should().Equal("Validation_FolderNameAlreadyExists:images");
        validator.Validate("locked.md").Errors.Should().Equal("Validation_FileIsReadOnly:locked.md");
        validator.Validate(string.Empty).Errors.Should().Equal("Validation_FileNameRequired");
        validator.Validate("   ").IsValid.Should().BeFalse();
    }

    [Test]
    public async Task AFileToSave_StillRefusesASidecarName()
    {
        var validator = await CreateValidatorAsync(ResourceNameKind.FileToSave);

        validator.Validate("notes.md.cel").Errors.Should().Equal("Validation_CelExtensionReserved");
    }

    [Test]
    public async Task AFolderThatCannotBeListed_GivesNoValidator()
    {
        var createResult = await _factory.CreateAsync(MissingFolder, ResourceNameKind.File);

        createResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public void AValidatorFromTheCallersListing_ChecksTheNameAgainstThatListing()
    {
        var notesFile = new FolderChild(new ResourceKey("docs/notes.md"), false, 1, default, FileSystemAttributes.None);

        var emptyListingValidator = _factory.CreateFromListing(ParentFolder, [], ResourceNameKind.File);
        emptyListingValidator.Validate("notes.md").IsValid.Should().BeTrue();

        var validator = _factory.CreateFromListing(ParentFolder, [notesFile], ResourceNameKind.File);
        validator.Validate("notes.md").Errors.Should().Equal("Validation_FileNameAlreadyExists:notes.md");
    }

    private async Task<IValidator> CreateValidatorAsync(ResourceNameKind kind, string? currentName = null)
    {
        var createResult = await _factory.CreateAsync(ParentFolder, kind, currentName);
        createResult.IsSuccess.Should().BeTrue();

        return createResult.Value;
    }
}
