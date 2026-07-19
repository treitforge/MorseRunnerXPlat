using System.Globalization;

namespace MorseRunner.App.ViewModels;

public sealed record ScoreWindowViewModel(
    int Score,
    int QsoCount,
    int QsoRatePerHour,
    int HighScore,
    string Contest,
    string Elapsed)
{
    public string ScoreString =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Contest} | {QsoCount} QSOs | {Score} points | {Elapsed}");

    public string RateString =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{QsoRatePerHour} QSOs/hour");

    public string HighScoreString =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{HighScore} points");
}
