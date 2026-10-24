using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using F1Team.Core.Models;

namespace F1Team.Data.Importing;

public sealed class F1FantasyToolsDataImporter
{
    private static readonly Uri BudgetBuilderUri = new("https://f1fantasytools.com/budget-builder");
    private static readonly Uri StatisticsUri = new("https://f1fantasytools.com/statistics");
    private static readonly Uri BoxBoxPredictionsUri = new("https://boxboxf1fantasy.com/data/predictions.json");
    private static readonly Uri BoxBoxHistoryUri = new("https://boxboxf1fantasy.com/data/driver_history.json");
    private static readonly Uri BoxBoxPicksIndexUri = new("https://boxboxf1fantasy.com/picks/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public string LastBoxBoxDiagnostic { get; private set; } = "Not requested.";

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
            dataSet = ApplyHistoricalPoints(dataSet, metrics);
        }
        catch when (HasUsableSimulationExpectedPoints(dataSet))
        {
        }

        try
        {
            LastBoxBoxDiagnostic = "Requesting predictions...";
            using var request = new HttpRequestMessage(HttpMethod.Get, BoxBoxPredictionsUri);
            request.Headers.UserAgent.ParseAdd("F1Team/1.0 (+https://boxboxf1fantasy.com/methodology/)");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var boxBoxJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var boxBox = ParseBoxBoxPredictions(boxBoxJson);
            dataSet = ApplyBoxBoxPredictions(dataSet, boxBox);
            var appliedCount = dataSet.Drivers.Concat(dataSet.Constructors)
                .Count(asset => asset.BoxBoxExpectedPoints is not null);
            LastBoxBoxDiagnostic = $"Loaded HTTP {(int)response.StatusCode}; {boxBox.Drivers.Count} drivers, {boxBox.Constructors.Count} constructors; applied {appliedCount} assets; feed round {boxBox.Round}.";
            WriteBoxBoxDiagnostic(LastBoxBoxDiagnostic);
        }
        catch (Exception ex)
        {
            // BoxBox is an optional model source; a feed outage must not block the primary import.
            LastBoxBoxDiagnostic = $"Failed: {ex.GetType().Name}: {ex.Message}";
            WriteBoxBoxDiagnostic(LastBoxBoxDiagnostic);
        }

        return dataSet;
    }

    private static void WriteBoxBoxDiagnostic(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "F1Team");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "boxbox-diagnostics.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never prevent the data refresh from completing.
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

    public BoxBoxPredictionData ParseBoxBoxPredictions(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var drivers = ParseBoxBoxAssets(root, "drivers", isConstructor: false);
        var constructors = ParseBoxBoxAssets(root, "constructors", isConstructor: true);
        DateTimeOffset? generatedAt = DateTimeOffset.TryParse(GetString(root, "generated_at"), out var parsedGeneratedAt)
            ? parsedGeneratedAt
            : null;

        return new BoxBoxPredictionData(
            GetInt(root, "season"),
            GetInt(root, "round"),
            GetString(root, "race") ?? "",
            GetString(root, "phase"),
            generatedAt,
            drivers,
            constructors);
    }

    public async Task<IReadOnlyList<BoxBoxHistoryRow>> ImportBoxBoxHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BoxBoxHistoryUri);
        request.Headers.UserAgent.ParseAdd("F1Team/1.0 (+https://boxboxf1fantasy.com/methodology/)");
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ParseBoxBoxHistory(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public IReadOnlyList<BoxBoxHistoryRow> ParseBoxBoxHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var rows = new List<BoxBoxHistoryRow>();
        ParseHistoryGroup(root, "drivers", isConstructor: false, rows);
        ParseHistoryGroup(root, "constructors", isConstructor: true, rows);
        return rows.OrderByDescending(row => row.Round).ThenBy(row => row.AssetType).ThenBy(row => row.AssetId).ToArray();
    }

    public IReadOnlyList<BoxBoxArchivePrediction> ParseBoxBoxArchivePredictions(string html)
    {
        var roundMatch = Regex.Match(html, @"\bround\s+(?<round>\d+)\b", RegexOptions.IgnoreCase);
        if (!roundMatch.Success || !int.TryParse(roundMatch.Groups["round"].Value, out var round))
        {
            return Array.Empty<BoxBoxArchivePrediction>();
        }

        var results = new List<BoxBoxArchivePrediction>();
        ParseArchiveTable(html, "Archived driver projections", "Driver", round, results);
        ParseArchiveTable(html, "Archived constructor projections", "Constructor", round, results);
        return results;
    }

    public async Task<IReadOnlyList<BoxBoxArchivePrediction>> ImportBoxBoxArchivePredictionsAsync(
        CancellationToken cancellationToken = default)
    {
        var indexHtml = await _httpClient.GetStringAsync(BoxBoxPicksIndexUri, cancellationToken);
        var urls = Regex.Matches(indexHtml, "(?:href=\"|https://boxboxf1fantasy\\.com)(?<url>/picks/[^\"'#?]+)", RegexOptions.IgnoreCase)
            .Cast<Match>()
            .Select(match => match.Groups["url"].Value)
            .Where(url => !url.Equals("/picks/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new Uri(new Uri(BoxBoxPicksIndexUri, "/"), path.TrimStart('/')))
            .ToArray();

        var results = new List<BoxBoxArchivePrediction>();
        foreach (var url in urls)
        {
            try
            {
                results.AddRange(ParseBoxBoxArchivePredictions(
                    await _httpClient.GetStringAsync(url, cancellationToken)));
            }
            catch
            {
                // One unavailable archive must not hide the remaining completed rounds.
            }
        }

        return results
            .GroupBy(item => (item.Round, item.AssetType, item.AssetId))
            .Select(group => group.First())
            .ToArray();
    }

    private static void ParseArchiveTable(
        string html,
        string heading,
        string assetType,
        int round,
        ICollection<BoxBoxArchivePrediction> results)
    {
        var headingIndex = html.IndexOf(heading, StringComparison.OrdinalIgnoreCase);
        if (headingIndex < 0)
        {
            return;
        }

        var tableEnd = html.IndexOf("</table>", headingIndex, StringComparison.OrdinalIgnoreCase);
        if (tableEnd < 0)
        {
            return;
        }

        var table = html[headingIndex..tableEnd];
        foreach (Match rowMatch in Regex.Matches(table, @"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var row = rowMatch.Groups["row"].Value;
            var slugMatch = Regex.Match(row, assetType == "Driver"
                ? "/drivers/(?<slug>[^/\"']+)"
                : "/constructors/(?<slug>[^/\"']+)", RegexOptions.IgnoreCase);
            if (!slugMatch.Success)
            {
                continue;
            }

            var cells = Regex.Matches(row, @"<td\b[^>]*>(?<value>.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Cast<Match>()
                .Select(match => Regex.Replace(match.Groups["value"].Value, "<[^>]+>", string.Empty).Trim())
                .ToArray();
            if (cells.Length < 6 || !decimal.TryParse(
                    Regex.Replace(cells[5], @"[^0-9+\-.]", string.Empty),
                    out var projectedPoints))
            {
                continue;
            }

            var assetId = assetType == "Driver"
                ? MapDriverSlug(slugMatch.Groups["slug"].Value)
                : MapConstructorId(slugMatch.Groups["slug"].Value);
            if (!string.IsNullOrWhiteSpace(assetId))
            {
                results.Add(new BoxBoxArchivePrediction(round, assetId, assetType, projectedPoints));
            }
        }
    }

    private static string? MapDriverSlug(string slug) => slug.ToLowerInvariant() switch
    {
        "max-verstappen" => "VER",
        "george-russell" => "RUS",
        "charles-leclerc" => "LEC",
        "kimi-antonelli" => "ANT",
        "lewis-hamilton" => "HAM",
        "lando-norris" => "NOR",
        "oscar-piastri" => "PIA",
        "isack-hadjar" => "HAD",
        "liam-lawson" => "LAW",
        "esteban-ocon" => "OCO",
        "fernando-alonso" => "ALO",
        "lance-stroll" => "STR",
        "nico-hulkenberg" => "HUL",
        "franco-colapinto" => "COL",
        "oliver-bearman" => "BEA",
        "carlos-sainz" => "SAI",
        "alex-albon" => "ALB",
        "gabriel-bortoleto" => "BOR",
        "pierre-gasly" => "GAS",
        "valtteri-bottas" => "BOT",
        "arvid-lindblad" => "LIN",
        "sergio-perez" => "PER",
        _ => null
    };

    private static void ParseHistoryGroup(
        JsonElement root,
        string propertyName,
        bool isConstructor,
        ICollection<BoxBoxHistoryRow> rows)
    {
        if (!root.TryGetProperty(propertyName, out var group) || group.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var asset in group.EnumerateObject())
        {
            var id = isConstructor ? MapConstructorId(asset.Name) : asset.Name;
            if (!asset.Value.TryGetProperty("rounds", out var rounds) || rounds.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var round in rounds.EnumerateArray())
            {
                if (!round.TryGetProperty("round", out var roundValue) ||
                    !round.TryGetProperty("points", out var pointsValue))
                {
                    continue;
                }

                rows.Add(new BoxBoxHistoryRow(
                    roundValue.GetInt32(),
                    id ?? asset.Name,
                    isConstructor ? "Constructor" : "Driver",
                    pointsValue.GetDecimal(),
                    !isConstructor && GetBool(round, "is_dnf"),
                    GetString(round, "circuit_id")));
            }
        }
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

    private static FantasyDataSet ApplyBoxBoxPredictions(
        FantasyDataSet dataSet,
        BoxBoxPredictionData predictions)
    {
        // BoxBox has occasionally been out of sync with the official round number.
        // Match the race name as well so a valid prediction is not discarded solely
        // because the provider's calendar numbering is stale or different.
        var sameRace = string.Equals(
            NormalizeRaceName(predictions.RaceName),
            NormalizeRaceName(dataSet.RaceContext.RaceName),
            StringComparison.OrdinalIgnoreCase);
        if (predictions.Round != dataSet.RaceContext.RoundNumber && !sameRace)
        {
            return dataSet;
        }

        return dataSet with
        {
            Drivers = ApplyBoxBoxPredictions(dataSet.Drivers, predictions.Drivers).ToArray(),
            Constructors = ApplyBoxBoxPredictions(dataSet.Constructors, predictions.Constructors).ToArray()
        };
    }

    private static string NormalizeRaceName(string? raceName) =>
        string.IsNullOrWhiteSpace(raceName)
            ? string.Empty
            : Regex.Replace(raceName.Trim().ToLowerInvariant(), "\\s+", " ");

    private static IEnumerable<FantasyAsset> ApplyBoxBoxPredictions(
        IEnumerable<FantasyAsset> assets,
        IReadOnlyDictionary<string, BoxBoxAssetPrediction> predictions)
    {
        foreach (var asset in assets)
        {
            if (!predictions.TryGetValue(asset.Id, out var prediction))
            {
                // Driver IDs in the BoxBox feed are abbreviations (for example
                // VER), while the app's canonical driver IDs include the team
                // prefix (for example RED_VER).
                var matchingPrediction = predictions.FirstOrDefault(pair =>
                    string.Equals(pair.Key, asset.Abbreviation, StringComparison.OrdinalIgnoreCase) ||
                    asset.Id.EndsWith($"_{pair.Key}", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(matchingPrediction.Key))
                {
                    yield return asset;
                    continue;
                }

                prediction = matchingPrediction.Value;
            }

            yield return asset with
            {
                BoxBoxExpectedPoints = prediction.ExpectedPoints,
                BoxBoxProjectedPoints = prediction.ProjectedPoints,
                BoxBoxRiskRating = prediction.RiskRating,
                BoxBoxConfidence = prediction.Confidence,
                BoxBoxSimulationStandardDeviation = prediction.SimulationStandardDeviation,
                BoxBoxSimulationP5 = prediction.SimulationP5,
                BoxBoxSimulationP95 = prediction.SimulationP95
            };
        }
    }

    private static IReadOnlyDictionary<string, BoxBoxAssetPrediction> ParseBoxBoxAssets(
        JsonElement root,
        string propertyName,
        bool isConstructor)
    {
        var result = new Dictionary<string, BoxBoxAssetPrediction>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(propertyName, out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var sourceId = GetString(asset, isConstructor ? "constructor_id" : "asset_id") ??
                           GetString(asset, isConstructor ? "name" : "driver_id");
            var id = isConstructor ? MapConstructorId(sourceId) : sourceId;
            if (string.IsNullOrWhiteSpace(id) || !asset.TryGetProperty("expected_points", out _))
            {
                continue;
            }

            result[id] = new BoxBoxAssetPrediction(
                GetDecimal(asset, "expected_points"),
                GetDecimal(asset, "projected_points"),
                GetNullableDecimal(asset, "risk_rating"),
                GetNullableDecimal(asset, "confidence"),
                GetNullableDecimal(asset, "mc_total_std"),
                GetNullableDecimal(asset, "mc_total_p5"),
                GetNullableDecimal(asset, "mc_total_p95"));
        }

        return result;
    }

    private static string? MapConstructorId(string? id) => id?.ToLowerInvariant() switch
    {
        "alpine" => "ALP",
        "aston_martin" => "AST",
        "audi" => "AUD",
        "cadillac" => "CAD",
        "ferrari" => "FER",
        "haas" => "HAA",
        "mclaren" => "MCL",
        "mercedes" => "MER",
        "racing_bulls" => "VRB",
        "red_bull" => "RED",
        "williams" => "WIL",
        _ => id
    };

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
