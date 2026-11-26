using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ViModManager.Views;

public static class Dialogs
{
    public static async Task Info(Window owner, string title, string message)
    {
        var (dialog, body) = Create(owner, title);
        body.Children.Add(Body(message));
        var close = Ui.Btn("OK", dialog.Close, "primary");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.MinWidth = 96;
        body.Children.Add(close);
        await dialog.ShowDialog(owner);
    }

    public static async Task<bool> Confirm(Window owner, string title, string message, string confirmText = "Confirm", bool danger = false)
    {
        var (dialog, body) = Create(owner, title);
        body.Children.Add(Body(message));

        bool confirmed = false;
        var buttons = Ui.HStack(10);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var cancel = Ui.Btn("Cancel", dialog.Close, "ghost");
        var accept = Ui.Btn(confirmText, () =>
        {
            confirmed = true;
            dialog.Close();
        }, danger ? "danger" : "primary");
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        body.Children.Add(buttons);

        await dialog.ShowDialog(owner);
        return confirmed;
    }

    public static async Task<string?> Prompt(Window owner, string title, string label, string initialValue = "")
    {
        var (dialog, body) = Create(owner, title);
        body.Children.Add(Body(label));
        var field = Ui.Field("", initialValue);
        body.Children.Add(field);

        string? result = null;
        var buttons = Ui.HStack(10);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var cancel = Ui.Btn("Cancel", dialog.Close, "ghost");
        var accept = Ui.Btn("Save", () =>
        {
            result = field.Text;
            dialog.Close();
        }, "primary");
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        body.Children.Add(buttons);

        await dialog.ShowDialog(owner);
        return result;
    }

    private static (Window Dialog, StackPanel Body) Create(Window owner, string title)
    {
        var body = new StackPanel { Spacing = 16 };
        var header = Ui.VStack(6);
        header.Children.Add(Ui.Text(title, "h2", true));
        header.Children.Add(Ui.AccentRule(2, 0));
        body.Children.Add(header);

        var card = new Border
        {
            Child = body,
            Margin = new Thickness(16),
        };
        card.Classes.Add("panel-soft");

        var dialog = new Window
        {
            Title = title,
            Width = 490,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Ui.Brush("BgBrush"),
            Content = card,
        };
        return (dialog, body);
    }

    private static TextBlock Body(string message) => new()
    {
        Text = message,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Ui.Brush("TextBrush"),
        LineHeight = 20,
    };
}
