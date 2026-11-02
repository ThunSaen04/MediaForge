using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Threading;
using MediaForge.Core;
using MediaForge.Localization;
using MediaForge.UI;
using MediaForge.ViewModels;

namespace MediaForge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        // Every <Hyperlink NavigateUri="..."> in the app opens in the default browser.
        EventManager.RegisterClassHandler(typeof(Hyperlink), Hyperlink.RequestNavigateEvent,
            new RequestNavigateEventHandler((_, args) =>
            {
                Dialogs.OpenUrl(args.Uri.AbsoluteUri);
                args.Handled = true;
            }));

        var settings = AppSettings.Load();
        Loc.Instance.Language = settings.Language;
        ThemeHelper.Apply(settings.Theme);

        var window = new MainWindow { DataContext = new MainViewModel(settings) };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AudioPreview.DisposeAll();
        base.OnExit(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialogs.Error(e.Exception.Message);
        e.Handled = true;
    }
}
