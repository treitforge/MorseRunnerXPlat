using System.Text;
using MorseRunner.Domain;

namespace MorseRunner.Infrastructure.Tests;

public sealed class ResultPersistenceTests
{
    [Fact]
    public void ExporterCreatesJsonAndCabrilloArtifacts()
    {
        SessionResult result = CreateResult(score: 123);
        Qso[] qsos =
        [
            new()
            {
                Call = "K1ABC",
                Rst = 599,
                Exchange1 = "5NN",
                Exchange2 = "42",
                Points = 3,
            },
        ];

        ResultExportArtifact json = ResultExporter.Create(
            result,
            qsos,
            ResultExportFormat.Json,
            "RANDY");
        ResultExportArtifact cabrillo = ResultExporter.Create(
            result,
            qsos,
            ResultExportFormat.Cabrillo,
            "RANDY");

        Assert.EndsWith(".json", json.SuggestedFileName);
        Assert.Contains(
            "\"operatorName\": \"RANDY\"",
            Encoding.UTF8.GetString(json.Content));
        string cabrilloText = Encoding.UTF8.GetString(cabrillo.Content);
        Assert.Contains("OPERATORS: RANDY", cabrilloText);
        Assert.Contains("CLAIMED-SCORE: 123", cabrilloText);
        Assert.Contains("QSO: K1ABC 599 5NN 42", cabrilloText);
    }

    [Fact]
    public async Task HighScoreStoreRetainsOnlyTheBestContestScore()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"MorseRunnerXPlat-results-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "high-scores.json");
        try
        {
            using var store = new HighScoreStore(path);
            await store.RecordAsync(
                CreateResult(score: 100),
                "FIRST",
                TestContext.Current.CancellationToken);
            await store.RecordAsync(
                CreateResult(score: 80),
                "SECOND",
                TestContext.Current.CancellationToken);
            ContestHighScore? score = await store.GetAsync(
                new ContestId("scWpx"),
                TestContext.Current.CancellationToken);

            Assert.NotNull(score);
            Assert.Equal(100, score.Score);
            Assert.Equal("FIRST", score.OperatorName);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static SessionResult CreateResult(int score) =>
        new(
            SessionId.New(),
            new ContestId("scWpx"),
            QsoCount: 1,
            score,
            TimeSpan.FromMinutes(1),
            SessionState.Completed,
            QsoRatePerHour: 60);
}
