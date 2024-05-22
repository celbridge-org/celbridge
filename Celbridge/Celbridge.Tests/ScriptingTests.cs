using Celbridge.BaseLibrary.Scripting;
using Celbridge.Scripting;
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
    public async Task ICanExecuteACSharpScript()
    {
        Guard.IsNotNull(_scriptingService);

        var scriptContext = _scriptingService.CreateScriptContext();

        var scriptText = "Console.WriteLine(\"Hello, World!\");";
        var scriptExecutor = scriptContext.CreateExecutor(scriptText).Value;

        scriptExecutor.OnOutput += (output) =>
        {
            output.Should().Be("Hello, World!");
        };

        var executeResult = await scriptExecutor.ExecuteAsync();
        executeResult.IsSuccess.Should().BeTrue();

        scriptExecutor.Status.Should().Be(ExecutionStatus.Finished);
    }
}
