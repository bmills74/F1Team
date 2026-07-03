namespace F1Team.Core.Models;

public sealed record RaceContext(
    string RaceId,
    string RaceName,
    int RoundNumber,
    string? CountryName,
    string? CircuitName,
    bool SprintWeekend,
    string? SimulationName,
    DateTimeOffset FetchedAtUtc);
