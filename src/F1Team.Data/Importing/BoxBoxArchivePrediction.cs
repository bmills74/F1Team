namespace F1Team.Data.Importing;

public sealed record BoxBoxArchivePrediction(
    int Round,
    string AssetId,
    string AssetType,
    decimal ProjectedPoints);
