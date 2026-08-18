using Microsoft.Windows.AppNotifications.Builder;
using TaskLens.Core;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace TaskLens_App.Services;

public sealed class TaskReminderService
{
    public void Synchronize(IEnumerable<TaskItem> tasks)
    {
        foreach (var task in tasks)
        {
            Schedule(task);
        }
    }

    public void Schedule(TaskItem task)
    {
        Cancel(task.Id);
        if (task.IsCompleted || task.DueAt is null)
        {
            return;
        }

        var reminderAt = task.DueAt.Value.Date.AddHours(9);
        if (reminderAt <= DateTimeOffset.Now)
        {
            return;
        }

        var payload = new AppNotificationBuilder()
            .AddArgument("taskId", task.Id)
            .AddText("Task due today")
            .AddText(task.Title)
            .BuildNotification()
            .Payload;
        var document = new XmlDocument();
        document.LoadXml(payload);
        var notification = new ScheduledToastNotification(document, reminderAt)
        {
            Tag = GetTag(task.Id),
            Group = "TaskLens"
        };
        ToastNotificationManager.CreateToastNotifier().AddToSchedule(notification);
    }

    public void Cancel(string taskId)
    {
        var notifier = ToastNotificationManager.CreateToastNotifier();
        var scheduled = notifier.GetScheduledToastNotifications();
        var tag = GetTag(taskId);
        foreach (var notification in scheduled.Where(item =>
                     item.Tag == tag && item.Group == "TaskLens"))
        {
            notifier.RemoveFromSchedule(notification);
        }
    }

    private static string GetTag(string taskId) =>
        taskId.Length <= 16 ? taskId : taskId[..16];
}
