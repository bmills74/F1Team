using F1Team.Core.Models;

namespace F1Team.Core.Calculations;

public sealed class BudgetProjectionCalculator
{
    public TeamProjection Project(FantasyTeam team, decimal currentBudget)
    {
        ArgumentNullException.ThrowIfNull(team);

        var totalCost = team.Assets.Sum(asset => asset.Price);
        var expectedPoints = team.Assets.Sum(asset => asset.ExpectedPoints);
        var expectedBudgetChange = team.Assets.Sum(asset => asset.ExpectedBudgetChange);

        return new TeamProjection(
            team.Name,
            team.Drivers.Count,
            team.Constructors.Count,
            currentBudget,
            totalCost,
            currentBudget - totalCost,
            expectedPoints,
            expectedBudgetChange);
    }
}
