#if DEBUG
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MorseRunner.App.ViewModels;

internal sealed class DebugSessionTrace
{
    private const int Capacity = 1_024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly object _gate = new();
    private readonly List<TraceEntry> _entries = [];
    private long _nextOrder;
    private string _sessionDescription = string.Empty;
    private DateTimeOffset _createdUtc;

    public string? FilePath { get; private set; }

    public void Reset(string sessionDescription, string traceDirectory, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(traceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_gate)
        {
            _entries.Clear();
            _nextOrder = 0;
            _sessionDescription = sessionDescription;
            _createdUtc = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(traceDirectory);
            string timestamp = _createdUtc.ToString(
                "yyyyMMdd-HHmmss-fff",
                CultureInfo.InvariantCulture);
            FilePath = Path.Combine(
                traceDirectory,
                $"debug-trace-{timestamp}-{sessionId}.json");
            AddCore(0, "Session", sessionDescription);
            PersistCore();
        }
    }

    public void Add(long simulationBlock, string category, string detail)
    {
        lock (_gate)
        {
            AddCore(simulationBlock, category, detail);
            PersistCore();
        }
    }

    public string Format()
    {
        TraceEntry[] entries;
        lock (_gate)
        {
            entries = _entries
                .OrderBy(entry => entry.SimulationBlock)
                .ThenBy(entry => entry.Order)
                .ToArray();
        }

        var builder = new StringBuilder();
        builder.AppendLine("MorseRunnerXPlat debug session trace");
        builder.AppendLine("This is an in-memory diagnostic view. It is available only in Debug builds.");
        builder.AppendLine("A station can reply after accepting a near-match. A logged CALL means the final typed call differed from that station's actual call.");
        if (FilePath is not null)
        {
            builder.Append("JSON trace file: ").AppendLine(FilePath);
        }

        builder.AppendLine();

        foreach (TraceEntry entry in entries)
        {
            builder.Append('[')
                .Append(entry.SimulationBlock.ToString(CultureInfo.InvariantCulture))
                .Append("] ")
                .Append(entry.Category)
                .AppendLine();
            foreach (string line in entry.Detail.Split('\n'))
            {
                builder.Append("  ").AppendLine(line);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public string FormatJson()
    {
        lock (_gate)
        {
            return SerializeCore();
        }
    }

    private void AddCore(long simulationBlock, string category, string detail)
    {
        _entries.Add(new(++_nextOrder, simulationBlock, category, detail));
        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(0, _entries.Count - Capacity);
        }
    }

    private void PersistCore()
    {
        if (FilePath is null)
        {
            return;
        }

        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, SerializeCore());
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private string SerializeCore() => JsonSerializer.Serialize(
        new TraceDocument(
            SchemaVersion: 1,
            CreatedUtc: _createdUtc,
            Session: _sessionDescription,
            Entries: _entries
                .OrderBy(entry => entry.SimulationBlock)
                .ThenBy(entry => entry.Order)
                .ToArray()),
        JsonOptions);

    private sealed record TraceDocument(
        int SchemaVersion,
        DateTimeOffset CreatedUtc,
        string Session,
        IReadOnlyList<TraceEntry> Entries);

    internal sealed record TraceEntry(
        long Order,
        long SimulationBlock,
        string Category,
        string Detail);
}
#endif
