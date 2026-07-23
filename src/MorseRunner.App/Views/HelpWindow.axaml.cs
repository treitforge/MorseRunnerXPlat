using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MorseRunner.App.Views;

public sealed partial class HelpWindow : Window
{
    private readonly string? _copyContent;

    public HelpWindow()
        : this("Help", string.Empty)
    {
    }

    public HelpWindow(string title, string content, string? copyContent = null)
    {
        InitializeComponent();
        Title = title;
        _copyContent = copyContent;
        this.FindControl<TextBlock>("Heading")!.Text = title;
        this.FindControl<TextBlock>("Body")!.Text = content;
        this.FindControl<Button>("CopyButton")!.IsVisible = copyContent is not null;
        Opened += (_, _) =>
            this.FindControl<Button>("CloseButton")?.Focus();
    }

    private void CloseClick(object? sender, RoutedEventArgs args)
    {
        Close();
    }

    private async void CopyClick(object? sender, RoutedEventArgs args)
    {
        if (_copyContent is null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_copyContent);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
