using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Projects;
using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers what the Features section shows while Safe Mode turns every flag off: each toggle still shows the
/// project file's value, or the application's default.
/// </summary>
[TestFixture]
public class FeatureFlagsSectionViewModelTests
{
    private IServiceProvider? _previousServiceProvider;
    private FeatureFlags _featureFlags = null!;

    [SetUp]
    public void Setup()
    {
        var configData = new Dictionary<string, string?>
        {
            [$"FeatureFlags:{FeatureFlagCatalog.WebViewDevTools.Name}"] = "true",
            [$"FeatureFlags:{FeatureFlagCatalog.NoteEditor.Name}"] = "false"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        _featureFlags = new FeatureFlags(configuration, Substitute.For<IMessengerService>());

        var services = new ServiceCollection();
        services.AddSingleton<IFeatureFlags>(_featureFlags);

        // The section acquires the flags through the global ServiceLocator. The fixture restores the
        // ServiceLocator after each test.
        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());
    }

    [TearDown]
    public void TearDown()
    {
        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
    }

    [Test]
    public void InSafeMode_TheSectionShowsTheProjectValuesAndTheDefaults()
    {
        var projectFeatures = new Dictionary<string, bool>
        {
            [FeatureFlagCatalog.NoteEditor.Name] = true
        };
        _featureFlags.ApplyProjectOverrides(projectFeatures);
        _featureFlags.TurnOffAllFeatures();

        var section = CreateSection(new ProjectConfig
        {
            Features = projectFeatures
        });

        section.Load();

        FindFlag(section, FeatureFlagCatalog.NoteEditor).IsOn.Should().BeTrue("the project file turns the flag on");
        FindFlag(section, FeatureFlagCatalog.WebViewDevTools).IsOn.Should().BeTrue("the application turns the flag on by default");
    }

    private static FeatureFlagsSectionViewModel CreateSection(ProjectConfig config)
    {
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        // The localizer returns each key as the text, so a test finds a flag by the flag's title key.
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(call => new LocalizedString((string)call[0], (string)call[0]));

        var context = new ProjectSettingsContext(
            Substitute.For<IWorkspaceWrapper>(),
            projectService,
            Substitute.For<ICommandService>(),
            () => { });

        return new FeatureFlagsSectionViewModel(context, stringLocalizer);
    }

    private static FeatureFlagItemViewModel FindFlag(FeatureFlagsSectionViewModel section, FeatureFlagDescriptor flag)
    {
        var titleKey = flag.TitleKey;

        return section.Groups
            .SelectMany(group => group.Flags)
            .Single(flag => flag.Title == titleKey);
    }
}
