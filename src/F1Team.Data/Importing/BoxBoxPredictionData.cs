namespace F1Team.Data.Importing;

public sealed record BoxBoxPredictionData(
    int Season,
    int Round,
    string RaceName,
    string? Phase,
    DateTimeOffset? GeneratedAtUtc,
    IReadOnlyDictionary<string, BoxBoxAssetPrediction> Drivers,
    IReadOnlyDictionary<string, BoxBoxAssetPrediction> Constructors);

public sealed record BoxBoxAssetPrediction(
    decimal ExpectedPoints,
    decimal ProjectedPoints,
    decimal? RiskRating,
    decimal? Confidence,
    decimal? SimulationStandardDeviation,
    decimal? SimulationP5,
    decimal? SimulationP95);
