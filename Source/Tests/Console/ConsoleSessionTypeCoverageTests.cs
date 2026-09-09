using System.Text.RegularExpressions;
using Celbridge.Console;
using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

/// <summary>
/// A session type is defined in two halves: a provider the host registers, and a module under
/// Web/Console/types/ holding the fields the settings form edits it through. The form offers only a type it
/// has a module for, so a type registered without one never reaches the Type dropdown. These tests pin the
/// two halves together, reading the registered types from the providers themselves, so adding a session
/// type provider without its module fails here rather than showing up as a type the user cannot select.
/// </summary>
[TestFixture]
public class ConsoleSessionTypeCoverageTests
{
    private static readonly Regex SessionTypeOptionRegex =
        new(@"<select id=""session-type"">\s*<option", RegexOptions.Compiled);

    [Test]
    public void EveryRegisteredSessionType_HasFieldsInTheConsoleSettingsForm()
    {
        var typesFolderPath = Path.Combine(Path.GetDirectoryName(FindConsoleIndexHtml())!, "types");
        Directory.Exists(typesFolderPath).Should().BeTrue("the console type modules should be copied to the test output");

        var moduleTypeIds = Directory
            .GetFiles(typesFolderPath, "*.js")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        moduleTypeIds.Should().BeEquivalentTo(RegisteredTypeIds());
    }

    [Test]
    public void TheSessionTypeDropdown_DeclaresNoOptionsOfItsOwn()
    {
        // The options are built from the types the host reports, so a hardcoded one would offer a type
        // nothing is registered for, or hide one that is.
        var html = File.ReadAllText(FindConsoleIndexHtml());

        SessionTypeOptionRegex.IsMatch(html).Should().BeFalse(
            "the Type options are built from the host's registered types, not authored in the markup");
    }

    // The session types the console registers, read from the providers rather than listed here: a list
    // would be the thing adding a type has to remember to update, which is what this test exists to catch.
    private static IReadOnlyList<string> RegisteredTypeIds()
    {
        var providerTypes = typeof(ShellSessionProvider).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => typeof(IConsoleSessionProvider).IsAssignableFrom(type));

        return providerTypes
            .Select(CreateProvider)
            .Select(provider => provider.SessionType.TypeId)
            .ToList();
    }

    // A provider reports its session type from instance state, so it has to be built to be asked. Nothing
    // it is constructed with takes part in that, so a substitute stands in for each constructor parameter.
    private static IConsoleSessionProvider CreateProvider(Type providerType)
    {
        var constructor = providerType.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => Substitute.For(new[] { parameter.ParameterType }, Array.Empty<object>()))
            .ToArray();

        return (IConsoleSessionProvider)constructor.Invoke(arguments);
    }

    private static string FindConsoleIndexHtml()
    {
        var indexHtmlPath = Directory
            .GetFiles(AppContext.BaseDirectory, "index.html", SearchOption.AllDirectories)
            .FirstOrDefault(path => Path.GetFileName(Path.GetDirectoryName(path)) == "Console");

        indexHtmlPath.Should().NotBeNull("the Console web app should be copied to the test output");

        return indexHtmlPath!;
    }
}
