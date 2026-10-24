using System.Text.Json;
using F1Team.Data.Importing;

namespace F1Team.Core.Tests;

public class F1FantasyToolsDataImporterTests
{
    private readonly F1FantasyToolsDataImporter _importer = new();

    [Fact]
    public void ParseStatisticsAverages_AveragesAssetTotalPointsAcrossRaces()
    {
        var statisticsPayload = """
            1:["$",{"1":{"drivers":[{"id":"MCL_NOR","totalPoints":40},{"id":"MCL_PIA","totalPoints":20}],"constructors":[{"id":"MCL","totalPoints":70}]},"2":{"drivers":[{"id":"MCL_NOR","totalPoints":20},{"id":"MCL_PIA","totalPoints":30}],"constructors":[{"id":"MCL","totalPoints":50}]}}]
            """;
        var html = CreateNextPayloadHtml(statisticsPayload);

        var averages = _importer.ParseStatisticsAverages(html);

        Assert.Equal(30.0m, averages["MCL_NOR"]);
        Assert.Equal(25.0m, averages["MCL_PIA"]);
        Assert.Equal(60.0m, averages["MCL"]);
    }

    [Fact]
    public void ParseStatisticsMetrics_CalculatesLastFiveAndTrimmedAverages()
    {
        var rounds = new[] { 1m, 2m, 3m, 4m, 5m, 100m }
            .Select((points, index) =>
                $"\"{index + 1}\":{{\"drivers\":[{{\"id\":\"MCL_NOR\",\"totalPoints\":{points}}}],\"constructors\":[]}}");
        var html = CreateNextPayloadHtml($"1:[\"$\",{{{string.Join(',', rounds)}}}]");

        var metrics = _importer.ParseStatisticsMetrics(html, upcomingSprintWeekend: false)["MCL_NOR"];

        Assert.Equal(19.2m, metrics.SeasonAverage);
        Assert.Equal(22.8m, metrics.LastFiveAverage);
        Assert.Equal(3.5m, metrics.TrimmedSeasonAverage);
        Assert.Equal(4.0m, metrics.TrimmedLastFiveAverage);
        Assert.Equal(6, metrics.RaceCount);
        Assert.Equal(5, metrics.LastFiveRaceCount);
    }

    [Fact]
    public void ParseStatisticsMetrics_RemovesTwoBestAndWorstAfterTenRaces()
    {
        var rounds = Enumerable.Range(0, 11)
            .Select((points, index) =>
                $"\"{index + 1}\":{{\"drivers\":[{{\"id\":\"MCL_NOR\",\"totalPoints\":{points}}}],\"constructors\":[]}}");
        var html = CreateNextPayloadHtml($"1:[\"$\",{{{string.Join(',', rounds)}}}]");

        var metrics = _importer.ParseStatisticsMetrics(html, upcomingSprintWeekend: false)["MCL_NOR"];

        Assert.Equal(5.0m, metrics.TrimmedSeasonAverage);
    }

    [Fact]
    public void ParseStatisticsMetrics_RemovesOnlyOneBestAndWorstAtTenRaces()
    {
        var values = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 20, 100 };
        var rounds = values.Select((points, index) =>
            $"\"{index + 1}\":{{\"drivers\":[{{\"id\":\"MCL_NOR\",\"totalPoints\":{points}}}],\"constructors\":[]}}");
        var html = CreateNextPayloadHtml($"1:[\"$\",{{{string.Join(',', rounds)}}}]");

        var metrics = _importer.ParseStatisticsMetrics(html, upcomingSprintWeekend: false)["MCL_NOR"];

        Assert.Equal(6.0m, metrics.TrimmedSeasonAverage);
    }

    [Fact]
    public void ParseStatisticsMetrics_RemovesExactSprintPointsAndAddsAverageUpliftForUpcomingSprint()
    {
        var statisticsPayload = """
            1:["$",{"1":{"drivers":[{"id":"MCL_NOR","totalPoints":20}],"constructors":[]},"2":{"drivers":[{"id":"MCL_NOR","totalPoints":30,"raceResult":{"S":{"totalPoints":{"points":10}}}}],"constructors":[]},"3":{"drivers":[{"id":"MCL_NOR","totalPoints":20,"raceResult":{"S":{"totalPoints":{"points":0}}}}],"constructors":[]}}]
            """;
        var html = CreateNextPayloadHtml(statisticsPayload);

        var normalWeekend = _importer.ParseStatisticsMetrics(html, upcomingSprintWeekend: false)["MCL_NOR"];
        var sprintWeekend = _importer.ParseStatisticsMetrics(html, upcomingSprintWeekend: true)["MCL_NOR"];

        Assert.Equal(20.0m, normalWeekend.SeasonAverage);
        Assert.Equal(0.0m, normalWeekend.StandardDeviation);
        Assert.Equal(5.0m, normalWeekend.AverageSprintPoints);
        Assert.Equal(2, normalWeekend.SprintRaceCount);
        Assert.Equal(25.0m, sprintWeekend.SeasonAverage);
        Assert.Equal(25.0m, sprintWeekend.LastFiveAverage);
    }

    [Fact]
    public void ParseBoxBoxPredictions_ParsesDriverAndMapsConstructorIds()
    {
        var json = """
            {
              "season": 2026,
              "round": 20,
              "race": "United States Grand Prix",
              "phase": "pre_fp",
              "generated_at": "2026-10-13T00:04:02.433905+00:00",
              "drivers": [{
                "driver_id": "VER",
                "expected_points": 26.1,
                "projected_points": 39.3,
                "risk_rating": 13.6,
                "confidence": 60,
                "mc_total_std": 18.6,
                "mc_total_p5": -11,
                "mc_total_p95": 51
              }],
              "constructors": [{
                "constructor_id": "red_bull",
                "expected_points": 46.6,
                "projected_points": 61.7,
                "risk_rating": 13.6,
                "confidence": 60,
                "mc_total_std": 22.7,
                "mc_total_p5": 5.8,
                "mc_total_p95": 80
              }]
            }
            """;

        var result = _importer.ParseBoxBoxPredictions(json);

        Assert.Equal(20, result.Round);
        Assert.Equal("pre_fp", result.Phase);
        Assert.Equal(26.1m, result.Drivers["VER"].ExpectedPoints);
        Assert.Equal(46.6m, result.Constructors["RED"].ExpectedPoints);
        Assert.Equal(22.7m, result.Constructors["RED"].SimulationStandardDeviation);
    }

    [Fact]
    public void Parse_StoresSimulationExpectedPointsWhenPresent()
    {
        var budgetBuilderPayload = """
            {"bbTableAssets":{"drivers":[{"id":"MCL_NOR","abbreviation":"NOR","price":29.4,"simulationExpectedPoints":{"sim-1":45.8}}],"constructors":[{"id":"MCL","abbreviation":"MCL","price":34.2,"simulationExpectedPoints":{"sim-1":78.0}}]},"currentRace":{"id":"race-1","name":"British Grand Prix","roundNumber":12,"countryName":"United Kingdom","city":"Silverstone","sprint":false},"analystSims":[{"id":"sim-1","name":"Rhter simulation","drivers":{"price_change_probability":{}},"constructors":{"price_change_probability":{}}}]}
            """;
        var html = CreateNextPayloadHtml(budgetBuilderPayload);

        var dataSet = _importer.Parse(html, DateTimeOffset.UnixEpoch);

        Assert.Equal(45.8m, dataSet.Drivers.Single().ExpectedPoints);
        Assert.Equal(45.8m, dataSet.Drivers.Single().SimulationExpectedPoints);
        Assert.Equal(78.0m, dataSet.Constructors.Single().ExpectedPoints);
        Assert.Equal(78.0m, dataSet.Constructors.Single().SimulationExpectedPoints);
    }

    private static string CreateNextPayloadHtml(string payload)
    {
        var encoded = JsonSerializer.Serialize(payload);
        return $"""<script>self.__next_f.push([1,{encoded}])</script>""";
    }
}
