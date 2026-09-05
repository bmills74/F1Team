namespace F1Team.Data.Importing;

public sealed record HistoricalPointsMetrics(
    decimal SeasonAverage,
    decimal LastFiveAverage,
    decimal TrimmedSeasonAverage,
    decimal TrimmedLastFiveAverage,
    decimal StandardDeviation,
    decimal AverageSprintPoints,
    int RaceCount,
    int LastFiveRaceCount,
    int SprintRaceCount);
