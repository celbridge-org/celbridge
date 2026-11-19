using System.Text.Json.Nodes;
using Celbridge.Settings;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// How to start a language server: the executable's absolute path, the arguments and the environment variables
/// added to Celbridge's own.
/// </summary>
public sealed record LanguageServerInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables);

/// <summary>
/// A language server for Celbridge to run.
/// </summary>
public interface ILanguageServerDefinition
{
    /// <summary>
    /// Returns the id that names the server in logs and to the code editor page.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Returns the feature that turns the server on for a project. The feature's dependencies install the server.
    /// </summary>
    FeatureFlagDescriptor Feature { get; }

    /// <summary>
    /// Returns the server's file extensions, each with the Language Server Protocol's language id for the
    /// extension. An extension is lowercase and starts with a dot.
    /// </summary>
    IReadOnlyDictionary<string, string> LanguageIds { get; }

    /// <summary>
    /// Returns how to start the server over stdio.
    /// </summary>
    LanguageServerInvocation CreateInvocation();

    /// <summary>
    /// Returns the initialization options sent in the initialize request, or null for none.
    /// </summary>
    JsonNode? CreateInitializationOptions();

    /// <summary>
    /// Returns the settings for one section of a workspace/configuration request, or null for none.
    /// </summary>
    JsonNode? CreateConfiguration(string section);
}
