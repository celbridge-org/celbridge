using CelUtilities.ErrorHandling;
using System;
using System.IO;

namespace CelUtilities.Resources
{
    public static class ResourceUtils
    {
        /// <summary>
        /// Returns an absolute path to a file in the project folder for the specified resourceKey.
        /// The resource key must start with:
        ///   '@' : Project folder resource
        ///   '#' : Relative or absolute path
        /// </summary>
        public static Result<string> GetResourcePath(string resourceKey, string projectFolder, bool mustExist)
        {
            if (string.IsNullOrEmpty(resourceKey))
            {
                return new ErrorResult<string>($"Failed to get resource path. Resource key is empty.");
            }

            string path = null;
            if (resourceKey.StartsWith("@"))
            {
                // Resource in the project folder, map it to an absolute path
                try
                {
                    var relativePath = resourceKey.Substring(1);
                    var combined = Path.Combine(projectFolder, relativePath);
                    path = Path.GetFullPath(combined);
                }
                catch (Exception ex)
                {
                    return new ErrorResult<string>($"Failed to get resource path. {ex}");
                }
            }
            else if (resourceKey.StartsWith("#"))
            {
                // Resource in the file system
                try
                {
                    path = resourceKey.Substring(1);
                    path = Path.GetFullPath(path);
                }
                catch (Exception ex)
                {
                    return new ErrorResult<string>($"Failed to get resource path. {ex}");
                }
            }

            if (string.IsNullOrEmpty(path))
            {
                return new ErrorResult<string>($"Failed to get path for resource key: {resourceKey}");
            }

            if (mustExist && !Path.Exists(path))
            {
                return new ErrorResult<string>($"Failed to get path for resource '{resourceKey}'. Resource does not exist at '{path}'");
            }

            return new SuccessResult<string>(path);
        }
    }
}
