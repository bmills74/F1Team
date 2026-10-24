namespace F1Team.Core.Models;

public sealed record FantasyAsset(
    string Id,
    string Abbreviation,
    FantasyAssetType Type,
    decimal Price,
    decimal ExpectedPoints,
    decimal ExpectedBudgetChange,
    string? DisplayName = null,
    string? TeamColor = null,
    decimal? PriorRacePoints = null,
    decimal? PriorPriorRacePoints = null,
    RequiredPoints? RequiredPoints = null,
    PriceChangeOdds? PriceChangeOdds = null,
    decimal? SimulationExpectedPoints = null,
    decimal? AverageRacePoints = null,
    decimal? LastFiveRacePoints = null,
    decimal? TrimmedAverageRacePoints = null,
    decimal? TrimmedLastFiveRacePoints = null,
    decimal? HistoricalPointsStandardDeviation = null,
    decimal? AverageSprintPoints = null,
    int HistoricalRaceCount = 0,
    int LastFiveRaceCount = 0,
    int HistoricalSprintRaceCount = 0,
    decimal? BoxBoxExpectedPoints = null,
    decimal? BoxBoxProjectedPoints = null,
    decimal? BoxBoxRiskRating = null,
    decimal? BoxBoxConfidence = null,
    decimal? BoxBoxSimulationStandardDeviation = null,
    decimal? BoxBoxSimulationP5 = null,
    decimal? BoxBoxSimulationP95 = null)
{
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Abbreviation : DisplayName;
}
