namespace Celbridge.Models
{
    [ResourceType("HTML File", "A HTML file resource", "Page2", ".html,.png,.jpg,.pdf")]
    public class HTMLResource : FileResource, IDocumentEntity
    {
        public string StartURL { get; set; } = string.Empty;
        
        public static Result CreateResource(string path)
        {
            try
            {
                string htmlContent = @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Empty HTML Page</title>
</head>
<body>
Empty HTML Page
</body>
</html>
";
                File.WriteAllText(path, htmlContent);
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to create file at '{path}'. {ex.Message}");
            }

            return new SuccessResult();
        }
    }
}
