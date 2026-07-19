using System.Globalization;
using System.Text;
using System.Text.Json;
using MorseRunner.Domain;

namespace MorseRunner.Infrastructure;

public enum ResultExportFormat
{
    Json,
    Cabrillo,
}

public sealed record ResultExportArtifact(
    string MediaType,
    string SuggestedFileName,
    byte[] Content);

public static class ResultExporter
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    public static ResultExportArtifact Create(
        SessionResult result,
        IReadOnlyList<Qso> qsos,
        ResultExportFormat format,
        string? operatorName = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(qsos);
        return format switch
        {
            ResultExportFormat.Json => new(
                "application/json",
                $"{result.SessionId}.json",
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        result,
                        qsos,
                        operatorName = operatorName ?? string.Empty,
                    },
                    JsonOptions)),
            ResultExportFormat.Cabrillo => new(
                "text/plain; charset=utf-8",
                $"{result.SessionId}.log",
                Encoding.UTF8.GetBytes(
                    CreateCabrillo(result, qsos, operatorName))),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    public static async Task<string> SaveAtomicAsync(
        string directory,
        ResultExportArtifact artifact,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(artifact);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, artifact.SuggestedFileName);
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                artifact.Content,
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CreateCabrillo(
        SessionResult result,
        IReadOnlyList<Qso> qsos,
        string? operatorName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("START-OF-LOG: 3.0");
        builder.Append("CONTEST: ").AppendLine(result.ContestId.Value);
        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            builder.Append("OPERATORS: ").AppendLine(operatorName.Trim());
        }

        builder.Append("CLAIMED-SCORE: ").AppendLine(
            result.Score.ToString(CultureInfo.InvariantCulture));
        foreach (Qso qso in qsos)
        {
            builder.Append("QSO: ")
                .Append(qso.Call)
                .Append(' ')
                .Append(qso.Rst)
                .Append(' ')
                .Append(qso.Exchange1)
                .Append(' ')
                .AppendLine(qso.Exchange2);
        }

        builder.AppendLine("END-OF-LOG:");
        return builder.ToString();
    }
}

public sealed record ContestHighScore(
    string ContestId,
    int Score,
    int QsoCount,
    int QsoRatePerHour,
    DateTimeOffset AchievedAt,
    string OperatorName);

public sealed class HighScoreStore(string path) : IDisposable
{
    private readonly string _path =
        Path.GetFullPath(
            path ?? throw new ArgumentNullException(nameof(path)));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public async Task<ContestHighScore?> GetAsync(
        ContestId contestId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyDictionary<string, ContestHighScore> scores =
                await LoadAsync(cancellationToken);
            return scores.TryGetValue(contestId.Value, out ContestHighScore? score)
                ? score
                : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ContestHighScore> RecordAsync(
        SessionResult result,
        string? operatorName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, ContestHighScore> scores =
                new(
                    await LoadAsync(cancellationToken),
                    StringComparer.Ordinal);
            if (scores.TryGetValue(
                    result.ContestId.Value,
                    out ContestHighScore? existing)
                && existing.Score >= result.Score)
            {
                return existing;
            }

            var highScore = new ContestHighScore(
                result.ContestId.Value,
                result.Score,
                result.QsoCount,
                result.QsoRatePerHour,
                DateTimeOffset.UtcNow,
                operatorName?.Trim() ?? string.Empty);
            scores[result.ContestId.Value] = highScore;
            await SaveAsync(scores, cancellationToken);
            return highScore;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, ContestHighScore>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new Dictionary<string, ContestHighScore>(
                StringComparer.Ordinal);
        }

        await using FileStream stream = File.OpenRead(_path);
        Dictionary<string, ContestHighScore>? scores =
            await JsonSerializer.DeserializeAsync<
                Dictionary<string, ContestHighScore>>(
                stream,
                cancellationToken: cancellationToken);
        return scores
            ?? new Dictionary<string, ContestHighScore>(
                StringComparer.Ordinal);
    }

    private async Task SaveAsync(
        IReadOnlyDictionary<string, ContestHighScore> scores,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (FileStream stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    scores,
                    cancellationToken: cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }
    }
}
