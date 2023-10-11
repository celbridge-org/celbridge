using Celbridge.Utils;
using System;
using System.IO;

namespace Celbridge.Models
{
    [ResourceType("Text File", "A text file resource", "Page2", ".txt")]
    public class TextFileResource : FileResource, IDocumentEntity
    {
        public string Permissions { get; set; } = string.Empty;

        [PathProperty]
        public string SomePath { get; set; } = string.Empty;

        public static Result CreateResource(string path)
        {
            try
            {
                File.WriteAllText(path, string.Empty);
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to create file at '{path}'. {ex.Message}");
            }

            return new SuccessResult();
        }
    }
}
