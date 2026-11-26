using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ViModManager.Views;

namespace ViModManager;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices();
            ThemeManager.Apply(services.Settings.Current);
            desktop.MainWindow = new MainWindow(services);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
