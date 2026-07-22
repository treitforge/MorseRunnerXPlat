namespace MorseRunner.Dsp;

public sealed class MorseKeyer
{
    private static readonly Dictionary<char, string> MorsePatterns =
        new Dictionary<char, string>
        {
            ['0'] = "-----",
            ['1'] = ".----",
            ['2'] = "..---",
            ['3'] = "...--",
            ['4'] = "....-",
            ['5'] = ".....",
            ['6'] = "-....",
            ['7'] = "--...",
            ['8'] = "---..",
            ['9'] = "----.",
            ['A'] = ".-",
            ['B'] = "-...",
            ['C'] = "-.-.",
            ['D'] = "-..",
            ['E'] = ".",
            ['F'] = "..-.",
            ['G'] = "--.",
            ['H'] = "....",
            ['I'] = "..",
            ['J'] = ".---",
            ['K'] = "-.-",
            ['L'] = ".-..",
            ['M'] = "--",
            ['N'] = "-.",
            ['O'] = "---",
            ['P'] = ".--.",
            ['Q'] = "--.-",
            ['R'] = ".-.",
            ['S'] = "...",
            ['T'] = "-",
            ['U'] = "..-",
            ['V'] = "...-",
            ['W'] = ".--",
            ['X'] = "-..-",
            ['Y'] = "-.--",
            ['Z'] = "--..",
            ['/'] = "-..-.",
            ['.'] = ".-.-.-",
            [','] = "--..--",
            ['?'] = "..--..",
            ['='] = "-...-",
            ['\\'] = "...-.",
        };

    private readonly float[] _rampOn;
    private readonly float[] _rampOff;

    public MorseKeyer(
        int sampleRate,
        int blockSize,
        float riseTimeSeconds = 0.005f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(riseTimeSeconds);

        SampleRate = sampleRate;
        BlockSize = blockSize;
        RiseTimeSeconds = riseTimeSeconds;

        int rampLength = (int)Math.Round(
            2.7d * (double)RiseTimeSeconds * SampleRate,
            MidpointRounding.ToEven);
        _rampOn = LegacyMorseRamp.CreateOn(rampLength);
        _rampOff = LegacyMorseRamp.CreateOff(_rampOn);
    }

    public int SampleRate { get; }

    public int BlockSize { get; }

    public float RiseTimeSeconds { get; }

    public int SendingWordsPerMinute { get; private set; }

    public int CharacterWordsPerMinute { get; private set; }

    public int TrueEnvelopeLength { get; private set; }

    public void SetWordsPerMinute(
        int sendingWordsPerMinute,
        int characterWordsPerMinute = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sendingWordsPerMinute);
        ArgumentOutOfRangeException.ThrowIfNegative(characterWordsPerMinute);
        SendingWordsPerMinute = sendingWordsPerMinute;
        CharacterWordsPerMinute = characterWordsPerMinute;
    }

    public static string Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new System.Text.StringBuilder();
        foreach (char character in text)
        {
            if (character is ' ' or '_')
            {
                result.Append(' ');
                continue;
            }

            char normalized = Char.ToUpperInvariant(character);
            if (!MorsePatterns.TryGetValue(normalized, out string? pattern))
            {
                continue;
            }

            result.Append(pattern);
            result.Append(' ');
        }

        if (result.Length > 0)
        {
            result[^1] = '~';
        }

        return result.ToString();
    }

    internal static bool TryGetPattern(
        char character,
        out string? pattern) =>
        MorsePatterns.TryGetValue(character, out pattern);

    public string EncodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (CharacterWordsPerMinute <= 0)
        {
            return Encode(text);
        }

        var result = new System.Text.StringBuilder();
        foreach (char character in text)
        {
            if (character is ' ' or '_')
            {
                if (result.Length > 0 && result[^1] == '^')
                {
                    result[^1] = '_';
                }
                else
                {
                    result.Append(' ');
                }

                continue;
            }

            char normalized = Char.ToUpperInvariant(character);
            if (!MorsePatterns.TryGetValue(normalized, out string? pattern))
            {
                continue;
            }

            result.Append(pattern);
            result.Append('^');
        }

        if (result.Length > 0 && result[^1] == '^')
        {
            result[^1] = '~';
        }

        return result.ToString();
    }

    public float[] CreateEnvelope(string morseMessage)
    {
        ArgumentNullException.ThrowIfNull(morseMessage);
        if (SendingWordsPerMinute <= 0)
        {
            throw new InvalidOperationException(
                "Words per minute must be configured before rendering.");
        }

        if (CharacterWordsPerMinute > 0)
        {
            return CreateCharacterSpeedEnvelope(morseMessage);
        }

        int unitCount = CountUnits(morseMessage);
        int samplesPerUnit = (int)Math.Round(
            60d / 48d * SampleRate / SendingWordsPerMinute,
            MidpointRounding.ToEven);
        TrueEnvelopeLength = checked(unitCount * samplesPerUnit);
        int paddedLength = checked(
            (TrueEnvelopeLength + BlockSize - 1) / BlockSize * BlockSize);
        var result = new float[paddedLength];
        int position = 0;

        foreach (char symbol in morseMessage)
        {
            switch (symbol)
            {
                case '.':
                    AddMark(result, ref position, samplesPerUnit);
                    AddOff(ref position, samplesPerUnit, _rampOff.Length);
                    break;
                case '-':
                    AddMark(result, ref position, 3 * samplesPerUnit);
                    AddOff(ref position, samplesPerUnit, _rampOff.Length);
                    break;
                case ' ':
                    AddOff(ref position, 2 * samplesPerUnit, 0);
                    break;
                case '~':
                    AddOff(ref position, 3 * samplesPerUnit, 0);
                    break;
            }
        }

        if (position != TrueEnvelopeLength)
        {
            throw new InvalidOperationException(
                "Morse envelope timing did not consume the expected samples.");
        }

        return result;
    }

    private float[] CreateCharacterSpeedEnvelope(string morseMessage)
    {
        bool farnsworth =
            SendingWordsPerMinute <= CharacterWordsPerMinute;
        int characterWordsPerMinute = farnsworth
            ? CharacterWordsPerMinute
            : SendingWordsPerMinute;
        int samplesPerCharacterUnit = (int)Math.Round(
            60d * SampleRate / characterWordsPerMinute / 48d,
            MidpointRounding.ToEven);
        int samplesPerAdjustedUnit = farnsworth
            ? CalculateSamplesPerAdjustedUnit()
            : samplesPerCharacterUnit;

        TrueEnvelopeLength = CountCharacterSpeedSamples(
            morseMessage,
            samplesPerCharacterUnit,
            samplesPerAdjustedUnit,
            farnsworth);
        int paddedLength = checked(
            (TrueEnvelopeLength + BlockSize - 1) / BlockSize * BlockSize);
        var result = new float[paddedLength];
        int position = 0;
        var symbols = new FarnsworthSymbolEnumerator(morseMessage);

        while (symbols.MoveNext(out char symbol))
        {
            switch (symbol)
            {
                case '.':
                    AddMark(result, ref position, samplesPerCharacterUnit);
                    AddOff(
                        ref position,
                        samplesPerCharacterUnit,
                        _rampOff.Length);
                    break;
                case '-':
                    AddMark(
                        result,
                        ref position,
                        3 * samplesPerCharacterUnit);
                    AddOff(
                        ref position,
                        samplesPerCharacterUnit,
                        _rampOff.Length);
                    break;
                case '^':
                    AddCharacterSpeedSpace(
                        ref position,
                        durationInUnits: 3,
                        subtractPriorCharacterUnit: true,
                        samplesPerCharacterUnit,
                        samplesPerAdjustedUnit,
                        farnsworth);
                    break;
                case '_':
                    AddCharacterSpeedSpace(
                        ref position,
                        durationInUnits: 5,
                        subtractPriorCharacterUnit: true,
                        samplesPerCharacterUnit,
                        samplesPerAdjustedUnit,
                        farnsworth);
                    break;
                case '~':
                    AddCharacterSpeedSpace(
                        ref position,
                        durationInUnits: 4,
                        subtractPriorCharacterUnit: true,
                        samplesPerCharacterUnit,
                        samplesPerAdjustedUnit,
                        farnsworth);
                    break;
                case ' ':
                    AddCharacterSpeedSpace(
                        ref position,
                        durationInUnits: 5,
                        subtractPriorCharacterUnit: false,
                        samplesPerCharacterUnit,
                        samplesPerAdjustedUnit,
                        farnsworth);
                    break;
            }
        }

        if (position != TrueEnvelopeLength)
        {
            throw new InvalidOperationException(
                "Morse envelope timing did not consume the expected samples.");
        }

        return result;
    }

    private int CalculateSamplesPerAdjustedUnit()
    {
        float delayPerWord = (float)(
            (60d / SendingWordsPerMinute)
            - (31d * 60d / CharacterWordsPerMinute / 48d));
        return (int)MathF.Round(
            delayPerWord * SampleRate / 17f,
            MidpointRounding.ToEven);
    }

    private static int CountCharacterSpeedSamples(
        string morseMessage,
        int samplesPerCharacterUnit,
        int samplesPerAdjustedUnit,
        bool farnsworth)
    {
        int sampleCount = 0;
        var symbols = new FarnsworthSymbolEnumerator(morseMessage);

        while (symbols.MoveNext(out char symbol))
        {
            int symbolSampleCount = symbol switch
            {
                '.' => checked(2 * samplesPerCharacterUnit),
                '-' => checked(4 * samplesPerCharacterUnit),
                '^' => CalculateSpaceSampleCount(
                    durationInUnits: 3,
                    subtractPriorCharacterUnit: true,
                    samplesPerCharacterUnit,
                    samplesPerAdjustedUnit,
                    farnsworth),
                '_' => CalculateSpaceSampleCount(
                    durationInUnits: 5,
                    subtractPriorCharacterUnit: true,
                    samplesPerCharacterUnit,
                    samplesPerAdjustedUnit,
                    farnsworth),
                '~' => CalculateSpaceSampleCount(
                    durationInUnits: 4,
                    subtractPriorCharacterUnit: true,
                    samplesPerCharacterUnit,
                    samplesPerAdjustedUnit,
                    farnsworth),
                ' ' => CalculateSpaceSampleCount(
                    durationInUnits: 5,
                    subtractPriorCharacterUnit: false,
                    samplesPerCharacterUnit,
                    samplesPerAdjustedUnit,
                    farnsworth),
                _ => 0,
            };
            sampleCount = checked(sampleCount + symbolSampleCount);
        }

        return sampleCount;
    }

    private static void AddCharacterSpeedSpace(
        ref int position,
        int durationInUnits,
        bool subtractPriorCharacterUnit,
        int samplesPerCharacterUnit,
        int samplesPerAdjustedUnit,
        bool farnsworth)
    {
        position = checked(
            position
            + CalculateSpaceSampleCount(
                durationInUnits,
                subtractPriorCharacterUnit,
                samplesPerCharacterUnit,
                samplesPerAdjustedUnit,
                farnsworth));
    }

    private static int CalculateSpaceSampleCount(
        int durationInUnits,
        bool subtractPriorCharacterUnit,
        int samplesPerCharacterUnit,
        int samplesPerAdjustedUnit,
        bool farnsworth)
    {
        int samplesPerUnit = farnsworth
            ? samplesPerAdjustedUnit
            : samplesPerCharacterUnit;
        int sampleCount = checked(durationInUnits * samplesPerUnit);
        return subtractPriorCharacterUnit
            ? checked(sampleCount - samplesPerCharacterUnit)
            : sampleCount;
    }

    private static int CountUnits(string morseMessage)
    {
        int units = 0;
        foreach (char symbol in morseMessage)
        {
            units += symbol switch
            {
                '.' => 2,
                '-' => 4,
                ' ' => 2,
                '~' => 3,
                _ => 0,
            };
        }

        return units;
    }

    private struct FarnsworthSymbolEnumerator
    {
        private readonly string _morseMessage;
        private readonly bool _translateEncodedSpaces;
        private int _position;
        private int _pendingAdditionalWordSpaces;
        private bool _hasSeenCharacterElement;

        public FarnsworthSymbolEnumerator(string morseMessage)
        {
            _morseMessage = morseMessage;
            // Base Encode emits no TFarns markers and exactly one terminal
            // message marker. Internal message markers identify a CE piece
            // stream whose raw separator spaces must remain unchanged.
            _translateEncodedSpaces =
                morseMessage.IndexOf('^') < 0
                && morseMessage.IndexOf('_') < 0
                && morseMessage.IndexOf('~') == morseMessage.Length - 1;
            _position = 0;
            _pendingAdditionalWordSpaces = 0;
            _hasSeenCharacterElement = false;
        }

        public bool MoveNext(out char symbol)
        {
            if (_pendingAdditionalWordSpaces > 0)
            {
                _pendingAdditionalWordSpaces--;
                symbol = ' ';
                return true;
            }

            if (_position >= _morseMessage.Length)
            {
                symbol = default;
                return false;
            }

            symbol = _morseMessage[_position];
            _position++;
            if (!_translateEncodedSpaces || symbol != ' ')
            {
                if (symbol is '.' or '-')
                {
                    _hasSeenCharacterElement = true;
                }

                return true;
            }

            int spaceCount = 1;
            while (_position < _morseMessage.Length
                   && _morseMessage[_position] == ' ')
            {
                spaceCount++;
                _position++;
            }

            if (!_hasSeenCharacterElement)
            {
                symbol = ' ';
                _pendingAdditionalWordSpaces = spaceCount - 1;
                return true;
            }

            // Encode replaces the last base space with '~'. A preceding run
            // therefore represents trailing text spaces, without a CE '~'.
            if (_position == _morseMessage.Length - 1
                && _morseMessage[_position] == '~')
            {
                _position++;
                symbol = '_';
                _pendingAdditionalWordSpaces = spaceCount - 1;
                return true;
            }

            if (spaceCount == 1)
            {
                symbol = '^';
                return true;
            }

            symbol = '_';
            _pendingAdditionalWordSpaces = spaceCount - 2;
            return true;
        }
    }

    private void AddMark(
        Span<float> destination,
        ref int position,
        int markSampleCount)
    {
        _rampOn.CopyTo(destination[position..]);
        position += _rampOn.Length;

        int steadySampleCount = markSampleCount - _rampOn.Length;
        if (steadySampleCount > 0)
        {
            destination.Slice(position, steadySampleCount).Fill(1f);
        }

        position = checked(position + steadySampleCount);

        _rampOff.CopyTo(destination[position..]);
        position += _rampOff.Length;
    }

    private static void AddOff(
        ref int position,
        int offSampleCount,
        int precedingRampLength)
    {
        position += offSampleCount - precedingRampLength;
    }
}
