namespace Celbridge.Utilities.Services;

public class DumpFile : IDumpFile
{
    private string _dumpFilePath = string.Empty;

    public Result Initialize(string dumpFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(dumpFilePath);
            Guard.IsNotNull(directory);

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(dumpFilePath))
            {
                File.Delete(dumpFilePath);
            }

            File.WriteAllText(dumpFilePath, string.Empty);

            _dumpFilePath = dumpFilePath;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to initialize dump file. {ex}");
        }

        return Result.Ok();
    }

    public Result WriteLine(string line)
    {
        try
        {
            using (var fileStream = new FileStream(_dumpFilePath, FileMode.Append, FileAccess.Write))
            using (var writer = new StreamWriter(fileStream))
            {
                // Write the line of text, adding a newline
                writer.WriteLine(line);
            }

        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write to dump file. {ex}");
        }

        return Result.Ok();
    }

    public Result ClearFile()
    {
        try
        {
            File.WriteAllText(_dumpFilePath, string.Empty);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to clear dump file. {ex}");
        }

        return Result.Ok();
    }
}
