namespace F1Team.Data.Cache;

public sealed record AppSettingsSnapshot(
    decimal CurrentBudget,
    string RivalName,
    IReadOnlyList<string> UserDriverIds,
    IReadOnlyList<string> UserConstructorIds,
    IReadOnlyList<string> RivalDriverIds,
    IReadOnlyList<string> RivalConstructorIds,
    decimal MyRemainingBudget = 0m,
    decimal RivalRemainingBudget = 0m,
    decimal MyActualBudget = 0m,
    decimal RivalActualBudget = 0m,
    string? MyX2DriverId = null,
    string? RivalX2DriverId = null,
    IReadOnlyList<string>? CandidateDriverIds = null,
    IReadOnlyList<string>? CandidateConstructorIds = null,
    decimal CandidateRemainingBudget = 0m,
    decimal CandidateActualBudget = 0m,
    string? CandidateX2DriverId = null,
    int SelectedComparisonTabIndex = 0,
    bool UseAverageRacePoints = false,
    string? ExpectedPointsSource = null,
    bool TrimHistoricalOutliers = false);
