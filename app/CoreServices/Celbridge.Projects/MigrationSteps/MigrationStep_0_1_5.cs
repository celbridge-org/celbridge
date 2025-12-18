using Celbridge.Projects.Services;
using System.Text.RegularExpressions;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// This migration handles the format change introduced in version 0.1.5 where the project
/// version format was standardized, e.g. celbridge.version = "0.1.5"
/// </summary>
public class MigrationStep_0_1_5 : IMigrationStep
{
    public Version TargetVersion => new Version("0.1.5");

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var originalText = await File.ReadAllTextAsync(context.ProjectFilePath);
        
        // Check for legacy [celbridge] section format using a regex
        // Matches: [celbridge] line followed by a version = "..." line
        // Pattern: optional indentation, [celbridge], line ending, optional indentation, version = "...", line ending
        var legacyPattern = @"^[ \t]*\[celbridge\][ \t]*\r?\n[ \t]*version[ \t]*=[ \t]*""[^""]*""[ \t]*\r?\n?";
        var legacyMatch = Regex.Match(originalText, legacyPattern, RegexOptions.Multiline);
        
        bool hasLegacyFormat = legacyMatch.Success;
        
        if (hasLegacyFormat)
        {
            context.Logger.LogInformation("Converting legacy [celbridge] section format to celbridge.version format");
            
            // Replace the legacy section with modern dotted notation at the same position
            // This preserves the position in the file and any surrounding content
            var updatedText = Regex.Replace(
                originalText, 
                legacyPattern, 
                $"celbridge.version = \"{TargetVersion}\"\n",
                RegexOptions.Multiline);
            
            await File.WriteAllTextAsync(context.ProjectFilePath, updatedText);
            context.Logger.LogInformation("Converted legacy [celbridge] section to celbridge.version format");
        }
        
        return Result.Ok();
    }
}
