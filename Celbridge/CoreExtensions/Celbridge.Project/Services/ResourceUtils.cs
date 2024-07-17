namespace Celbridge.Project.Services;

public class ResourceUtils
{
    public static void CopyFolder(string sourceFolder, string destFolder)
    {
        DirectoryInfo dir = new DirectoryInfo(sourceFolder);

        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceFolder}");
        }

        DirectoryInfo[] dirs = dir.GetDirectories();
        if (!Directory.Exists(destFolder))
        {
            Directory.CreateDirectory(destFolder);
        }

        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            string tempPath = Path.Combine(destFolder, file.Name);
            file.CopyTo(tempPath);
        }

        foreach (DirectoryInfo subdir in dirs)
        {
            string tempPath = Path.Combine(destFolder, subdir.Name);
            CopyFolder(subdir.FullName, tempPath);
        }
    }
}
