using Celbridge.Projects.Services;
using System.Text.RegularExpressions;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// The version format was standardized in v0.1.5 to the following format.
/// [celbridge]
/// celbridge-version = "0.1.5"
/// </summary>
public class MigrationStep_0_1_5 : IMigrationStep
{
    public Version TargetVersion => new Version("0.1.5");

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var originalText = await File.ReadAllTextAsync(context.ProjectFilePath);
        
        // Check for legacy [celbridge] section format with "version" property (4-digit format)
        // Matches: [celbridge] line followed by a version = "..." line
        var legacyPattern = @"^[ \t]*\[celbridge\][ \t]*\r?\n[ \t]*version[ \t]*=[ \t]*""([^""]*)""[ \t]*\r?\n?";
        var legacyMatch = Regex.Match(originalText, legacyPattern, RegexOptions.Multiline);
        
        if (legacyMatch.Success)
        {
            context.Logger.LogInformation("Converting legacy [celbridge] version property to celbridge-version format");

            // Extract the old version string and convert from legacy 4-digit format to new 3-digit format for v0.1.5
            var oldVersion = legacyMatch.Groups[1].Value;
            var newVersion = "0.1.5";
            
            // Replace with new format: [celbridge].celbridge-version property

            var updatedText = Regex.Replace(
                originalText, 
                legacyPattern, 
                $"[celbridge]\ncelbridge-version = \"{newVersion}\"\n",
                RegexOptions.Multiline);
            
            await File.WriteAllTextAsync(context.ProjectFilePath, updatedText);
            context.Logger.LogInformation("Converted legacy version property to celbridge-version format");
        }
        
        return Result.Ok();
    }
}
