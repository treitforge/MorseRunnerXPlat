using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using MorseRunner.App.ViewModels;

namespace MorseRunner.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
        : this(
            new MainWindowViewModel(
                MorseRunner.Client.InProcessMorseRunnerClient.CreateDefault()))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Opened += (_, _) => CallEntryBox.Focus();
        viewModel.ShowScoreRequested += ShowScore;
        Closed += async (_, _) => await viewModel.DisposeAsync();
    }

    private void ShowScore(object? sender, ScoreSummaryEventArgs args)
    {
        var window = new ScoreWindow(
            new ScoreWindowViewModel(
                args.Score,
                args.QsoCount,
                args.Contest,
                args.Elapsed));
        _ = window.ShowDialog(this);
    }

    private void CallTextInput(object? sender, TextInputEventArgs args)
    {
        FilterTextInput(
            sender,
            args,
            character => char.IsAsciiLetterOrDigit(character)
                || character is '/' or '?',
            character => char.ToUpperInvariant(character));
    }

    private void RstTextInput(object? sender, TextInputEventArgs args)
    {
        FilterTextInput(
            sender,
            args,
            char.IsAsciiDigit,
            character => char.ToUpperInvariant(character) switch
            {
                'A' => '1',
                'E' => '5',
                'N' => '9',
                char value => value,
            });
    }

    private void Exchange1TextInput(object? sender, TextInputEventArgs args)
    {
        FilterTextInput(
            sender,
            args,
            character => char.IsAsciiLetterOrDigit(character),
            char.ToUpperInvariant);
    }

    private void Exchange2TextInput(object? sender, TextInputEventArgs args)
    {
        FilterTextInput(
            sender,
            args,
            character => char.IsAsciiLetterOrDigit(character)
                || character is '/' or ' ',
            character => char.ToUpperInvariant(character) switch
            {
                'A' => '1',
                'N' => '9',
                'O' or 'T' => '0',
                char value => value,
            });
    }

    private static void FilterTextInput(
        object? sender,
        TextInputEventArgs args,
        Func<char, bool> isAllowed,
        Func<char, char> transform)
    {
        if (sender is not TextBox textBox
            || string.IsNullOrEmpty(args.Text)
            || args.Text.Length != 1)
        {
            return;
        }

        char value = transform(args.Text[0]);
        if (!isAllowed(value))
        {
            args.Handled = true;
            return;
        }

        if (value == args.Text[0])
        {
            return;
        }

        int start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        int end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        string current = textBox.Text ?? string.Empty;
        textBox.Text = current.Remove(start, end - start).Insert(start, value.ToString());
        textBox.CaretIndex = start + 1;
        args.Handled = true;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
