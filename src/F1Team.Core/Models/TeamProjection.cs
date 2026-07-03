namespace F1Team.Core.Models;

public sealed record TeamProjection(
    string TeamName,
    int DriverCount,
    int ConstructorCount,
    decimal CurrentBudget,
    decimal TotalCost,
    decimal RemainingBudget,
    decimal ExpectedPoints,
    decimal ExpectedBudgetChange)
{
    public const int RequiredDriverCount = 5;
    public const int RequiredConstructorCount = 2;

    public bool IsComplete =>
        DriverCount == RequiredDriverCount &&
        ConstructorCount == RequiredConstructorCount;

    public bool IsOverBudget => RemainingBudget < 0;
}
