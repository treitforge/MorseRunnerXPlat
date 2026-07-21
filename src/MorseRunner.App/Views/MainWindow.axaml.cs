using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MorseRunner.App.ViewModels;
using MorseRunner.Domain;
using MorseRunner.Infrastructure;

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
        Opened += async (_, _) =>
        {
            await viewModel.InitializeAsync();
            this.FindControl<TextBox>("CallEntryBox")?.Focus();
        };
        viewModel.ShowScoreRequested += ShowScore;
        viewModel.EntryFocusRequested += FocusEntry;
        Closed += async (_, _) => await viewModel.DisposeAsync();
    }

    private MainWindowViewModel ViewModel =>
        (MainWindowViewModel)DataContext!;

    private void ShowScore(object? sender, ScoreSummaryEventArgs args)
    {
        var window = new ScoreWindow(
            new ScoreWindowViewModel(
                args.Score,
                args.QsoCount,
                args.QsoRatePerHour,
                args.HighScore,
                args.Contest,
                args.Elapsed));
        _ = window.ShowDialog(this);
    }

    private void FocusEntry(
        object? sender,
        EntryFocusRequestedEventArgs args)
    {
        string controlName = args.Target switch
        {
            EntryFocusTarget.Call => "CallEntryBox",
            EntryFocusTarget.Rst => "RstEntryBox",
            EntryFocusTarget.Exchange1 => "Exchange1EntryBox",
            EntryFocusTarget.Exchange2 => "Exchange2EntryBox",
            _ => throw new InvalidOperationException(
                $"Unknown entry focus target '{args.Target}'."),
        };
        TextBox? entry = this.FindControl<TextBox>(controlName);
        entry?.Focus();
        if (entry is not null && args.SelectQuestionMark)
        {
            int questionMark = (entry.Text ?? String.Empty).IndexOf(
                '?',
                StringComparison.Ordinal);
            if (questionMark >= 0)
            {
                entry.SelectionStart = questionMark;
                entry.SelectionEnd = questionMark + 1;
            }
        }
    }

    private void ExitClick(object? sender, RoutedEventArgs args)
    {
        Close();
    }

    private void FocusSetupClick(object? sender, RoutedEventArgs args)
    {
        this.FindControl<TextBox>("StationCallBox")?.Focus();
    }

    private async void MonitorLevelChanged(
        object? sender,
        RangeBaseValueChangedEventArgs args)
    {
        await ViewModel.SetMonitorLevelAsync(args.NewValue);
    }

    private void KeyboardHelpClick(object? sender, RoutedEventArgs args)
    {
        _ = new HelpWindow(
            "Keyboard reference",
            """
            F1 CQ                 F7 ?
            F2 Exchange           F8 NIL
            F3 TU                 F9 Start
            F4 My call            Shift+F9 Single calls
            F5 His call           Ctrl+F9 HST
            F6 QSO B4             F10 Stop
            F11 Wipe              F12 NR?

            Enter sends the next QSO message and logs only after validation.
            Insert or semicolon sends the caller's
            call and exchange. Period, comma, plus, or left bracket sends
            TU and logs. Space moves through the entry fields.

            Up/Down adjusts RIT. Ctrl+Up/Down adjusts bandwidth.
            Page Up/Page Down adjusts CW speed. Escape aborts sending.
            Ctrl+W clears the entry fields. Ctrl+P pauses and Ctrl+R resumes.
            """).ShowDialog(this);
    }

    private void FirstTimeSetupClick(object? sender, RoutedEventArgs args)
    {
        _ = new HelpWindow(
            "First time setup",
            """
            1. Choose the contest, run mode, and session duration.
            2. Enter your station callsign.
            3. Set CW speed, pitch, bandwidth, monitor level, and conditions.
            4. Enable audio recording if you want a WAV review file.
            5. Press F9 for Pile-Up, Shift+F9 for Single Calls, or Ctrl+F9
               for HST Competition.

            The callsign entry receives focus when the window opens. The full
            operating workflow is available from the keyboard. Open Keyboard
            reference from this menu for the complete map.
            """).ShowDialog(this);
    }

    private void ReadmeClick(object? sender, RoutedEventArgs args)
    {
        using TextReader reader =
            new PackagedDataCatalog().OpenTextRequired("Readme.txt");
        _ = new HelpWindow(
            "MorseRunner readme",
            reader.ReadToEnd()).ShowDialog(this);
    }

    private void CommunityPageClick(object? sender, RoutedEventArgs args)
    {
        Process.Start(
            new ProcessStartInfo(
                "https://github.com/w7sst/MorseRunner#readme")
            {
                UseShellExecute = true,
            });
    }

    private void AboutClick(object? sender, RoutedEventArgs args)
    {
        _ = new HelpWindow(
            "About MorseRunnerXPlat",
            """
            MorseRunnerXPlat

            A cross-platform Morse contest simulator built on one deterministic
            .NET engine. Avalonia, terminal, command-line, and optional gRPC
            clients share the same commands, events, snapshots, scoring, and
            audio-rendering behavior.

            The application preserves the keyboard-first MorseRunner workflow
            while providing accessible controls and platform-native operation
            on Windows, Linux, and macOS.
            """).ShowDialog(this);
    }

    private void WindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyModifiers == KeyModifiers.None
            && args.Key is Key.OemSemicolon)
        {
            _ = ViewModel.SendCallAndExchangeCommand.ExecuteAsync(null);
            args.Handled = true;
            return;
        }

        if (args.KeyModifiers == KeyModifiers.None
            && args.Key is Key.OemPeriod
                or Key.OemComma
                or Key.OemPlus
                or Key.OemOpenBrackets)
        {
            _ = ViewModel.CompleteQsoCommand.ExecuteAsync(null);
            args.Handled = true;
            return;
        }

        if (args.KeyModifiers == KeyModifiers.None && args.Key == Key.Space)
        {
            Control? focused = FocusManager?.GetFocusedElement() as Control;
            Control? next = focused?.Name switch
            {
                "CallEntryBox" => this.FindControl<TextBox>("RstEntryBox"),
                "RstEntryBox" => this.FindControl<TextBox>("Exchange1EntryBox"),
                "Exchange1EntryBox" => this.FindControl<TextBox>("Exchange2EntryBox"),
                "Exchange2EntryBox" => this.FindControl<TextBox>("CallEntryBox"),
                _ => null,
            };
            if (next is not null)
            {
                next.Focus();
                if (next is TextBox textBox)
                {
                    textBox.SelectAll();
                }
                args.Handled = true;
            }
        }
    }

    private static void EntryGotFocus(
        object? sender,
        RoutedEventArgs args)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
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
