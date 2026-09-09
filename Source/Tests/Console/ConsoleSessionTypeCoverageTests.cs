using System.Text.Json;
using System.Text.RegularExpressions;
using Celbridge.Console;
using Celbridge.Console.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Console;

/// <summary>
/// A session type is defined in two halves: a provider the host registers, and a module under
/// Web/Console/types/ holding the fields the settings form edits it through. The form offers only a type it
/// has a module for, so a type registered without one never reaches the Type dropdown. These tests pin the
/// two halves together, reading the registered types from the DI registrations, so adding a session type
/// provider without its module fails here rather than showing up as a type the user cannot select.
/// </summary>
[TestFixture]
public class ConsoleSessionTypeCoverageTests
{
    private static readonly Regex SessionTypeOptionRegex =
        new(@"<select id=""session-type"">\s*<option", RegexOptions.Compiled);

    // The document key a module's field writes, as the module spells it: key: 'executable'.
    private static readonly Regex ModuleFieldKeyRegex =
        new(@"key:\s*'([^']+)'", RegexOptions.Compiled);

    [Test]
    public void EveryRegisteredSessionType_HasFieldsInTheConsoleSettingsForm()
    {
        var moduleTypeIds = Directory
            .GetFiles(TypesFolderPath(), "*.js")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();

        var registeredTypeIds = RegisteredSessionTypes().Select(sessionType => sessionType.TypeId);

        moduleTypeIds.Should().BeEquivalentTo(registeredTypeIds);
    }

    [Test]
    public void EveryRegisteredSessionType_DeclaresEveryKeyItsModuleWrites()
    {
        foreach (var sessionType in RegisteredSessionTypes())
        {
            var moduleKeys = ModuleFieldKeyRegex
                .Matches(File.ReadAllText(ModulePath(sessionType.TypeId)))
                .Select(match => match.Groups[1].Value)
                .ToArray();

            // The script key is accepted in every type's table, so the host owns it rather than the type.
            var declaredKeys = sessionType.OptionKeys
                .Append(ConsoleDocumentConfigParser.ScriptKey)
                .ToArray();

            moduleKeys.Should().BeEquivalentTo(
                declaredKeys,
                $"the {sessionType.TypeId} module writes the keys its provider declares in OptionKeys");
        }
    }

    [Test]
    public void EveryRegisteredSessionType_NamesItselfInTheConsoleStrings()
    {
        // The dropdown label and the section description are looked up by a key built from the type id, so
        // a missing entry renders the raw id instead of being caught by the markup localization sweep.
        var definedKeys = LocalizationKeys();

        foreach (var sessionType in RegisteredSessionTypes())
        {
            var typeId = sessionType.TypeId;
            var suffix = char.ToUpperInvariant(typeId[0]) + typeId.Substring(1);

            definedKeys.Should().Contain($"Console_Type_{suffix}");
            definedKeys.Should().Contain($"Console_Desc_{suffix}");
        }
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

    // The session types the console registers, read from the DI registrations rather than from the classes
    // the assembly happens to hold, so a provider nobody registers is not counted as a type the host offers.
    // A provider registered from another module would not be seen here, which is the arrangement this test
    // holds the console to.
    private static IReadOnlyList<ConsoleSessionType> RegisteredSessionTypes()
    {
        var services = new ServiceCollection();
        Celbridge.Console.ServiceConfiguration.ConfigureServices(services);

        var providerTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IConsoleSessionProvider))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();

        providerTypes.Should().NotBeEmpty("the console should register a session type provider");
        providerTypes.Should().NotContainNulls("a session type provider should be registered by its type");

        return providerTypes
            .Select(providerType => CreateProvider(providerType!))
            .Select(provider => provider.SessionType)
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

    private static IReadOnlyCollection<string> LocalizationKeys()
    {
        var enJsonPath = Path.Combine(ConsoleFolderPath(), "localization", "en.json");
        using var document = JsonDocument.Parse(File.ReadAllText(enJsonPath));

        return document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }

    private static string ModulePath(string typeId)
    {
        var modulePath = Path.Combine(TypesFolderPath(), $"{typeId}.js");
        modulePath.Should().Match(path => File.Exists(path), $"the {typeId} session type should have a module");

        return modulePath;
    }

    private static string TypesFolderPath()
    {
        var typesFolderPath = Path.Combine(ConsoleFolderPath(), "types");
        Directory.Exists(typesFolderPath).Should().BeTrue("the console type modules should be copied to the test output");

        return typesFolderPath;
    }

    private static string ConsoleFolderPath()
    {
        return Path.GetDirectoryName(FindConsoleIndexHtml())!;
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
