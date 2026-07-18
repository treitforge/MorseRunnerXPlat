using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MorseRunner.App.Views;

public sealed partial class HelpWindow : Window
{
    public HelpWindow()
        : this("Help", string.Empty)
    {
    }

    public HelpWindow(string title, string content)
    {
        InitializeComponent();
        Title = title;
        this.FindControl<TextBlock>("Heading")!.Text = title;
        this.FindControl<TextBlock>("Body")!.Text = content;
        Opened += (_, _) =>
            this.FindControl<Button>("CloseButton")?.Focus();
    }

    private void CloseClick(object? sender, RoutedEventArgs args)
    {
        Close();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
