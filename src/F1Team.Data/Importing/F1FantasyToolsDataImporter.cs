using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using F1Team.Core.Models;

namespace F1Team.Data.Importing;

public sealed class F1FantasyToolsDataImporter
{
    private static readonly Uri BudgetBuilderUri = new("https://f1fantasytools.com/budget-builder");
    private static readonly Uri StatisticsUri = new("https://f1fantasytools.com/statistics");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public F1FantasyToolsDataImporter()
        : this(new HttpClient())
    {
    }

    public F1FantasyToolsDataImporter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FantasyDataSet> ImportAsync(CancellationToken cancellationToken = default)
    {
        var budgetBuilderHtml = await _httpClient.GetStringAsync(BudgetBuilderUri, cancellationToken);
        var dataSet = Parse(budgetBuilderHtml, DateTimeOffset.UtcNow);

        try
        {
            var statisticsHtml = await _httpClient.GetStringAsync(StatisticsUri, cancellationToken);
            var metrics = ParseStatisticsMetrics(statisticsHtml, dataSet.RaceContext.SprintWeekend);
            return ApplyHistoricalPoints(dataSet, metrics);
        }
        catch when (HasUsableSimulationExpectedPoints(dataSet))
        {
            return dataSet;
        }
    }

    public FantasyDataSet Parse(string html, DateTimeOffset fetchedAtUtc)
    {
        var payload = DecodeNextPayload(html);
        var bbTableAssetsJson = ExtractJsonProperty(payload, "bbTableAssets");
        var currentRaceJson = ExtractJsonProperty(payload, "currentRace");
        var analystSimsJson = ExtractJsonProperty(payload, "analystSims");

        using var bbTableAssets = JsonDocument.Parse(bbTableAssetsJson);
        using var currentRace = JsonDocument.Parse(currentRaceJson);
        using var analystSims = JsonDocument.Parse(analystSimsJson);

        var primarySim = analystSims.RootElement.ValueKind == JsonValueKind.Array && analystSims.RootElement.GetArrayLength() > 0
            ? analystSims.RootElement[0]
            : default;
        var primarySimId = GetString(primarySim, "id");

        var hasSimulationData = primarySimId is not null;

        var drivers = ParseAssets(
            bbTableAssets.RootElement.GetProperty("drivers"),
            FantasyAssetType.Driver,
            primarySim,
            primarySimId,
            hasSimulationData).ToArray();

        var constructors = ParseAssets(
            bbTableAssets.RootElement.GetProperty("constructors"),
            FantasyAssetType.Constructor,
            primarySim,
            primarySimId,
            hasSimulationData).ToArray();

        var raceContext = new RaceContext(
            GetString(currentRace.RootElement, "id") ?? "",
            GetString(currentRace.RootElement, "name") ?? "Upcoming Race",
            GetInt(currentRace.RootElement, "roundNumber"),
            GetString(currentRace.RootElement, "countryName"),
            GetString(currentRace.RootElement, "city"),
            GetBool(currentRace.RootElement, "sprint"),
            GetString(primarySim, "name"),
            fetchedAtUtc);

        return new FantasyDataSet(drivers, constructors, raceContext);
    }

    public IReadOnlyDictionary<string, decimal> ParseStatisticsAverages(string html)
    {
        return ParseStatisticsMetrics(html, upcomingSprintWeekend: false).ToDictionary(
            item => item.Key,
            item => item.Value.SeasonAverage,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, HistoricalPointsMetrics> ParseStatisticsMetrics(
        string html,
        bool upcomingSprintWeekend)
    {
        var payload = DecodeNextPayload(html, requiredToken: "\"totalPoints\"");
        var scoresByAsset = new Dictionary<string, SortedDictionary<int, RaceScore>>(StringComparer.OrdinalIgnoreCase);
        var countedAssetsByRace = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raceMatches = Regex.Matches(
            payload,
            "\"(?<race>\\d+)\":\\{\"drivers\":\\[",
            RegexOptions.Singleline);

        foreach (Match match in raceMatches)
        {
            var raceKey = match.Groups["race"].Value;
            if (!int.TryParse(raceKey, out var raceNumber))
            {
                continue;
            }

            var objectStart = match.Value.IndexOf('{') + match.Index;
            using var race = JsonDocument.Parse(ExtractBalancedJson(payload, objectStart));

            AccumulateRaceScores(race.RootElement, "drivers", raceKey, raceNumber, scoresByAsset, countedAssetsByRace);
            AccumulateRaceScores(race.RootElement, "constructors", raceKey, raceNumber, scoresByAsset, countedAssetsByRace);
        }

        return scoresByAsset.ToDictionary(
            item => item.Key,
            item => CalculateMetrics(item.Value.Values, upcomingSprintWeekend),
            StringComparer.OrdinalIgnoreCase);
    }

    private static FantasyDataSet ApplyHistoricalPoints(
        FantasyDataSet dataSet,
        IReadOnlyDictionary<string, HistoricalPointsMetrics> metricsByAssetId)
    {
        var drivers = ApplyHistoricalPoints(dataSet.Drivers, metricsByAssetId).ToArray();
        var constructors = ApplyHistoricalPoints(dataSet.Constructors, metricsByAssetId).ToArray();
        return dataSet with { Drivers = drivers, Constructors = constructors };
    }

    private static bool HasUsableSimulationExpectedPoints(FantasyDataSet dataSet) =>
        dataSet.Drivers.Concat(dataSet.Constructors).Any(asset => asset.SimulationExpectedPoints > 0m);

    private static IEnumerable<FantasyAsset> ApplyHistoricalPoints(
        IEnumerable<FantasyAsset> assets,
        IReadOnlyDictionary<string, HistoricalPointsMetrics> metricsByAssetId)
    {
        foreach (var asset in assets)
        {
            if (!metricsByAssetId.TryGetValue(asset.Id, out var metrics))
            {
                yield return asset;
                continue;
            }

            var expectedPoints = asset.SimulationExpectedPoints is null
                ? metrics.SeasonAverage
                : asset.ExpectedPoints;

            yield return asset with
            {
                ExpectedPoints = expectedPoints,
                AverageRacePoints = metrics.SeasonAverage,
                LastFiveRacePoints = metrics.LastFiveAverage,
                TrimmedAverageRacePoints = metrics.TrimmedSeasonAverage,
                TrimmedLastFiveRacePoints = metrics.TrimmedLastFiveAverage,
                HistoricalPointsStandardDeviation = metrics.StandardDeviation,
                AverageSprintPoints = metrics.AverageSprintPoints,
                HistoricalRaceCount = metrics.RaceCount,
                LastFiveRaceCount = metrics.LastFiveRaceCount,
                HistoricalSprintRaceCount = metrics.SprintRaceCount
            };
        }
    }

    private static void AccumulateRaceScores(
        JsonElement race,
        string assetGroupName,
        string raceKey,
        int raceNumber,
        Dictionary<string, SortedDictionary<int, RaceScore>> scoresByAsset,
        HashSet<string> countedAssetsByRace)
    {
        if (!race.TryGetProperty(assetGroupName, out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var id = GetString(asset, "id");
            if (string.IsNullOrWhiteSpace(id) ||
                !asset.TryGetProperty("totalPoints", out var totalPoints) ||
                totalPoints.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var countedKey = $"{raceKey}:{id}";
            if (!countedAssetsByRace.Add(countedKey))
            {
                continue;
            }

            if (!scoresByAsset.TryGetValue(id, out var scores))
            {
                scores = new SortedDictionary<int, RaceScore>();
                scoresByAsset[id] = scores;
            }

            var sprint = GetSprintResult(asset);
            scores[raceNumber] = new RaceScore(totalPoints.GetDecimal() - sprint.Points, sprint.Points, sprint.IsSprint);
        }
    }

    private static HistoricalPointsMetrics CalculateMetrics(
        IEnumerable<RaceScore> raceScores,
        bool upcomingSprintWeekend)
    {
        var scores = raceScores.ToArray();
        var basePoints = scores.Select(score => score.BasePoints).ToArray();
        var lastFiveBasePoints = basePoints.TakeLast(5).ToArray();
        var sprintPoints = scores.Where(score => score.IsSprint).Select(score => score.SprintPoints).ToArray();
        var sprintUplift = upcomingSprintWeekend && sprintPoints.Length > 0 ? Average(sprintPoints) : 0m;

        return new HistoricalPointsMetrics(
            Round(Average(basePoints) + sprintUplift),
            Round(Average(lastFiveBasePoints) + sprintUplift),
            Round(TrimmedAverage(basePoints) + sprintUplift),
            Round(TrimmedAverage(lastFiveBasePoints) + sprintUplift),
            Round(StandardDeviation(basePoints)),
            Round(sprintPoints.Length > 0 ? Average(sprintPoints) : 0m),
            basePoints.Length,
            lastFiveBasePoints.Length,
            sprintPoints.Length);
    }

    private static (bool IsSprint, decimal Points) GetSprintResult(JsonElement asset)
    {
        if (!asset.TryGetProperty("raceResult", out var raceResult) ||
            !raceResult.TryGetProperty("S", out var sprintResult))
        {
            return (false, 0m);
        }

        if (!sprintResult.TryGetProperty("totalPoints", out var totalPoints) ||
            !totalPoints.TryGetProperty("points", out var points) ||
            points.ValueKind != JsonValueKind.Number)
        {
            return (true, 0m);
        }

        return (true, points.GetDecimal());
    }

    private static decimal TrimmedAverage(IReadOnlyCollection<decimal> values)
    {
        var trimCount = values.Count switch
        {
            >= 11 => 2,
            >= 5 => 1,
            _ => 0
        };

        return Average(values.OrderBy(value => value).Skip(trimCount).Take(values.Count - (trimCount * 2)));
    }

    private static decimal Average(IEnumerable<decimal> values)
    {
        var materialized = values as decimal[] ?? values.ToArray();
        return materialized.Length == 0 ? 0m : materialized.Average();
    }

    private static decimal StandardDeviation(IReadOnlyCollection<decimal> values)
    {
        if (values.Count < 2)
        {
            return 0m;
        }

        var average = values.Average();
        var variance = values.Average(value => (value - average) * (value - average));
        return (decimal)Math.Sqrt((double)variance);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 1, MidpointRounding.AwayFromZero);

    private sealed record RaceScore(decimal BasePoints, decimal SprintPoints, bool IsSprint);

    private static IEnumerable<FantasyAsset> ParseAssets(
        JsonElement assets,
        FantasyAssetType assetType,
        JsonElement primarySim,
        string? primarySimId,
        bool hasSimulationData)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var id = GetString(asset, "id") ?? "";
            var abbreviation = GetString(asset, "abbreviation") ?? id;
            var simulationExpectedPoints = GetSimulationExpectedPoints(asset, primarySimId);
            var expectedPoints = simulationExpectedPoints ?? 0m;
            var expectedBudgetChange = GetExpectedBudgetChange(primarySim, assetType, id);

            yield return new FantasyAsset(
                id,
                abbreviation,
                assetType,
                GetDecimal(asset, "price"),
                expectedPoints,
                expectedBudgetChange,
                DisplayName: abbreviation,
                TeamColor: GetString(asset, "color"),
                PriorRacePoints: GetNullableDecimal(asset, "priorRacePoints"),
                PriorPriorRacePoints: GetNullableDecimal(asset, "priorPriorRacePoints"),
                RequiredPoints: ParseRequiredPoints(asset),
                PriceChangeOdds: ParsePriceChangeOdds(asset, primarySimId),
                SimulationExpectedPoints: hasSimulationData ? simulationExpectedPoints : null);
        }
    }

    private static RequiredPoints? ParseRequiredPoints(JsonElement asset)
    {
        if (!asset.TryGetProperty("minRequiredPoints", out var required))
        {
            return null;
        }

        return new RequiredPoints(
            GetNullableDecimal(required, "TERRIBLE"),
            GetNullableDecimal(required, "POOR"),
            GetNullableDecimal(required, "GOOD"),
            GetNullableDecimal(required, "GREAT"));
    }

    private static PriceChangeOdds? ParsePriceChangeOdds(JsonElement asset, string? primarySimId)
    {
        if (primarySimId is null ||
            !asset.TryGetProperty("simulationOddsPerPriceChangeLevel", out var oddsBySim) ||
            !oddsBySim.TryGetProperty(primarySimId, out var odds))
        {
            return null;
        }

        return new PriceChangeOdds(
            GetNullableDecimal(odds, "TERRIBLE"),
            GetNullableDecimal(odds, "POOR"),
            GetNullableDecimal(odds, "GOOD"),
            GetNullableDecimal(odds, "GREAT"));
    }

    private static decimal? GetSimulationExpectedPoints(JsonElement asset, string? primarySimId)
    {
        if (primarySimId is null ||
            !asset.TryGetProperty("simulationExpectedPoints", out var expectedBySim) ||
            !expectedBySim.TryGetProperty(primarySimId, out var expected))
        {
            return null;
        }

        return expected.GetDecimal();
    }

    private static decimal GetExpectedBudgetChange(JsonElement primarySim, FantasyAssetType assetType, string assetId)
    {
        if (primarySim.ValueKind != JsonValueKind.Object)
        {
            return 0m;
        }

        var groupName = assetType == FantasyAssetType.Driver ? "drivers" : "constructors";
        if (!primarySim.TryGetProperty(groupName, out var group) ||
            !group.TryGetProperty("price_change_probability", out var probabilitiesByAsset) ||
            !probabilitiesByAsset.TryGetProperty(assetId, out var probabilities))
        {
            return 0m;
        }

        var expectedChange = 0m;
        foreach (var probability in probabilities.EnumerateObject())
        {
            var change = ParsePriceChangeKey(probability.Name);
            expectedChange += change * probability.Value.GetDecimal() / 100m;
        }

        return decimal.Round(expectedChange, 3);
    }

    private static decimal ParsePriceChangeKey(string key)
    {
        if (key.Length < 3)
        {
            return 0m;
        }

        var sign = key.StartsWith("pm", StringComparison.Ordinal) ? -1m : 1m;
        if (!int.TryParse(key[2..], out var tenths))
        {
            return 0m;
        }

        return sign * tenths / 10m;
    }

    private static string DecodeNextPayload(string html, string requiredToken = "\"bbTableAssets\"")
    {
        var builder = new StringBuilder();
        var matches = Regex.Matches(
            html,
            @"self\.__next_f\.push\(\[1,""(?<payload>(?:\\.|[^""\\])*)""\]\)",
            RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            var encoded = match.Groups["payload"].Value;
            var decoded = JsonSerializer.Deserialize<string>($"\"{encoded}\"", JsonOptions);
            builder.Append(decoded);
        }

        var payload = builder.ToString();
        if (!payload.Contains(requiredToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Could not find the expected data in the page payload.");
        }

        return payload;
    }

    private static string ExtractJsonProperty(string payload, string propertyName)
    {
        var token = $"\"{propertyName}\":";
        var tokenIndex = payload.IndexOf(token, StringComparison.Ordinal);
        if (tokenIndex < 0)
        {
            throw new InvalidOperationException($"Could not find '{propertyName}' in the page payload.");
        }

        var valueStart = tokenIndex + token.Length;
        while (valueStart < payload.Length && char.IsWhiteSpace(payload[valueStart]))
        {
            valueStart++;
        }

        return ExtractBalancedJson(payload, valueStart);
    }

    private static string ExtractBalancedJson(string text, int startIndex)
    {
        var opening = text[startIndex];
        var closing = opening switch
        {
            '{' => '}',
            '[' => ']',
            _ => throw new InvalidOperationException("Expected a JSON object or array.")
        };

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = startIndex; index < text.Length; index++)
        {
            var current = text[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (current == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (current == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (current == opening)
            {
                depth++;
            }
            else if (current == closing)
            {
                depth--;
                if (depth == 0)
                {
                    return text[startIndex..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException("Could not extract a balanced JSON value.");
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetInt32() : 0;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.GetBoolean();
    }

    private static decimal GetDecimal(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value.GetDecimal() : 0m;
    }

    private static decimal? GetNullableDecimal(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;
    }
}
