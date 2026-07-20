using MorseRunner.Domain;

namespace MorseRunner.Engine;

internal enum QrmMessageKind
{
    Qrl,
    QrlTwice,
    LongCq,
    Qsy,
}

internal readonly struct ContestQrmMessageDescriptor
{
    private readonly string _segment0;
    private readonly string _segment1;
    private readonly string _segment2;
    private readonly string _segment3;
    private readonly string _segment4;

    internal ContestQrmMessageDescriptor(
        QrmMessageKind kind,
        string messageSet,
        string segment0,
        string? segment1 = null,
        string? segment2 = null,
        string? segment3 = null,
        string? segment4 = null)
    {
        Kind = kind;
        MessageSet = messageSet;
        _segment0 = segment0;
        _segment1 = segment1 ?? string.Empty;
        _segment2 = segment2 ?? string.Empty;
        _segment3 = segment3 ?? string.Empty;
        _segment4 = segment4 ?? string.Empty;
        SegmentCount = segment4 is not null
            ? 5
            : segment3 is not null
                ? 4
                : segment2 is not null
                    ? 3
                    : segment1 is not null
                        ? 2
                        : 1;
    }

    public QrmMessageKind Kind { get; }

    public string MessageSet { get; }

    public int SegmentCount { get; }

    public int CharacterCount
    {
        get
        {
            int result = 0;
            for (int index = 0; index < SegmentCount; index++)
            {
                result += GetSegment(index).Length;
            }

            return result;
        }
    }

    public string GetSegment(int index)
    {
        return index switch
        {
            0 when index < SegmentCount => _segment0,
            1 when index < SegmentCount => _segment1,
            2 when index < SegmentCount => _segment2,
            3 when index < SegmentCount => _segment3,
            4 when index < SegmentCount => _segment4,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    public ReadOnlySpan<char> GetSegmentSpan(int index) =>
        GetSegment(index).AsSpan();

    public string MaterializeForObservation()
    {
        return string.Create(
            CharacterCount,
            this,
            static (destination, descriptor) =>
            {
                int destinationOffset = 0;
                for (int index = 0;
                     index < descriptor.SegmentCount;
                     index++)
                {
                    ReadOnlySpan<char> segment =
                        descriptor.GetSegmentSpan(index);
                    segment.CopyTo(destination[destinationOffset..]);
                    destinationOffset += segment.Length;
                }
            });
    }
}

internal static class ContestQrmMessageCatalog
{
    public static ContestQrmMessageDescriptor CreateInitial(
        ContestId contestId,
        int choice,
        string qrmCall,
        string operatorCall)
    {
        ArgumentNullException.ThrowIfNull(qrmCall);
        ArgumentNullException.ThrowIfNull(operatorCall);

        return choice switch
        {
            0 => new(
                QrmMessageKind.Qrl,
                "[msgQrl]",
                "QRL?"),
            1 or 2 => new(
                QrmMessageKind.QrlTwice,
                "[msgQrl2]",
                "QRL?",
                "   ",
                "QRL?"),
            3 or 4 or 5 => CreateLongCq(contestId, qrmCall),
            6 => new(
                QrmMessageKind.Qsy,
                "[msqQsy]",
                operatorCall,
                "  QSY QSY"),
            _ => throw new ArgumentOutOfRangeException(nameof(choice)),
        };
    }

    public static ContestQrmMessageDescriptor CreateLongCq(
        ContestId contestId,
        string qrmCall)
    {
        ArgumentNullException.ThrowIfNull(qrmCall);

        return contestId.Value switch
        {
            "scFieldDay" => WithSuffix(
                "CQ CQ FD ",
                qrmCall,
                " FD"),
            "scArrlSS" => WithSuffix(
                "CQ CQ SS ",
                qrmCall,
                " SS"),
            "scCwt" => WithoutSuffix(
                "CQ CQ CWT ",
                qrmCall),
            "scSst" => WithoutSuffix(
                "CQ CQ SST ",
                qrmCall),
            _ => WithSuffix(
                "CQ CQ TEST ",
                qrmCall,
                " TEST"),
        };
    }

    private static ContestQrmMessageDescriptor WithSuffix(
        string prefix,
        string qrmCall,
        string suffix)
    {
        return new(
            QrmMessageKind.LongCq,
            "[msgLongCQ]",
            prefix,
            qrmCall,
            " ",
            qrmCall,
            suffix);
    }

    private static ContestQrmMessageDescriptor WithoutSuffix(
        string prefix,
        string qrmCall)
    {
        return new(
            QrmMessageKind.LongCq,
            "[msgLongCQ]",
            prefix,
            qrmCall,
            " ",
            qrmCall);
    }
}
