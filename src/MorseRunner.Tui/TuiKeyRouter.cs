namespace MorseRunner.Tui;

public static class TuiKeyRouter
{
    public static TuiAction Map(ConsoleKeyInfo key)
    {
        bool control = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        bool shift = key.Modifiers.HasFlag(ConsoleModifiers.Shift);
        bool alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);

        if (key.Key == ConsoleKey.Enter && (control || shift || alt))
        {
            return new(TuiActionKind.SaveQso);
        }

        if (control)
        {
            return key.Key switch
            {
                ConsoleKey.Q => new(TuiActionKind.Quit),
                ConsoleKey.W => new(TuiActionKind.Wipe),
                ConsoleKey.P => new(TuiActionKind.Pause),
                ConsoleKey.R => new(TuiActionKind.Resume),
                ConsoleKey.S => new(TuiActionKind.ToggleSettings),
                ConsoleKey.T => new(TuiActionKind.ToggleResults),
                ConsoleKey.G => new(TuiActionKind.ToggleDiagnostics),
                ConsoleKey.A => new(TuiActionKind.ToggleRecording),
                ConsoleKey.E when shift =>
                    new(TuiActionKind.ExportCabrillo),
                ConsoleKey.E => new(TuiActionKind.ExportJson),
                ConsoleKey.O => new(TuiActionKind.OpenRecording),
                ConsoleKey.LeftArrow => new(TuiActionKind.PreviousContest),
                ConsoleKey.RightArrow => new(TuiActionKind.NextContest),
                ConsoleKey.D => new(TuiActionKind.NextDuration),
                ConsoleKey.D1 => new(TuiActionKind.ToggleQsk),
                ConsoleKey.D2 => new(TuiActionKind.ToggleQsb),
                ConsoleKey.D3 => new(TuiActionKind.ToggleQrm),
                ConsoleKey.D4 => new(TuiActionKind.ToggleQrn),
                ConsoleKey.D5 => new(TuiActionKind.ToggleFlutter),
                ConsoleKey.D6 => new(TuiActionKind.ToggleLids),
                ConsoleKey.UpArrow => new(TuiActionKind.BandwidthUp),
                ConsoleKey.DownArrow => new(TuiActionKind.BandwidthDown),
                ConsoleKey.F9 => new(TuiActionKind.StartHst),
                _ => new(TuiActionKind.None),
            };
        }

        if (alt)
        {
            return key.Key switch
            {
                ConsoleKey.LeftArrow => new(TuiActionKind.PreviousRunMode),
                ConsoleKey.RightArrow => new(TuiActionKind.NextRunMode),
                _ => new(TuiActionKind.None),
            };
        }

        if (shift && key.Key == ConsoleKey.F9)
        {
            return new(TuiActionKind.StartSingle);
        }

        return key.Key switch
        {
            ConsoleKey.F1 => new(TuiActionKind.SendCq),
            ConsoleKey.F2 => new(TuiActionKind.SendExchange),
            ConsoleKey.F3 => new(TuiActionKind.SendThankYou),
            ConsoleKey.F4 => new(TuiActionKind.SendMyCall),
            ConsoleKey.F5 => new(TuiActionKind.SendHisCall),
            ConsoleKey.F6 => new(TuiActionKind.SendBefore),
            ConsoleKey.F7 => new(TuiActionKind.SendQuestion),
            ConsoleKey.F8 => new(TuiActionKind.SendNil),
            ConsoleKey.F9 => new(TuiActionKind.StartPileup),
            ConsoleKey.F10 => new(TuiActionKind.Stop),
            ConsoleKey.F11 => new(TuiActionKind.Wipe),
            ConsoleKey.F12 => new(TuiActionKind.SendNumberQuestion),
            ConsoleKey.Insert => new(TuiActionKind.SendCallAndExchange),
            ConsoleKey.Enter => new(TuiActionKind.EnterSendMessage),
            ConsoleKey.Escape => new(TuiActionKind.Abort),
            ConsoleKey.UpArrow => new(TuiActionKind.RitUp),
            ConsoleKey.DownArrow => new(TuiActionKind.RitDown),
            ConsoleKey.LeftArrow => new(TuiActionKind.DecreaseSetting),
            ConsoleKey.RightArrow => new(TuiActionKind.IncreaseSetting),
            ConsoleKey.PageUp => new(TuiActionKind.SpeedUp),
            ConsoleKey.PageDown => new(TuiActionKind.SpeedDown),
            ConsoleKey.Tab when shift => new(TuiActionKind.PreviousField),
            ConsoleKey.Tab => new(TuiActionKind.NextField),
            ConsoleKey.Backspace => new(TuiActionKind.Backspace),
            _ when key.KeyChar is ';' =>
                new(TuiActionKind.SendCallAndExchange),
            _ when key.KeyChar is '.' or ',' or '+' or '[' =>
                new(TuiActionKind.LogQso),
            _ when key.KeyChar is '?' => new(TuiActionKind.ToggleHelp),
            _ when !Char.IsControl(key.KeyChar) =>
                new(TuiActionKind.InsertCharacter, key.KeyChar),
            _ => new(TuiActionKind.None),
        };
    }
}
