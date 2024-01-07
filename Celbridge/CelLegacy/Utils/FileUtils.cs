using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.Storage;
using Microsoft.UI.Xaml;
using System;
using Celbridge.Services;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Utils
{
    public static class FileUtils
    {
        /// <summary>
        /// Checks if a path is absolute and valid on Windows, macOS, and Linux.
        /// </summary>
        /// <param name="path">The path to check for validity.</param>
        /// <returns>True if the path is valid, false otherwise.</returns>
        public static bool IsAbsolutePathValid(string path)
        {
            // Check if the path is null or empty
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            // Check if the path is an absolute path
            if (!Path.IsPathRooted(path))
            {
                return false;
            }

            // Get the list of invalid path and filename characters
            var invalidPathChars = Path.GetInvalidPathChars();
            var invalidFilenameChars = Path.GetInvalidFileNameChars();

            // Split the path into its components
            var directory = Path.GetDirectoryName(path);
            var filename = Path.GetFileName(path);

            // Check the directory and filename for invalid characters
            if (directory != null && directory.IndexOfAny(invalidPathChars) != -1)
            {
                return false;
            }
            if (filename != null && filename.IndexOfAny(invalidFilenameChars) != -1)
            {
                return false;
            }

            Guard.IsNotNull(directory);

            // Check each folder name for invalid characters
            var folders = directory.Split(Path.DirectorySeparatorChar)[1..];
            foreach (var folder in folders)
            {
                if (folder.IndexOfAny(invalidFilenameChars) >= 0)
                {
                    return false;
                }
            }

#if WINDOWS
            // On Windows, check if the root directory is invalid
            if (path[0] == Path.VolumeSeparatorChar || path[0] == Path.AltDirectorySeparatorChar)
            {
                return false;
            }
#endif

            // All components are valid
            return true;
        }

        public static Result<string> GetRelativePath(string relativeTo, string path)        
        {
            string fullRelativeTo = Path.GetFullPath(relativeTo);
            string fullPath = Path.GetFullPath(path);

            // Get the relative path of the second path with respect to the first path
            string relativePath = Path.GetRelativePath(fullRelativeTo, fullPath);

            // Check if the relative path starts with ".." to determine if the first path contains the second path
            if (relativePath.StartsWith(".."))
            {
                return new ErrorResult<string>($"Paths do not share a common root: '{relativeTo}' and '{path}'");
            }

            return new SuccessResult<string>(relativePath);
        }

        public static async Task<Result<StorageFolder>> ShowFolderPicker()
        {
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            folderPicker.FileTypeFilter.Add("*");

#if WINDOWS
            // For Uno.WinUI-based apps
            var mainWindow = LegacyServiceProvider.MainWindow!;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
#endif

            StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null)
            {
                return new ErrorResult<StorageFolder>("No folder selected");
            }
            return new SuccessResult<StorageFolder>(folder);
        }

        public static async Task<Result<string>> ShowFileOpenPicker(bool relativeOnly = true)
        {
            var fileOpenPicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };

            fileOpenPicker.FileTypeFilter.Add(".txt");

#if WINDOWS
            // For Uno.WinUI-based apps
            var mainWindow = LegacyServiceProvider.MainWindow!;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(fileOpenPicker, hwnd);
#endif

            StorageFile file = await fileOpenPicker.PickSingleFileAsync();

            if (file == null)
            {
                return new ErrorResult<string>("No file selected to open");
            }

            if (!File.Exists(file.Path))
            {
                return new ErrorResult<string>("Selected file does not exist");
            }

            var projectService = LegacyServiceProvider.Services!.GetService<IProjectService>();
            Guard.IsNotNull(projectService);

            if (projectService.ActiveProject != null)
            {
                try
                {
                    var projectPath = projectService.ActiveProject.ProjectPath;
                    var projectFolder = Path.GetDirectoryName(projectPath);
                    Guard.IsNotNull(projectFolder);

                    var result = FileUtils.GetRelativePath(projectFolder, file.Path);
                    if (result.Success)
                    {
                        return new SuccessResult<string>(result.Data!);
                    }
                }
                catch (Exception ex)
                {
                    return new ErrorResult<string>($"Failed to select file. {ex.Message}");
                }
            }

            if (relativeOnly)
            {
                Guard.IsNotNull(projectService.ActiveProject);

                var projectPath = projectService.ActiveProject.ProjectPath;
                var projectFolder = Path.GetDirectoryName(projectPath);
                return new ErrorResult<string>($"Selected file is not in the project folder: {projectFolder}");
            }

            return new SuccessResult<string>(file.Path);
        }

        public static Result DeletePath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    // Delete the file
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    // Delete the directory recursively
                    Directory.Delete(path, true);
                }
                else
                {
                    // Throw an exception if the path does not exist
                    throw new ArgumentException($"The specified path '{path}' does not exist.");
                }
            }
            catch (Exception ex) 
            {
                return new ErrorResult($"Failed to delete: {path}. {ex.Message}");
            }

            return new SuccessResult();
        }

        public static async Task<Result> SaveTextAsync(string path, string text, int maxRetries = 3, int retryDelayMs = 250)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await File.WriteAllTextAsync(path, text);
                }
                catch (Exception ex)
                {
                    // We intermittently get this error when saving "The process cannot access the file because it is being used by another process."
                    // File IO is inherently complex and unreliable, so we retry the save a few times before giving up.

                    if (i == maxRetries - 1)
                    {
                        return new ErrorResult($"Failed to save text at path '{path}'. {ex.Message}");
                    }
                    else
                    {
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

            return new SuccessResult();
        }
    }
}
