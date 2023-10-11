using Celbridge.Utils;
using System;
using System.IO;

namespace Celbridge.Models
{
    [ResourceType("CelScript", "A Cel Script resource", "Page2", ".cel")]
    public class CelScriptResource : FileResource, IDocumentEntity
    {
        public static Result CreateResource(string path)
        {
            try
            {
                File.WriteAllText(path, "{}");
            }
            catch (Exception ex)
            {
                return new ErrorResult($"Failed to create CelScript resource at '{path}'. {ex.Message}");
            }

            return new SuccessResult();
        }
    }
}
