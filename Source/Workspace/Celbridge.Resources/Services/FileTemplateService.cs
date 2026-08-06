using System.Text;
using Celbridge.Workspace;
using ClosedXML.Excel;
using Celbridge.Explorer;

namespace Celbridge.Resources.Services;

/// <summary>
/// Provides initial content for new files based on their file type.
/// Queries extension manifests for template content first, then falls back to built-in templates.
/// </summary>
public class FileTemplateService : IFileTemplateService
{
    // A .webview opens a web page in an embedded browser. The comment block names the
    // supported keys so a hand-edited file does not need external documentation.
    private const string WebViewTemplate =
        """
        # Opens a web page in an embedded browser.
        # source_url is the external http/https page to open. Set show_url_bar = false
        # to hide the browser controls and present the page as an application.
        source_url = ""
        """;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public FileTemplateService(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public byte[] GetNewFileContent(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Check package-provided templates first
        var packageService = _workspaceWrapper.WorkspaceService.PackageService;
        var packageContent = packageService.GetDefaultTemplateContent(extension);
        if (packageContent is not null)
        {
            return packageContent;
        }

        if (extension == ExplorerConstants.WebViewExtension)
        {
            return Encoding.UTF8.GetBytes(WebViewTemplate + "\n");
        }

        if (extension == ExplorerConstants.ExcelExtension)
        {
            // Create an empty Excel file content
            using var ms = new MemoryStream();
            using var wb = new XLWorkbook();
            var sheet = wb.AddWorksheet("Sheet1");

            // This workaround forces a block of cells to be displayed instead of a single empty cell.
            sheet.Cell(200, 20).Style.NumberFormat.Format = "@";

            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // Default: empty file
        return [];
    }
}
