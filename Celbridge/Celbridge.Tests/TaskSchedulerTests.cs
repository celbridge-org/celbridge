using Celbridge.BaseLibrary.Tasks;
using Celbridge.BaseLibrary.Core;
using Celbridge.Services.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Celbridge.BaseLibrary.Logging;
using Celbridge.Services.Logging;

namespace Celbridge.Tests;

[TestFixture]
public class TaskSchedulerTests
{
    private ServiceProvider? _serviceProvider;
    private ISchedulerService? _schedulerService;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        _serviceProvider = services.BuildServiceProvider();

        _schedulerService = _serviceProvider!.GetRequiredService<ISchedulerService>();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider != null)
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }

        _schedulerService = null;
    }

    public class MyTask : ITask
    {
        public bool Completed { get; private set; }

        public async Task<Result> ExecuteAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(10);

            Completed = true;

            return Result.Ok();
        }
    }

    [Test]
    public async Task ICanScheduleAndExecuteATask()
    { 
        var myTask = new MyTask();
        myTask.Completed.Should().BeFalse();

        _schedulerService!.ScheduleTask(myTask);

        while (!myTask.Completed)
        {
            await Task.Delay(100);
        }

        myTask.Completed.Should().BeTrue();
    }

    [Test]
    public async Task ICanScheduleAndExecuteAFunction()
    {
        bool completed = false;

        _schedulerService!.ScheduleFunction(async () =>
        {
            completed = true;

            await Task.CompletedTask;
        });

        while (!completed)
        {
            await Task.Delay(100);
        }

        completed.Should().BeTrue();
    }
}
