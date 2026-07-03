using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using F1Team.Core.Models;

namespace F1Team.Data.Importing;

public sealed class F1FantasyToolsDataImporter
{
    private static readonly Uri BudgetBuilderUri = new("https://f1fantasytools.com/budget-builder");
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
        var html = await _httpClient.GetStringAsync(BudgetBuilderUri, cancellationToken);
        return Parse(html, DateTimeOffset.UtcNow);
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

        var drivers = ParseAssets(
            bbTableAssets.RootElement.GetProperty("drivers"),
            FantasyAssetType.Driver,
            primarySim,
            primarySimId).ToArray();

        var constructors = ParseAssets(
            bbTableAssets.RootElement.GetProperty("constructors"),
            FantasyAssetType.Constructor,
            primarySim,
            primarySimId).ToArray();

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

    private static IEnumerable<FantasyAsset> ParseAssets(
        JsonElement assets,
        FantasyAssetType assetType,
        JsonElement primarySim,
        string? primarySimId)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            var id = GetString(asset, "id") ?? "";
            var abbreviation = GetString(asset, "abbreviation") ?? id;
            var expectedPoints = GetSimulationExpectedPoints(asset, primarySimId);
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
                PriceChangeOdds: ParsePriceChangeOdds(asset, primarySimId));
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

    private static decimal GetSimulationExpectedPoints(JsonElement asset, string? primarySimId)
    {
        if (primarySimId is null ||
            !asset.TryGetProperty("simulationExpectedPoints", out var expectedBySim) ||
            !expectedBySim.TryGetProperty(primarySimId, out var expected))
        {
            return 0m;
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

    private static string DecodeNextPayload(string html)
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
        if (!payload.Contains("\"bbTableAssets\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Could not find Budget Builder data in the page payload.");
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
