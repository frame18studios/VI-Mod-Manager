using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ViModManager.Core;
using ViModManager.Views;

namespace ViModManager;

public partial class App : Application
{
    private static DataRootLock? _instanceLock;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!DataRootLock.TryAcquire(AppServices.ResolveRoot(), out var lockHandle, out string? reason))
            {
                desktop.MainWindow = BuildAlreadyRunningWindow(desktop, reason);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            _instanceLock = lockHandle;
            var services = new AppServices();
            ThemeManager.Apply(services.Settings.Current);
            desktop.MainWindow = new MainWindow(services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window BuildAlreadyRunningWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        string? reason)
    {
        var close = new Button
        {
            Content = "Close",
            Width = 96,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => desktop.Shutdown();

        return new Window
        {
            Title = "VI Mod Manager",
            Width = 520,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Spacing = 16,
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock
                    {
                        Text = "VI Mod Manager is already running",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = reason ?? "Another instance is using this data folder.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    close,
                },
            },
        };
    }
}
