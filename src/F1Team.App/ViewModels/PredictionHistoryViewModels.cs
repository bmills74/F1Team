using F1Team.Data.Importing;
using System.Windows.Media;

namespace F1Team.App.ViewModels;

public sealed record PredictionHistoryCell(
    int Round,
    decimal? ActualPoints,
    decimal? Prediction,
    decimal? Difference)
{
    public string ActualText => ActualPoints?.ToString("0.0") ?? "–";

    public string PredictionText => Prediction is null ? string.Empty : Prediction.Value.ToString("0.0");

    public string DifferenceText => Difference is null ? string.Empty : $"{Difference:+0.0;-0.0;0.0}";

    public string DisplayText => string.IsNullOrEmpty(PredictionText)
        ? ActualText
        : $"{ActualText}\n{PredictionText} / {DifferenceText}";

    public Brush DifferenceBrush => Difference is null
        ? Brushes.Black
        : Math.Abs(Difference.Value) <= 5m
            ? Brushes.ForestGreen
            : Math.Abs(Difference.Value) <= 10m
                ? Brushes.DarkOrange
                : Brushes.Firebrick;
}

public sealed record PredictionHistoryRow(
    string AssetId,
    string AssetType,
    IReadOnlyList<PredictionHistoryCell> Cells)
{
    public decimal TotalActual => Cells.Where(cell => cell.ActualPoints is not null)
        .Sum(cell => cell.ActualPoints ?? 0m);

    public decimal? TotalPredicted => Cells.Any(cell => cell.Prediction is not null)
        ? Cells.Where(cell => cell.Prediction is not null).Sum(cell => cell.Prediction ?? 0m)
        : null;

    public string TotalActualText => TotalActual.ToString("0.0");

    public string TotalPredictedText => TotalPredicted?.ToString("0.0") ?? "–";
}

public static class PredictionHistoryMapper
{
    public static IReadOnlyList<PredictionHistoryRow> Build(
        IEnumerable<BoxBoxHistoryRow> history,
        string assetType,
        IEnumerable<int> rounds,
        IEnumerable<BoxBoxArchivePrediction> predictions)
    {
        var source = history
            .Where(row => string.Equals(row.AssetType, assetType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var predictionsByKey = predictions
            .Where(row => string.Equals(row.AssetType, assetType, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(row => (row.Round, row.AssetId), row => row.ProjectedPoints);
        return source
            .GroupBy(row => row.AssetId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Sum(row => row.ActualPoints))
            .ThenBy(group => group.Key)
            .Select(group => new PredictionHistoryRow(
                group.Key,
                assetType,
                rounds.Select(round =>
                {
                    var result = group.FirstOrDefault(row => row.Round == round);
                    var prediction = predictionsByKey.TryGetValue((round, group.Key), out var value) ? value : (decimal?)null;
                    return new PredictionHistoryCell(round, result?.ActualPoints, prediction,
                        result is not null && prediction is not null ? result.ActualPoints - prediction : null);
                }).ToArray()))
            .ToArray();
    }
}
