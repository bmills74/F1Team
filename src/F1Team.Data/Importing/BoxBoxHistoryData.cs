namespace F1Team.Data.Importing;

public sealed record BoxBoxHistoryRow(
    int Round,
    string AssetId,
    string AssetType,
    decimal ActualPoints,
    bool IsDnf,
    string? CircuitId = null);
