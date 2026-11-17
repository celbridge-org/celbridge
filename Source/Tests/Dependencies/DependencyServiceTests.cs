using Celbridge.Automation;
using Celbridge.Dependencies;
using Celbridge.Dependencies.Services;
using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Microsoft.Extensions.Configuration;

namespace Celbridge.Tests.Dependencies;

/// <summary>
/// Covers which dependencies a project's features install, in which order, and what a failed install reports. The
/// alpha feature is on by default, and the beta and gamma features are off by default.
/// </summary>
[TestFixture]
public class DependencyServiceTests
{
    private static readonly FeatureFlagDescriptor Alpha = new("alpha", string.Empty, string.Empty);
    private static readonly FeatureFlagDescriptor Beta = new("beta", string.Empty, string.Empty);
    private static readonly FeatureFlagDescriptor Gamma = new("gamma", string.Empty, string.Empty);

    private List<string> _calls = null!;
    private IFeatureFlags _featureFlags = null!;
    private IDialogService _dialogService = null!;

    [SetUp]
    public void Setup()
    {
        _calls = new List<string>();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:alpha"] = "true",
            })
            .Build();
        _featureFlags = new FeatureFlags(configuration, Substitute.For<IMessengerService>());

        _dialogService = Substitute.For<IDialogService>();
    }

    [Test]
    public async Task InstallDependencies_InstallsWhatTheFeaturesThatAreOnNeed_InEachFeaturesOrder()
    {
        var service = CreateService(
            new TestFeatureDependencies(Alpha, CreateDependencies("first", "second")),
            new TestFeatureDependencies(Beta, CreateDependencies("third")),
            new TestFeatureDependencies(Gamma, CreateDependencies("fourth")));
        var projectFeatures = new Dictionary<string, bool>
        {
            ["gamma"] = true,
        };

        var failure = await service.InstallDependenciesAsync(projectFeatures);

        failure.Should().BeNull();
        InstalledIds().Should().Equal("first", "second", "fourth");
    }

    [Test]
    public async Task InstallDependencies_AProjectThatTurnsAFeatureOff_InstallsNothingForIt()
    {
        var service = CreateService(new TestFeatureDependencies(Alpha, CreateDependencies("first")));
        var projectFeatures = new Dictionary<string, bool>
        {
            ["alpha"] = false,
        };

        var failure = await service.InstallDependenciesAsync(projectFeatures);

        failure.Should().BeNull();
        _calls.Should().BeEmpty();
    }

    [Test]
    public async Task InstallDependencies_EveryDependencyInstalled_InstallsNothing()
    {
        var installed = new FakeDependency("first", _calls)
        {
            IsInstalled = true
        };
        var service = CreateService(new TestFeatureDependencies(Alpha, new List<IDependency>
        {
            installed,
        }));

        var failure = await service.InstallDependenciesAsync(new Dictionary<string, bool>());

        failure.Should().BeNull();
        _calls.Should().Equal("check:first");
        _dialogService.DidNotReceive().AcquireProgressDialog(Arg.Any<string>());
    }

    [Test]
    public async Task InstallDependencies_AMissingDependency_InstallsUnderItsProgressTitle()
    {
        var progressToken = Substitute.For<IProgressDialogToken>();
        _dialogService.AcquireProgressDialog("Installing first").Returns(progressToken);
        var service = CreateService(new TestFeatureDependencies(Alpha, CreateDependencies("first")));

        var failure = await service.InstallDependenciesAsync(new Dictionary<string, bool>());

        failure.Should().BeNull();
        _calls.Should().Equal("check:first", "install:first");
        progressToken.Received(1).Dispose();
    }

    [Test]
    public async Task InstallDependencies_AFailedInstall_ReturnsTheFeatureAndTheDependency_AndStops()
    {
        var failing = new FakeDependency("first", _calls)
        {
            InstallResult = Result.Fail("The network is down")
        };
        var later = new FakeDependency("second", _calls);
        var service = CreateService(new TestFeatureDependencies(Alpha, new List<IDependency>
        {
            failing,
            later,
        }));

        var failure = await service.InstallDependenciesAsync(new Dictionary<string, bool>());

        failure.Should().NotBeNull();
        failure!.Feature.Should().Be(Alpha);
        failure.DependencyName.Should().Be("first name");
        failure.Error.MessageChain.Should().Contain("The network is down");
        InstalledIds().Should().Equal("first");
    }

    [Test]
    public async Task InstallDependencies_AnInstallThatThrows_IsAFailure()
    {
        var throwing = new FakeDependency("first", _calls)
        {
            ThrowsOnInstall = true
        };
        var service = CreateService(new TestFeatureDependencies(Alpha, new List<IDependency>
        {
            throwing,
        }));

        var failure = await service.InstallDependenciesAsync(new Dictionary<string, bool>());

        failure.Should().NotBeNull();
        failure!.DependencyName.Should().Be("first name");
    }

    [Test]
    public async Task InstallDependencies_ADependencyThatTwoFeaturesShare_IsCheckedOnce()
    {
        var shared = new FakeDependency("shared", _calls);
        var service = CreateService(
            new TestFeatureDependencies(Alpha, new List<IDependency>
            {
                shared,
            }),
            new TestFeatureDependencies(Gamma, new List<IDependency>
            {
                shared,
            }));
        var projectFeatures = new Dictionary<string, bool>
        {
            ["gamma"] = true,
        };

        await service.InstallDependenciesAsync(projectFeatures);

        _calls.Should().Equal("check:shared", "install:shared");
    }

    private DependencyService CreateService(params IFeatureDependencies[] featureDependencies)
    {
        return new DependencyService(
            featureDependencies,
            _featureFlags,
            _dialogService,
            Substitute.For<IAutomationService>(),
            new NullLogger<DependencyService>());
    }

    private List<IDependency> CreateDependencies(params string[] ids)
    {
        var dependencies = new List<IDependency>();
        foreach (var id in ids)
        {
            dependencies.Add(new FakeDependency(id, _calls));
        }

        return dependencies;
    }

    private IEnumerable<string> InstalledIds()
    {
        return _calls
            .Where(call => call.StartsWith("install:"))
            .Select(call => call["install:".Length..]);
    }

    private sealed record TestFeatureDependencies(FeatureFlagDescriptor Feature, IReadOnlyList<IDependency> Dependencies)
        : IFeatureDependencies;

    // Records each check and install in the shared list of calls, so a test reads the order across dependencies.
    private sealed class FakeDependency : IDependency
    {
        private readonly List<string> _calls;

        public FakeDependency(string id, List<string> calls)
        {
            Id = id;
            _calls = calls;
        }

        public string Id { get; }

        public string Version => "1.0.0";

        public string Name => $"{Id} name";

        public string ProgressTitle => $"Installing {Id}";

        public bool IsInstalled { get; init; }

        public Result InstallResult { get; init; } = Result.Ok();

        public bool ThrowsOnInstall { get; init; }

        public Task<bool> IsInstalledAsync()
        {
            _calls.Add($"check:{Id}");

            return Task.FromResult(IsInstalled);
        }

        public Task<Result> InstallAsync()
        {
            _calls.Add($"install:{Id}");
            if (ThrowsOnInstall)
            {
                throw new InvalidOperationException("The installer crashed");
            }

            return Task.FromResult(InstallResult);
        }
    }
}
