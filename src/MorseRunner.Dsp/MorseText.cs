namespace MorseRunner.Dsp;

public readonly struct MorseText
{
    public const int MaximumSegmentCount = 8;

    private readonly string _segment0;
    private readonly string? _segment1;
    private readonly string? _segment2;
    private readonly string? _segment3;
    private readonly string? _segment4;
    private readonly string? _segment5;
    private readonly string? _segment6;
    private readonly string? _segment7;

    public MorseText(
        string segment0,
        string? segment1 = null,
        string? segment2 = null,
        string? segment3 = null,
        string? segment4 = null,
        string? segment5 = null,
        string? segment6 = null,
        string? segment7 = null)
    {
        ArgumentNullException.ThrowIfNull(segment0);
        EnsureSegmentsAreContiguous(
            segment1,
            segment2,
            segment3,
            segment4,
            segment5,
            segment6,
            segment7);

        _segment0 = segment0;
        _segment1 = segment1;
        _segment2 = segment2;
        _segment3 = segment3;
        _segment4 = segment4;
        _segment5 = segment5;
        _segment6 = segment6;
        _segment7 = segment7;
        SegmentCount = segment7 is not null ? 8
            : segment6 is not null ? 7
            : segment5 is not null ? 6
            : segment4 is not null ? 5
            : segment3 is not null ? 4
            : segment2 is not null ? 3
            : segment1 is not null ? 2
            : 1;
    }

    public int SegmentCount { get; }

    public string GetSegment(int index) =>
        index switch
        {
            0 when SegmentCount > 0 => _segment0,
            1 when SegmentCount > 1 => _segment1!,
            2 when SegmentCount > 2 => _segment2!,
            3 when SegmentCount > 3 => _segment3!,
            4 when SegmentCount > 4 => _segment4!,
            5 when SegmentCount > 5 => _segment5!,
            6 when SegmentCount > 6 => _segment6!,
            7 when SegmentCount > 7 => _segment7!,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

    internal CharacterEnumerator GetEnumerator() => new(this);

    private static void EnsureSegmentsAreContiguous(
        string? segment1,
        string? segment2,
        string? segment3,
        string? segment4,
        string? segment5,
        string? segment6,
        string? segment7)
    {
        if ((segment1 is null && segment2 is not null)
            || (segment2 is null && segment3 is not null)
            || (segment3 is null && segment4 is not null)
            || (segment4 is null && segment5 is not null)
            || (segment5 is null && segment6 is not null)
            || (segment6 is null && segment7 is not null))
        {
            throw new ArgumentException(
                "Morse text segments must be contiguous.");
        }
    }

    internal struct CharacterEnumerator
    {
        private readonly MorseText _text;
        private int _segmentIndex;
        private int _characterIndex;

        public CharacterEnumerator(MorseText text)
        {
            _text = text;
            _segmentIndex = 0;
            _characterIndex = 0;
        }

        public bool MoveNext(out char character)
        {
            while (_segmentIndex < _text.SegmentCount)
            {
                string segment = _text.GetSegment(_segmentIndex);
                if (_characterIndex < segment.Length)
                {
                    character = segment[_characterIndex];
                    _characterIndex++;
                    return true;
                }

                _segmentIndex++;
                _characterIndex = 0;
            }

            character = default;
            return false;
        }
    }
}
