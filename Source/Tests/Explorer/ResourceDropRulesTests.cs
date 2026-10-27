using Celbridge.Explorer.Helpers;
using Celbridge.Resources;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Tests where resources dragged within the Explorer tree can land. The operation service answers for the policy,
/// and these tests pin the rules the tree adds on top of it.
/// </summary>
[TestFixture]
public class ResourceDropRulesTests
{
    private IResourceOperationService _operationService = null!;

    [SetUp]
    public void Setup()
    {
        _operationService = Substitute.For<IResourceOperationService>();
        _operationService.CanAddToFolder(Arg.Any<ResourceKey>()).Returns(Result.Ok());
        _operationService.CanCreateResource(Arg.Any<ResourceKey>(), Arg.Any<bool>()).Returns(Result.Ok());
    }

    [Test]
    public void AFolderCannotDropOntoItself()
    {
        CanDropInto("a", Folder("a")).Should().BeFalse();
    }

    [Test]
    public void AFolderCannotDropIntoAFolderBelowIt()
    {
        CanDropInto("a/b", Folder("a")).Should().BeFalse();
        CanDropInto("a/b/c", Folder("a")).Should().BeFalse();
    }

    [Test]
    public void AFolderCanDropIntoAFolderBesideIt()
    {
        CanDropInto("b", Folder("a")).Should().BeTrue();
        CanDropInto("ab", Folder("a")).Should().BeTrue();
    }

    [Test]
    public void OneDraggedFolderBelowTheDestinationRefusesTheWholeDrop()
    {
        CanDropInto("a/b", File("c.txt"), Folder("a")).Should().BeFalse();
    }

    [Test]
    public void AResourceCanDropBackIntoItsOwnFolder()
    {
        CanDropInto("a", File("a/x.txt")).Should().BeTrue();

        // The move changes nothing, so the policy is left out of the decision.
        _operationService.DidNotReceive().CanCreateResource(Arg.Any<ResourceKey>(), Arg.Any<bool>());
    }

    [Test]
    public void ADropIsRefusedWhenTheFolderRefusesAdditions()
    {
        _operationService.CanAddToFolder(ResourceKey.Create("b")).Returns(Result.Fail("Refused"));

        CanDropInto("b", File("a/x.txt")).Should().BeFalse();
    }

    [Test]
    public void ADropIsRefusedWhenAResourceIsNotPermittedThere()
    {
        _operationService.CanCreateResource(ResourceKey.Create("b/x.txt"), false).Returns(Result.Fail("Refused"));

        CanDropInto("b", File("a/x.txt")).Should().BeFalse();
        CanDropInto("b", File("a/y.txt")).Should().BeTrue();
    }

    [Test]
    public void TheTransferLeavesOutResourcesAlreadyInTheFolder()
    {
        var resources = new List<ResourceItem> { File("a/x.txt"), File("b/y.txt"), Folder("b/c") };

        var transferred = ResourceDropRules.GetResourcesToTransfer(resources, ResourceKey.Create("a"));

        transferred.Should().Equal(ResourceKey.Create("b/y.txt"), ResourceKey.Create("b/c"));
    }

    private bool CanDropInto(string destFolder, params ResourceItem[] draggedResources)
    {
        return ResourceDropRules.CanDropInto(_operationService, ResourceKey.Create(destFolder), draggedResources);
    }

    private static ResourceItem Folder(string key)
    {
        return new ResourceItem(ResourceKey.Create(key), IsFolder: true);
    }

    private static ResourceItem File(string key)
    {
        return new ResourceItem(ResourceKey.Create(key), IsFolder: false);
    }
}
