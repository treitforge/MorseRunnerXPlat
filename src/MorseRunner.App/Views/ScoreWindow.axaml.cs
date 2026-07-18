using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MorseRunner.App.ViewModels;

namespace MorseRunner.App.Views;

public sealed partial class ScoreWindow : Window
{
    public ScoreWindow()
        : this(new ScoreWindowViewModel(0, 0, "CQ WPX", "00:00.000"))
    {
    }

    public ScoreWindow(ScoreWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
