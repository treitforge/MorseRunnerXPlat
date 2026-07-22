namespace MorseRunner.Dsp;

public sealed class MorseEnvelopeCursor
{
    private readonly MorseKeyingProfile _profile;
    private MorseSymbolEnumerator _symbols;
    private int _samplesPerCharacterUnit;
    private int _samplesPerAdjustedUnit;
    private float _amplitude;
    private char _currentSymbol;
    private int _currentSymbolPosition;
    private int _currentSymbolSampleCount;

    public MorseEnvelopeCursor(
        MorseKeyingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
    }

    public MorseKeyingProfile Profile => _profile;

    public int SendingWordsPerMinute { get; private set; }

    public int CharacterWordsPerMinute { get; private set; }

    public int TrueEnvelopeSampleCount { get; private set; }

    public int PaddedEnvelopeSampleCount { get; private set; }

    public int SendPosition { get; private set; }

    public int RemainingPaddedSampleCount =>
        PaddedEnvelopeSampleCount - SendPosition;

    public int RemainingBlockCount =>
        RemainingPaddedSampleCount / _profile.BlockSize;

    public bool HasPendingAudio =>
        SendPosition < PaddedEnvelopeSampleCount;

    public void Reset(
        MorseText text,
        int wordsPerMinute,
        float amplitude)
    {
        MorseKeyingProfile.ValidateQrmWordsPerMinute(
            wordsPerMinute);
        if (!float.IsFinite(amplitude) || amplitude < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(amplitude));
        }

        SendingWordsPerMinute = wordsPerMinute;
        CharacterWordsPerMinute = wordsPerMinute;
        _amplitude = amplitude;
        CalculateUnitWidths(wordsPerMinute);

        _symbols = new(text, _profile.Mode);
        TrueEnvelopeSampleCount = CountTrueSamples(_symbols);
        PaddedEnvelopeSampleCount = checked(
            (TrueEnvelopeSampleCount + _profile.BlockSize - 1)
            / _profile.BlockSize
            * _profile.BlockSize);

        _symbols = new(text, _profile.Mode);
        SendPosition = 0;
        _currentSymbol = default;
        _currentSymbolPosition = 0;
        _currentSymbolSampleCount = 0;
    }

    public bool TryRenderNextBlock(Span<float> destination)
    {
        if (destination.Length != _profile.BlockSize)
        {
            throw new ArgumentException(
                "The destination must contain exactly one audio block.",
                nameof(destination));
        }

        if (!HasPendingAudio)
        {
            destination.Clear();
            return false;
        }

        Render(destination);
        return true;
    }

    public void Render(Span<float> destination)
    {
        if (destination.Length > RemainingPaddedSampleCount)
        {
            throw new ArgumentException(
                "The destination exceeds the remaining padded "
                + "envelope.",
                nameof(destination));
        }

        int destinationIndex = 0;
        while (destinationIndex < destination.Length
               && SendPosition < TrueEnvelopeSampleCount)
        {
            if (_currentSymbolPosition
                == _currentSymbolSampleCount)
            {
                LoadNextSymbol();
            }

            destination[destinationIndex] =
                RenderCurrentSymbolSample() * _amplitude;
            destinationIndex++;
            SendPosition++;
            _currentSymbolPosition++;
        }

        if (destinationIndex < destination.Length)
        {
            destination[destinationIndex..].Clear();
            SendPosition += destination.Length - destinationIndex;
        }
    }

    private void CalculateUnitWidths(int wordsPerMinute)
    {
        if (_profile.Mode == MorseKeyingMode.Standard)
        {
            _samplesPerCharacterUnit = (int)Math.Round(
                60d / 48d
                * _profile.SampleRate
                / wordsPerMinute,
                MidpointRounding.ToEven);
            _samplesPerAdjustedUnit = 0;
            return;
        }

        _samplesPerCharacterUnit = (int)Math.Round(
            60d
            * _profile.SampleRate
            / wordsPerMinute
            / 48d,
            MidpointRounding.ToEven);
        float delayPerWord = (float)(
            (60d / wordsPerMinute)
            - (31d * 60d / wordsPerMinute / 48d));
        _samplesPerAdjustedUnit = (int)MathF.Round(
            delayPerWord * _profile.SampleRate / 17f,
            MidpointRounding.ToEven);
    }

    private int CountTrueSamples(MorseSymbolEnumerator symbols)
    {
        int sampleCount = 0;
        while (symbols.MoveNext(out char symbol))
        {
            sampleCount = checked(
                sampleCount + GetSymbolSampleCount(symbol));
        }

        return sampleCount;
    }

    private int GetSymbolSampleCount(char symbol) =>
        _profile.Mode switch
        {
            MorseKeyingMode.Standard => symbol switch
            {
                '.' => checked(2 * _samplesPerCharacterUnit),
                '-' => checked(4 * _samplesPerCharacterUnit),
                ' ' => checked(2 * _samplesPerCharacterUnit),
                '~' => checked(3 * _samplesPerCharacterUnit),
                _ => throw new InvalidOperationException(
                    "The standard Morse stream contains an invalid "
                    + "symbol."),
            },
            MorseKeyingMode.SstFarnsworth => symbol switch
            {
                '.' => checked(2 * _samplesPerCharacterUnit),
                '-' => checked(4 * _samplesPerCharacterUnit),
                '^' => CalculateAdjustedSpaceSampleCount(3, true),
                '_' => CalculateAdjustedSpaceSampleCount(5, true),
                '~' => CalculateAdjustedSpaceSampleCount(4, true),
                ' ' => CalculateAdjustedSpaceSampleCount(5, false),
                _ => throw new InvalidOperationException(
                    "The SST Morse stream contains an invalid "
                    + "symbol."),
            },
            _ => throw new InvalidOperationException(
                "The Morse keying mode is invalid."),
        };

    private int CalculateAdjustedSpaceSampleCount(
        int durationInUnits,
        bool subtractPriorCharacterUnit)
    {
        int sampleCount = checked(
            durationInUnits * _samplesPerAdjustedUnit);
        return subtractPriorCharacterUnit
            ? checked(sampleCount - _samplesPerCharacterUnit)
            : sampleCount;
    }

    private void LoadNextSymbol()
    {
        if (!_symbols.MoveNext(out _currentSymbol))
        {
            throw new InvalidOperationException(
                "The Morse symbol stream ended before its declared "
                + "true sample count.");
        }

        _currentSymbolPosition = 0;
        _currentSymbolSampleCount =
            GetSymbolSampleCount(_currentSymbol);
    }

    private float RenderCurrentSymbolSample()
    {
        if (_currentSymbol is not ('.' or '-'))
        {
            return 0f;
        }

        int markSampleCount = _currentSymbol == '.'
            ? _samplesPerCharacterUnit
            : checked(3 * _samplesPerCharacterUnit);
        if (_currentSymbolPosition < _profile.RampLength)
        {
            return _profile.RampOn[_currentSymbolPosition];
        }

        if (_currentSymbolPosition < markSampleCount)
        {
            return 1f;
        }

        int rampOffPosition =
            _currentSymbolPosition - markSampleCount;
        return rampOffPosition < _profile.RampLength
            ? _profile.RampOff[rampOffPosition]
            : 0f;
    }

    private struct MorseSymbolEnumerator
    {
        private readonly MorseKeyingMode _mode;
        private MorseText.CharacterEnumerator _characters;
        private string? _pattern;
        private int _patternPosition;
        private bool _separatorPending;
        private bool _hasPendingRawSymbol;
        private char _pendingRawSymbol;

        public MorseSymbolEnumerator(
            MorseText text,
            MorseKeyingMode mode)
        {
            _mode = mode;
            _characters = text.GetEnumerator();
            _pattern = null;
            _patternPosition = 0;
            _separatorPending = false;
            _hasPendingRawSymbol = false;
            _pendingRawSymbol = default;
        }

        public bool MoveNext(out char symbol)
        {
            return _mode == MorseKeyingMode.Standard
                ? MoveNextStandard(out symbol)
                : MoveNextSst(out symbol);
        }

        private bool MoveNextStandard(out char symbol)
        {
            if (!_hasPendingRawSymbol
                && !MoveNextRaw(out _pendingRawSymbol))
            {
                symbol = default;
                return false;
            }

            _hasPendingRawSymbol = true;
            if (MoveNextRaw(out char next))
            {
                symbol = _pendingRawSymbol;
                _pendingRawSymbol = next;
                return true;
            }

            symbol = '~';
            _hasPendingRawSymbol = false;
            return true;
        }

        private bool MoveNextSst(out char symbol)
        {
            if (!_hasPendingRawSymbol
                && !MoveNextRaw(out _pendingRawSymbol))
            {
                symbol = default;
                return false;
            }

            _hasPendingRawSymbol = true;
            if (_pendingRawSymbol != '^')
            {
                symbol = _pendingRawSymbol;
                _hasPendingRawSymbol = false;
                return true;
            }

            if (!MoveNextRaw(out char next))
            {
                symbol = '~';
                _hasPendingRawSymbol = false;
                return true;
            }

            if (next == ' ')
            {
                symbol = '_';
                _hasPendingRawSymbol = false;
                return true;
            }

            symbol = '^';
            _pendingRawSymbol = next;
            return true;
        }

        private bool MoveNextRaw(out char symbol)
        {
            while (true)
            {
                if (_pattern is not null
                    && _patternPosition < _pattern.Length)
                {
                    symbol = _pattern[_patternPosition];
                    _patternPosition++;
                    return true;
                }

                if (_separatorPending)
                {
                    _separatorPending = false;
                    symbol =
                        _mode == MorseKeyingMode.Standard
                            ? ' '
                            : '^';
                    return true;
                }

                if (!_characters.MoveNext(out char character))
                {
                    symbol = default;
                    return false;
                }

                if (character is ' ' or '_')
                {
                    symbol = ' ';
                    return true;
                }

                if (!MorseKeyer.TryGetPattern(
                        Char.ToUpperInvariant(character),
                        out _pattern))
                {
                    continue;
                }

                _patternPosition = 0;
                _separatorPending = true;
            }
        }
    }
}
