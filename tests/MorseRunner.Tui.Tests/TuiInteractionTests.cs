using MorseRunner.Domain;

namespace MorseRunner.Tui.Tests;

public sealed class TuiInteractionTests
{
    [Theory]
    [InlineData(ConsoleKey.F1, '\0', ConsoleModifiers.None, TuiActionKind.SendCq)]
    [InlineData(ConsoleKey.F9, '\0', ConsoleModifiers.None, TuiActionKind.StartPileup)]
    [InlineData(ConsoleKey.F9, '\0', ConsoleModifiers.Shift, TuiActionKind.StartSingle)]
    [InlineData(ConsoleKey.F9, '\0', ConsoleModifiers.Control, TuiActionKind.StartHst)]
    [InlineData(ConsoleKey.F10, '\0', ConsoleModifiers.None, TuiActionKind.Stop)]
    [InlineData(ConsoleKey.F11, '\0', ConsoleModifiers.None, TuiActionKind.Wipe)]
    [InlineData(ConsoleKey.F12, '\0', ConsoleModifiers.None, TuiActionKind.SendNumberQuestion)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.None, TuiActionKind.EnterSendMessage)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.Control, TuiActionKind.SaveQso)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.Shift, TuiActionKind.SaveQso)]
    [InlineData(ConsoleKey.Enter, '\r', ConsoleModifiers.Alt, TuiActionKind.SaveQso)]
    [InlineData(ConsoleKey.UpArrow, '\0', ConsoleModifiers.None, TuiActionKind.RitUp)]
    [InlineData(ConsoleKey.UpArrow, '\0', ConsoleModifiers.Control, TuiActionKind.BandwidthUp)]
    [InlineData(ConsoleKey.W, '\u0017', ConsoleModifiers.Control, TuiActionKind.Wipe)]
    [InlineData(ConsoleKey.D2, '\0', ConsoleModifiers.Control, TuiActionKind.ToggleQsb)]
    public void LegacyKeysMapToSemanticActions(
        ConsoleKey key,
        char character,
        ConsoleModifiers modifiers,
        TuiActionKind expected)
    {
        var keyInfo = new ConsoleKeyInfo(
            character,
            key,
            modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt),
            modifiers.HasFlag(ConsoleModifiers.Control));

        Assert.Equal(expected, TuiKeyRouter.Map(keyInfo).Kind);
    }

    [Theory]
    [InlineData(';', TuiActionKind.SendCallAndExchange)]
    [InlineData('.', TuiActionKind.LogQso)]
    [InlineData(',', TuiActionKind.LogQso)]
    [InlineData('+', TuiActionKind.LogQso)]
    [InlineData('[', TuiActionKind.LogQso)]
    [InlineData('?', TuiActionKind.ToggleHelp)]
    public void LegacyPunctuationMapsToSemanticActions(
        char character,
        TuiActionKind expected)
    {
        var keyInfo = new ConsoleKeyInfo(
            character,
            ConsoleKey.Oem1,
            shift: false,
            alt: false,
            control: false);

        Assert.Equal(expected, TuiKeyRouter.Map(keyInfo).Kind);
    }

    [Fact]
    public void RendererAdaptsToCompactAndWideTerminals()
    {
        var state = new TuiState
        {
            Snapshot = new SessionSnapshot(
                Guid.Empty,
                SessionId.New(),
                SessionState.Running,
                1,
                12,
                6144,
                TimeSpan.FromMilliseconds(557),
                12345,
                ContestCatalog.All[0].Id,
                new("rmPileup"),
                "K1ABC",
                1,
                1,
                null,
                ActiveOperatorState: OperatorState.NeedNumber),
            Call = "K1ABC",
            Qsos =
            [
                new Qso
                {
                    Timestamp = DateTimeOffset.UnixEpoch,
                    Call = "K1ABC",
                    Rst = 599,
                    Exchange1 = "123",
                    Points = 1,
                },
            ],
        };

        string compact = TuiRenderer.Render(state, 80, 24);
        string wide = TuiRenderer.Render(state, 140, 40);

        Assert.Contains("K1ABC", compact, StringComparison.Ordinal);
        Assert.Contains("SCORE 1", wide, StringComparison.Ordinal);
        Assert.Contains("CALLER NEED EXCHANGE", wide, StringComparison.Ordinal);
        Assert.False(compact.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.False(wide.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.Equal(24, compact.Split(Environment.NewLine).Length);
        Assert.Equal(40, wide.Split(Environment.NewLine).Length);
        Assert.All(
            compact.Split(Environment.NewLine),
            line => Assert.True(line.Length <= 80));
        Assert.All(
            wide.Split(Environment.NewLine),
            line => Assert.True(line.Length <= 140));
    }

    [Fact]
    public void RendererAddsAnsiStylingOnlyWhenRequested()
    {
        var state = new TuiState();

        string plain = TuiRenderer.Render(state, 100, 28);
        string styled = TuiRenderer.Render(state, 100, 28, useColor: true);

        Assert.DoesNotContain("\u001b[", plain, StringComparison.Ordinal);
        Assert.Contains("\u001b[", styled, StringComparison.Ordinal);
        Assert.False(styled.EndsWith(Environment.NewLine, StringComparison.Ordinal));
    }
}
