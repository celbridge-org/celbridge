using System.Text.Json.Nodes;
using Celbridge.Settings;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// How to start a language server: the executable's absolute path and the arguments. The server's environment is
/// Celbridge's own, with the added variables set and the removed variables left out.
/// </summary>
public sealed record LanguageServerInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    IReadOnlyList<string> RemovedEnvironmentVariables);

/// <summary>
/// Names files by extension and by name. The language client reports each change on disk to one of the files to a
/// server. An extension is lowercase and starts with a dot, and a name is lowercase.
/// </summary>
public sealed record ReportedFiles(
    IReadOnlySet<string> Extensions,
    IReadOnlySet<string> FileNames);

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
    /// Returns the files to report to the server when one of them changes on disk. Returns null for a server that
    /// watches the project's files itself.
    /// </summary>
    ReportedFiles? ReportedFiles { get; }

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
