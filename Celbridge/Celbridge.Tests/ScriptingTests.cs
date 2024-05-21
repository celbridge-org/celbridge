using Celbridge.BaseLibrary.Scripting;
using Celbridge.Scripting;
using Celbridge.Scripting.DotNetInteractive;
using Celbridge.Scripting.FakeScript;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

[TestFixture]
public class ScriptingTests
{
    private ServiceProvider? _serviceProvider;
    private IScriptingService? _scriptingService;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IScriptingService, ScriptingService>();
        _serviceProvider = services.BuildServiceProvider();

        _scriptingService = _serviceProvider!.GetRequiredService<IScriptingService>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider != null)
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    [Test]
    public async Task ICanExecuteAScript()
    {
        Guard.IsNotNull(_scriptingService);

        var scriptContextFactory = new FakeScriptContextFactory();

        var registerResult = _scriptingService.RegisterScriptContextFactory(scriptContextFactory);
        registerResult.IsSuccess.Should().BeTrue();

        var scriptContext = _scriptingService.AcquireScriptContext("FakeScript").Value;

        var scriptExecutionContext = scriptContext.CreateExecutionContext("a command").Value;

        scriptExecutionContext.OnOutput += (output) =>
        {
            output.Should().Be("a command");
        };

        var executeResult = await scriptExecutionContext.ExecuteAsync();
        executeResult.IsSuccess.Should().BeTrue();

        scriptExecutionContext.Status.Should().Be(ExecutionStatus.Finished);
    }

    [Test]
    public async Task ICanExecuteADotNetInteractiveScript()
    {
        Guard.IsNotNull(_scriptingService);

        var scriptContextFactory = new DotNetInteractiveContextFactory();

        var registerResult = _scriptingService.RegisterScriptContextFactory(scriptContextFactory);
        registerResult.IsSuccess.Should().BeTrue();

        var scriptContext = _scriptingService.AcquireScriptContext("DotNetInteractive").Value;

        var scriptText = "Console.WriteLine(\"Hello, World!\");";

        var scriptExecutionContext = scriptContext.CreateExecutionContext(scriptText).Value;
        scriptExecutionContext.OnOutput += (output) =>
        {
            output.Should().Be("Hello, World!");
        };

        var executeResult = await scriptExecutionContext.ExecuteAsync();
        executeResult.IsSuccess.Should().BeTrue();
    }
}
