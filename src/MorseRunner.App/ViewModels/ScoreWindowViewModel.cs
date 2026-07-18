using System.Globalization;

namespace MorseRunner.App.ViewModels;

public sealed record ScoreWindowViewModel(
    int Score,
    int QsoCount,
    string Contest,
    string Elapsed)
{
    public string ScoreString =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Contest} | {QsoCount} QSOs | {Score} points | {Elapsed}");
}
