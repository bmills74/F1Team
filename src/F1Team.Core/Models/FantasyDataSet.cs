namespace F1Team.Core.Models;

public sealed record FantasyDataSet(
    IReadOnlyList<FantasyAsset> Drivers,
    IReadOnlyList<FantasyAsset> Constructors,
    RaceContext RaceContext);
