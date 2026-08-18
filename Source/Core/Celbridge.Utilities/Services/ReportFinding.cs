using System.Globalization;
using Celbridge.Localization;
using Celbridge.Reports;

namespace Celbridge.Utilities;

/// <summary>
/// Builds report items from finding descriptors.
/// </summary>
public static class ReportFinding
{
    /// <summary>
    /// Builds an occurrence of a finding: the descriptor's message localized and composed with the
    /// arguments, at the descriptor's default severity.
    /// </summary>
    public static ReportItem Create(
        ILocalizerService localizerService,
        ReportFindingDescriptor descriptor,
        params object[] arguments)
    {
        // The template is looked up as a key and returned unchanged when nothing matches, so a host
        // descriptor naming a key is localized and a contribution's literal wording passes through.
        var template = localizerService.GetString(descriptor.MessageTemplate);
        var message = ComposeMessage(template, arguments);

        return new ReportItem(descriptor.DefaultSeverity, message)
        {
            Code = descriptor.Code
        };
    }

    // A template whose placeholders do not match the arguments supplied would otherwise throw from
    // inside a report producer, losing the whole report over one malformed finding.
    private static string ComposeMessage(string template, object[] arguments)
    {
        if (arguments.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
