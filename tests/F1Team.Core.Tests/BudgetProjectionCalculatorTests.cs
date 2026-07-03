using F1Team.Core.Calculations;
using F1Team.Core.Models;

namespace F1Team.Core.Tests;

public class BudgetProjectionCalculatorTests
{
    private readonly BudgetProjectionCalculator _calculator = new();

    [Fact]
    public void Project_SumsCostAndRemainingBudget()
    {
        var team = new FantasyTeam("My Team", new[]
        {
            Driver("NOR", 29.4m),
            Driver("PIA", 26.1m),
            Constructor("MCL", 34.2m)
        });

        var projection = _calculator.Project(team, currentBudget: 100.0m);

        Assert.Equal(89.7m, projection.TotalCost);
        Assert.Equal(10.3m, projection.RemainingBudget);
        Assert.False(projection.IsOverBudget);
    }

    [Fact]
    public void Project_MarksTeamOverBudgetWhenRemainingBudgetIsNegative()
    {
        var team = new FantasyTeam("Spicy Team", new[]
        {
            Driver("VER", 30.0m),
            Driver("NOR", 29.0m),
            Constructor("MCL", 42.0m)
        });

        var projection = _calculator.Project(team, currentBudget: 100.0m);

        Assert.Equal(-1.0m, projection.RemainingBudget);
        Assert.True(projection.IsOverBudget);
    }

    [Fact]
    public void Project_SumsExpectedPointsAndBudgetChange()
    {
        var team = new FantasyTeam("Projection Team", new[]
        {
            Driver("RUS", 19.2m, expectedPoints: 37.5m, expectedBudgetChange: 0.2m),
            Driver("ANT", 13.1m, expectedPoints: 21.0m, expectedBudgetChange: 0.4m),
            Constructor("MER", 29.0m, expectedPoints: 65.25m, expectedBudgetChange: 0.1m)
        });

        var projection = _calculator.Project(team, currentBudget: 100.0m);

        Assert.Equal(123.75m, projection.ExpectedPoints);
        Assert.Equal(0.7m, projection.ExpectedBudgetChange);
    }

    [Fact]
    public void Project_DetectsCompleteFantasyTeamShape()
    {
        var team = new FantasyTeam("Complete Team", new[]
        {
            Driver("D1", 10m),
            Driver("D2", 10m),
            Driver("D3", 10m),
            Driver("D4", 10m),
            Driver("D5", 10m),
            Constructor("C1", 15m),
            Constructor("C2", 15m)
        });

        var projection = _calculator.Project(team, currentBudget: 100.0m);

        Assert.Equal(5, projection.DriverCount);
        Assert.Equal(2, projection.ConstructorCount);
        Assert.True(projection.IsComplete);
    }

    [Fact]
    public void Project_DetectsIncompleteFantasyTeamShape()
    {
        var team = new FantasyTeam("Incomplete Team", new[]
        {
            Driver("D1", 10m),
            Driver("D2", 10m),
            Constructor("C1", 15m)
        });

        var projection = _calculator.Project(team, currentBudget: 100.0m);

        Assert.False(projection.IsComplete);
    }

    [Fact]
    public void FantasyAsset_UsesAbbreviationWhenDisplayNameIsMissing()
    {
        var asset = Driver("HAM", 18.0m);

        Assert.Equal("HAM", asset.Name);
    }

    [Fact]
    public void FantasyAsset_UsesDisplayNameWhenProvided()
    {
        var asset = Driver("HAM", 18.0m) with { DisplayName = "Lewis Hamilton" };

        Assert.Equal("Lewis Hamilton", asset.Name);
    }

    private static FantasyAsset Driver(
        string abbreviation,
        decimal price,
        decimal expectedPoints = 0m,
        decimal expectedBudgetChange = 0m)
    {
        return Asset(abbreviation, FantasyAssetType.Driver, price, expectedPoints, expectedBudgetChange);
    }

    private static FantasyAsset Constructor(
        string abbreviation,
        decimal price,
        decimal expectedPoints = 0m,
        decimal expectedBudgetChange = 0m)
    {
        return Asset(abbreviation, FantasyAssetType.Constructor, price, expectedPoints, expectedBudgetChange);
    }

    private static FantasyAsset Asset(
        string abbreviation,
        FantasyAssetType type,
        decimal price,
        decimal expectedPoints,
        decimal expectedBudgetChange)
    {
        return new FantasyAsset(
            abbreviation,
            abbreviation,
            type,
            price,
            expectedPoints,
            expectedBudgetChange);
    }
}
