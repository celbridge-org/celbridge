namespace Celbridge.Legacy.Media;

public static class MediaUtils
{
    public static async Task<Result<string>> WriteMediaFile(byte[] audioData, string filename)
    {
        StorageFile? tempFile = null;

        try
        {
            // Create a temporary file to store the audio
            tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(filename, CreationCollisionOption.ReplaceExisting);

            // Write the byte array to the file
            await FileIO.WriteBytesAsync(tempFile, audioData);

            return new SuccessResult<string>(tempFile.Path);
        }
        catch (Exception ex)
        {
            if (tempFile != null)
            {
                await tempFile.DeleteAsync();
            }

            // Log or handle the exception as needed
            return new ErrorResult<string>($"Error writing media file: {ex.Message}");
        }
    }

    public static async Task OpenMediaFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The specified file does not exist.", filePath);
        }

        try
        {
            using (var process = new Process())
            {
                // Run VLC in headless mode
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "vlc",
                    Arguments = $"--intf dummy --play-and-exit \"{filePath}\"",
                    UseShellExecute = true
                };

                process.Start();

                // Asynchronously wait for the process to exit
                await Task.Run(() => process.WaitForExit());
            }
        }
        catch (Exception ex)
        {
            // Handle exceptions (e.g., no application associated with MP3 files)
            System.Console.WriteLine($"Error opening media file: {ex.Message}");
        }
    }
}
