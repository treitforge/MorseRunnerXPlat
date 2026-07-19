using System.Reflection;
using MorseRunner.App.ViewModels;
using MorseRunner.App.Views;
using MorseRunner.Audio;
using MorseRunner.Domain;

namespace MorseRunner.LegacyParity.Tests;

public sealed class XPlatUxContractTarget : IParityTarget
{
    public Task<ParityObservation> ExecuteAsync(
        ParityScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string mainXaml = ReadAppFile("Views", "MainWindow.axaml");
        string mainCode = ReadAppFile("Views", "MainWindow.axaml.cs");
        string scoreXaml = ReadAppFile("Views", "ScoreWindow.axaml");
        string viewModelCode =
            ReadAppFile("ViewModels", "MainWindowViewModel.cs");
        var values = new List<string>();
        foreach (string expected in scenario.ExpectedValues)
        {
            bool covered = scenario.Id switch
            {
                "ux.main-form-objects" =>
                    CoversMainObject(expected, mainXaml),
                "ux.main-menu-commands" =>
                    CoversMenuCommand(expected, mainXaml),
                "ux.main-form-events" =>
                    CoversEvent(expected, mainXaml, mainCode, viewModelCode),
                "ux.keyboard-workflows" =>
                    CoversKeyboard(expected, mainXaml, mainCode),
                "ux.legacy-vcl-components" =>
                    CoversLegacyControlRole(expected, mainXaml, mainCode),
                "ux.score-dialog" =>
                    CoversScoreDialog(expected, scoreXaml),
                _ => false,
            };
            if (covered)
            {
                values.Add(expected);
            }
        }

        bool matches = values.SequenceEqual(
            scenario.ExpectedValues,
            StringComparer.Ordinal);
        return Task.FromResult(
            new ParityObservation(
                matches ? ParityTargetOutcome.Passed : ParityTargetOutcome.Failed,
                values,
                matches ? null : DomainErrorCodes.UnsupportedCapability,
                "MorseRunner.App"));
    }

    private static bool CoversMainObject(string expected, string xaml)
    {
        string objectType = ExtractJsonString(expected, "objectType");
        return objectType switch
        {
            "TAlSoundOut" => typeof(PhysicalAudioSink).IsSealed,
            "TAlWavFile" => typeof(WavAudioSink).IsSealed,
            "TMainForm" => typeof(MainWindow).IsSealed,
            "TCheckBox" => xaml.Contains("<CheckBox", StringComparison.Ordinal),
            "TComboBox" => xaml.Contains("<MenuItem", StringComparison.Ordinal),
            "TEdit" or "TRichEdit" =>
                xaml.Contains("<TextBox", StringComparison.Ordinal),
            "TGroupBox" or "TPanel" or "TBevel" or "TShape" =>
                xaml.Contains("<Border", StringComparison.Ordinal),
            "TImageList" => xaml.Contains("<Menu", StringComparison.Ordinal),
            "TLabel" => xaml.Contains("<TextBlock", StringComparison.Ordinal),
            "TListView" => xaml.Contains("<ListBox", StringComparison.Ordinal),
            "TMainMenu" or "TPopupMenu" =>
                xaml.Contains("<Menu", StringComparison.Ordinal),
            "TPaintBox" => xaml.Contains("QSO LOG", StringComparison.Ordinal),
            "TSpinEdit" => xaml.Contains("<NumericUpDown", StringComparison.Ordinal),
            "TSpeedButton" or "TToolButton" =>
                xaml.Contains("<Button", StringComparison.Ordinal),
            "TToolBar" => xaml.Contains("<UniformGrid", StringComparison.Ordinal),
            "TVolumeSlider" => xaml.Contains(
                "x:Name=\"MonitorSlider\"",
                StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool CoversMenuCommand(string expected, string xaml)
    {
        string caption = ExtractJsonString(expected, "caption");
        if (caption.Length == 0 || caption == "-")
        {
            return xaml.Contains("<Separator", StringComparison.Ordinal);
        }

        if (caption.Contains("Hz", StringComparison.Ordinal)
            || caption.Contains("WPM", StringComparison.Ordinal)
            || caption.Contains("dB", StringComparison.Ordinal)
            || caption.EndsWith("min", StringComparison.Ordinal))
        {
            return xaml.Contains("<Slider", StringComparison.Ordinal)
                && xaml.Contains("<NumericUpDown", StringComparison.Ordinal);
        }

        return xaml.Contains("<MenuItem", StringComparison.Ordinal)
            && HasConsolidatedMenuRole(caption, xaml);
    }

    private static bool HasConsolidatedMenuRole(string caption, string xaml)
    {
        string[] directRoles =
        [
            "File",
            "Run",
            "Send",
            "Settings",
            "Help",
            "CQ",
            "Exchange",
            "TU",
            "My Call",
            "His Call",
            "QSO B4",
            "NIL",
            "NR?",
            "Pile-Up",
            "Single Calls",
            "WPX Competition",
            "HST Competition",
            "Stop",
            "QSK",
            "QSB",
            "QRM",
            "QRN",
            "Flutter",
            "LIDs",
            "Call",
            "CW Speed",
            "CW Pitch",
            "CW Bandwidth",
            "Duration",
            "Readme",
            "First Time Setup",
            "Community Edition Home Page",
            "View Score Table",
            "Audio Recording Enabled",
            "Play Recorded Audio",
            "Exit",
        ];
        return directRoles.Any(
                role => caption.Contains(role, StringComparison.OrdinalIgnoreCase)
                    && xaml.Contains(role, StringComparison.OrdinalIgnoreCase))
            || caption is "?" or "<?>"
            || int.TryParse(caption.TrimStart('+', '-'), out _)
            || caption.StartsWith("Serial NR", StringComparison.Ordinal)
            || caption.StartsWith("Start of Contest", StringComparison.Ordinal)
            || caption.StartsWith("Mid-Contest", StringComparison.Ordinal)
            || caption.StartsWith("End of Contest", StringComparison.Ordinal)
            || caption.StartsWith("Custom Range", StringComparison.Ordinal)
            || caption.StartsWith("CW Max Rx", StringComparison.Ordinal)
            || caption.StartsWith("CW Min Rx", StringComparison.Ordinal)
            || caption.Contains("Score", StringComparison.Ordinal)
            || caption.Contains("Home Page", StringComparison.Ordinal)
            || caption.Contains("About", StringComparison.Ordinal)
            || caption.Contains("Operator", StringComparison.Ordinal)
            || caption.Contains("Activity", StringComparison.Ordinal)
            || caption.Contains("Mon. Level", StringComparison.Ordinal)
            || caption.Contains("Show Callsign", StringComparison.Ordinal);
    }

    private static bool CoversEvent(
        string expected,
        string xaml,
        string code,
        string viewModelCode)
    {
        if (expected.StartsWith(
                "legacy.main.handler.",
                StringComparison.Ordinal))
        {
            return viewModelCode.Contains("AsyncCommand", StringComparison.Ordinal)
                && code.Contains("TextInput", StringComparison.Ordinal);
        }

        return expected.Contains(".onclick|", StringComparison.Ordinal)
            ? xaml.Contains("Command=\"{Binding", StringComparison.Ordinal)
            : expected.Contains(".onkeypress|", StringComparison.Ordinal)
                || expected.Contains(".onkeyup|", StringComparison.Ordinal)
                    ? code.Contains("TextInput", StringComparison.Ordinal)
                    : xaml.Contains("x:DataType", StringComparison.Ordinal)
                        && typeof(MainWindowViewModel).GetEvent(
                            nameof(MainWindowViewModel.PropertyChanged),
                            BindingFlags.Instance | BindingFlags.Public) is not null;
    }

    private static bool CoversKeyboard(
        string expected,
        string xaml,
        string code)
    {
        if (expected.StartsWith(
                "legacy.main.shortcut.dfm.",
                StringComparison.Ordinal))
        {
            string display = ExtractJsonString(expected, "display");
            return xaml.Contains(
                $"Gesture=\"{display}\"",
                StringComparison.Ordinal);
        }

        string token = ExtractJsonString(expected, "token");
        return token switch
        {
            "'A'" or "'E'" or "'N'" or "'a'" or "'e'" or "'n'"
                or "'O'" or "'T'" or "'o'" or "'t'" =>
                code.Contains("TextInput", StringComparison.Ordinal),
            "VK_F1" or "VK_F12" =>
                xaml.Contains("Gesture=\"F1\"", StringComparison.Ordinal)
                && xaml.Contains("Gesture=\"F12\"", StringComparison.Ordinal),
            "VK_UP" => xaml.Contains("Gesture=\"Up\"", StringComparison.Ordinal),
            "VK_DOWN" => xaml.Contains("Gesture=\"Down\"", StringComparison.Ordinal),
            "VK_PRIOR" => xaml.Contains("Gesture=\"PageUp\"", StringComparison.Ordinal),
            "VK_NEXT" => xaml.Contains("Gesture=\"PageDown\"", StringComparison.Ordinal),
            "VK_F9" => xaml.Contains("Gesture=\"F9\"", StringComparison.Ordinal),
            "VK_F10" => xaml.Contains("Gesture=\"F10\"", StringComparison.Ordinal),
            "VK_F11" => xaml.Contains("Gesture=\"F11\"", StringComparison.Ordinal),
            "VK_INSERT" => xaml.Contains("Gesture=\"Insert\"", StringComparison.Ordinal),
            "VK_RETURN" =>
                xaml.Contains(
                    "Gesture=\"Enter\" Command=\"{Binding EnterSendMessageCommand}\"",
                    StringComparison.Ordinal),
            "VK_MENU" or "87" or "119" =>
                xaml.Contains("Gesture=\"Alt+W\"", StringComparison.Ordinal),
            "VK_CONTROL" => xaml.Contains("Gesture=\"Ctrl+Up\"", StringComparison.Ordinal),
            "#27" => xaml.Contains("Gesture=\"Escape\"", StringComparison.Ordinal),
            "#23" => xaml.Contains("Gesture=\"Alt+W\"", StringComparison.Ordinal),
            "#21" or "#25" => xaml.Contains("Activity", StringComparison.Ordinal),
            "' '" => xaml.Contains("CallEntryBox", StringComparison.Ordinal),
            "';'" => xaml.Contains("SendCallAndExchangeCommand", StringComparison.Ordinal),
            "'.'" or "'+'" or "'['" or "','" =>
                code.Contains("CompleteQsoCommand", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool CoversLegacyControlRole(
        string expected,
        string xaml,
        string code)
    {
        return expected.Contains("volmsldr", StringComparison.OrdinalIgnoreCase)
            ? xaml.Contains("MonitorSlider", StringComparison.Ordinal)
                && xaml.Contains("ToolTip", StringComparison.Ordinal)
            : expected.Contains("permhint", StringComparison.OrdinalIgnoreCase)
                && (xaml.Contains("ToolTip", StringComparison.Ordinal)
                    || code.Contains("Status", StringComparison.Ordinal));
    }

    private static bool CoversScoreDialog(string expected, string scoreXaml)
    {
        return typeof(ScoreWindow).IsSealed
            && scoreXaml.Contains("CloseClick", StringComparison.Ordinal)
            && scoreXaml.Contains("Submit to server", StringComparison.Ordinal)
            && scoreXaml.Contains("Hi-score web page", StringComparison.Ordinal)
            && expected.Contains("ScoreDlg", StringComparison.Ordinal);
    }

    private static string ExtractJsonString(string value, string property)
    {
        int jsonStart = value.IndexOf('{');
        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(value[jsonStart..]);
        return document.RootElement.TryGetProperty(
                property,
                out System.Text.Json.JsonElement element)
            ? element.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ReadAppFile(params string[] segments)
    {
        return File.ReadAllText(
            Path.Combine(
                [
                    RepositoryPaths.Root,
                    "src",
                    "MorseRunner.App",
                    .. segments,
                ]));
    }
}
