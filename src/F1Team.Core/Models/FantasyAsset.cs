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
    PriceChangeOdds? PriceChangeOdds = null)
{
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Abbreviation : DisplayName;
}
