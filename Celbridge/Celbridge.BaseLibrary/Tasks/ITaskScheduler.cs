namespace Celbridge.BaseLibrary.Tasks;

public interface ITaskScheduler
{
    void ScheduleTaskGroup(ITaskGroup taskGroup);
}