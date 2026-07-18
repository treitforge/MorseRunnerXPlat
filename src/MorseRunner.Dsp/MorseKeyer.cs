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

        int rampLength = (int)MathF.Round(
            2.7f * RiseTimeSeconds * SampleRate,
            MidpointRounding.ToEven);
        _rampOn = CreateBlackmanHarrisStepResponse(rampLength);
        _rampOff = new float[rampLength];
        for (int index = 0; index < rampLength; index++)
        {
            _rampOff[_rampOff.Length - 1 - index] = _rampOn[index];
        }
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

    public float[] CreateEnvelope(string morseMessage)
    {
        ArgumentNullException.ThrowIfNull(morseMessage);
        if (SendingWordsPerMinute <= 0)
        {
            throw new InvalidOperationException(
                "Words per minute must be configured before rendering.");
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

    private static float[] CreateBlackmanHarrisStepResponse(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var response = new float[length];
        for (int index = 0; index < response.Length; index++)
        {
            double position = (float)index / length;
            response[index] = (float)(
                0.35875d
                - (0.48829d * Math.Cos(2d * Math.PI * position))
                + (0.14128d * Math.Cos(4d * Math.PI * position))
                - (0.01168d * Math.Cos(6d * Math.PI * position)));
        }

        for (int index = 1; index < response.Length; index++)
        {
            response[index] = response[index - 1] + response[index];
        }

        float scale = 1f / response[^1];
        for (int index = 0; index < response.Length; index++)
        {
            response[index] *= scale;
        }

        return response;
    }

    private void AddMark(
        Span<float> destination,
        ref int position,
        int markSampleCount)
    {
        _rampOn.CopyTo(destination[position..]);
        position += _rampOn.Length;

        int steadySampleCount = markSampleCount - _rampOn.Length;
        destination.Slice(position, steadySampleCount).Fill(1f);
        position += steadySampleCount;

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
