using ClosedXML.Excel;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Provides initial content for new files based on their file type.
/// </summary>
public class FileTemplateService : IFileTemplateService
{
    public byte[] GetNewFileContent(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        if (extension == ExplorerConstants.ExcelExtension)
        {
            // Create an empty Excel file content
            using var ms = new MemoryStream();
            using var wb = new XLWorkbook();
            var sheet = wb.AddWorksheet("Sheet1");

            // This workaround forces a block of cells to be displayed instead of a single empty cell.
            // I think SpreadJS does something similar internally when you add a new sheet.
            sheet.Cell(200, 20).Style.NumberFormat.Format = "@";

            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // Default: empty file
        return [];
    }
}
