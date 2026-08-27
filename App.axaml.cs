using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hugoer.Services;
using Hugoer.ViewModels;
using Hugoer.Views;

namespace Hugoer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Compose the runtime once at the application seam. Page models
            // receive this graph explicitly, which keeps the desktop lifetime
            // in charge of the shared session and service instances.
            var services = AppServices.Instance;
            var viewModel = new MainViewModel(services);
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.Exit += (_, _) => viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
