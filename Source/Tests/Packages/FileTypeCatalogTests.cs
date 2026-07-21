using Celbridge.Packages;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class FileTypeCatalogTests
{
    private readonly IFileTypeCatalog _catalog = new FileTypeCatalog();

    [Test]
    public void GetCategories_MultiCategoryExtension_ReturnsAllCategoriesInOrder()
    {
        _catalog.GetCategories(".json").Should().Equal(FileTypeCategory.Text, FileTypeCategory.Data);
    }

    [Test]
    public void GetCategories_SingleCategoryExtension_ReturnsThatCategory()
    {
        _catalog.GetCategories(".png").Should().Equal(FileTypeCategory.Image);
    }

    [Test]
    public void GetCategories_IsCaseInsensitive()
    {
        _catalog.GetCategories(".PNG").Should().Equal(FileTypeCategory.Image);
    }

    [Test]
    public void GetCategories_UncataloguedExtension_ReturnsEmpty()
    {
        // A code extension the catalog does not list defaults to Text at the call site, not here.
        _catalog.GetCategories(".cs").Should().BeEmpty();
    }
}
