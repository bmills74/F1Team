namespace F1Team.App.ViewModels;

/// <summary>
/// One metric row in the side-by-side comparison table (My / Rival / Candidate).
/// Values are pre-formatted strings and colors are hex brushes so the view binds directly.
/// </summary>
public sealed record ComparisonRow(
    string Label,
    string MyText,
    string MyColor,
    string RivalText,
    string RivalColor,
    string CandidateText,
    string CandidateColor);
