using Celbridge.Commands;

namespace Celbridge.GenerativeAI;

/// <summary>
/// A command that generates a text file from a prompt via generative AI.
/// </summary>
public interface IMakeTextCommand : IExecutableCommand
{
    /// <summary>
    /// Resource key for the new text file resource.
    /// </summary>
    ResourceKey DestFileResource { get; set; }

    /// <summary>
    /// Prompt used by the generative AI to generate the text.
    /// </summary>
    string Prompt { get; set; }
}
