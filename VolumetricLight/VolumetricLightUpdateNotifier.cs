using System.Windows;
using GitHubUpdateNotifier;
using YukkuriMovieMaker.Plugin.Update;
using Notifier = GitHubUpdateNotifier.GitHubUpdateNotifier;

namespace VolumetricLight;

internal static class VolumetricLightUpdateNotifier
{
    private const string Owner = "routersys";
    private const string Repository = "YMM4-VolumetricLight";
    private static int _started;

    public static void EnsureCheckedOnce()
    {
        if (Application.Current is null || Interlocked.Exchange(ref _started, 1) != 0)
            return;
        _ = RunAsync();
    }

    private static async Task RunAsync()
    {
        try
        {
            var options = new UpdateNotifierOptions
            {
                CurrentVersion = PluginVersion.FromAssemblyInformationalVersion(typeof(VolumetricLightUpdateNotifier)),
                IncludePrerelease = false,
                MessageFormatter = FormatMessage,
                NotificationHandler = ShowNotificationAsync,
            };
            await new Notifier(Owner, Repository, options).NotifyAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is not UpdateCheckException)
                VolumetricLightTelemetry.Report(exception);
        }
    }

    private static string FormatMessage(UpdateInfo update)
        => string.Format(Texts.UpdateAvailableMessage, update.TagName) + Environment.NewLine + update.ReleaseUrl;

    private static Task ShowNotificationAsync(UpdateNotification notification, CancellationToken cancellationToken)
    {
        var application = Application.Current;
        if (application is null)
            return Task.CompletedTask;
        return application.Dispatcher.InvokeAsync(() =>
            MessageBox.Show(notification.Message, Texts.VolumetricLight, MessageBoxButton.OK, MessageBoxImage.Information)).Task;
    }
}
