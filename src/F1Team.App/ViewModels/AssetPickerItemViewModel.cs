using System.Globalization;
using F1Team.Core.Models;

namespace F1Team.App.ViewModels;

public sealed class AssetPickerItemViewModel : ObservableObject
{
    private bool _isSelectedByUser;
    private bool _isSelectedByRival;
    private bool _isSelectedByCandidate;
    private bool _isAffordableForActiveSlot = true;
    private bool _isSelectable = true;
    private readonly string? _volatilityText;

    public AssetPickerItemViewModel(FantasyAsset asset, string? volatilityText = null)
    {
        Asset = asset;
        _volatilityText = volatilityText;
    }

    public FantasyAsset Asset { get; }

    public string Id => Asset.Id;

    public string Abbreviation => Asset.Abbreviation;

    public string Name => Asset.Name;

    /// <summary>
    /// The abbreviation shown as a muted subtitle only when it differs from the display name,
    /// so imported data (where name == abbreviation) does not repeat the same text twice.
    /// </summary>
    public string SubtitleAbbreviation =>
        string.Equals(Name, Abbreviation, StringComparison.OrdinalIgnoreCase) ? string.Empty : Abbreviation;

    public FantasyAssetType Type => Asset.Type;

    public decimal Price => Asset.Price;

    public decimal ExpectedPoints => Asset.ExpectedPoints;

    public decimal ExpectedBudgetChange => Asset.ExpectedBudgetChange;

    public decimal PointsPerMillion => Price == 0m ? 0m : decimal.Round(ExpectedPoints / Price, 2);

    public bool HasDetails =>
        Asset.PriorRacePoints is not null ||
        Asset.PriorPriorRacePoints is not null ||
        Asset.RequiredPoints is not null ||
        Asset.PriceChangeOdds is not null ||
        Asset.HistoricalPointsStandardDeviation is not null;

    public string VolatilityText
    {
        get
        {
            if (Asset.HistoricalPointsStandardDeviation is null || Asset.HistoricalRaceCount < 3)
            {
                return Asset.HistoricalRaceCount > 0 ? "Low conf." : string.Empty;
            }

            return _volatilityText ?? string.Empty;
        }
    }

    public string VolatilityColor => VolatilityText switch
    {
        "Low" => "#1b7f3a",
        "Medium" => "#9a6700",
        "High" => "#b42318",
        _ => "#617086"
    };

    public string VolatilityDetailLine
    {
        get
        {
            if (Asset.HistoricalPointsStandardDeviation is null || Asset.HistoricalRaceCount == 0)
            {
                return string.Empty;
            }

            var detail = Asset.HistoricalRaceCount < 3
                ? $"Historical confidence: low ({Asset.HistoricalRaceCount} races)"
                : $"Format-adjusted volatility: {VolatilityText} relative to other {Type.ToString().ToLowerInvariant()}s (σ {Asset.HistoricalPointsStandardDeviation:0.0}, {Asset.HistoricalRaceCount} races)";

            return Asset.HistoricalSprintRaceCount > 0
                ? $"{detail}   ·   sprint uplift {Asset.AverageSprintPoints:0.0} pts"
                : detail;
        }
    }

    /// <summary>Recent form: points scored in the last two races, when available.</summary>
    public string RecentFormLine
    {
        get
        {
            var parts = new List<string>();
            if (Asset.PriorRacePoints is { } last)
            {
                parts.Add($"last race {last:0.#}");
            }

            if (Asset.PriorPriorRacePoints is { } beforeLast)
            {
                parts.Add($"race before {beforeLast:0.#}");
            }

            return parts.Count == 0 ? string.Empty : string.Join("   ·   ", parts);
        }
    }

    /// <summary>Points needed to rise a price tier (Good/Great).</summary>
    public string RequiredToRiseLine => FormatRequired("Rise", PriceChangeLevel.Good, PriceChangeLevel.Great);

    /// <summary>Points below which the price falls a tier (Poor/Terrible).</summary>
    public string RequiredToFallLine => FormatRequired("Fall risk below", PriceChangeLevel.Poor, PriceChangeLevel.Terrible);

    /// <summary>Simulation odds of each price-change level.</summary>
    public string PriceOddsLine
    {
        get
        {
            var odds = Asset.PriceChangeOdds;
            if (odds is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            AppendOdds(parts, "Great", odds.Great);
            AppendOdds(parts, "Good", odds.Good);
            AppendOdds(parts, "Poor", odds.Poor);
            AppendOdds(parts, "Terrible", odds.Terrible);
            return parts.Count == 0 ? string.Empty : string.Join("   ", parts);
        }
    }

    private string FormatRequired(string label, params PriceChangeLevel[] levels)
    {
        var required = Asset.RequiredPoints;
        if (required is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var level in levels)
        {
            if (required.ForLevel(level) is { } points)
            {
                parts.Add($"{level} {points:0.#}");
            }
        }

        return parts.Count == 0 ? string.Empty : $"{label}: {string.Join("   ·   ", parts)}";
    }

    private static void AppendOdds(List<string> parts, string label, decimal? probability)
    {
        if (probability is { } value)
        {
            parts.Add($"{label} {value.ToString("0.#", CultureInfo.InvariantCulture)}%");
        }
    }

    public bool IsSelectedByUser
    {
        get => _isSelectedByUser;
        set => SetProperty(ref _isSelectedByUser, value);
    }

    public bool IsSelectedByRival
    {
        get => _isSelectedByRival;
        set => SetProperty(ref _isSelectedByRival, value);
    }

    public bool IsSelectedByCandidate
    {
        get => _isSelectedByCandidate;
        set => SetProperty(ref _isSelectedByCandidate, value);
    }

    public bool IsAffordableForActiveSlot
    {
        get => _isAffordableForActiveSlot;
        set => SetProperty(ref _isAffordableForActiveSlot, value);
    }

    public bool IsSelectable
    {
        get => _isSelectable;
        set => SetProperty(ref _isSelectable, value);
    }

    public string SelectedMarker
    {
        get
        {
            if (IsSelectedByUser && IsSelectedByRival)
            {
                return "You + Rival";
            }

            if (IsSelectedByUser && IsSelectedByCandidate)
            {
                return "You + Candidate";
            }

            if (IsSelectedByUser)
            {
                return "You";
            }

            if (IsSelectedByRival)
            {
                return "Rival";
            }

            return IsSelectedByCandidate ? "Candidate" : "";
        }
    }

    public string AffordabilityText => IsAffordableForActiveSlot ? "OK" : "Over";

    public string BudgetChangeColor
    {
        get
        {
            if (ExpectedBudgetChange > 0m)
            {
                return "#1b7f3a";
            }

            return ExpectedBudgetChange < 0m ? "#b42318" : "#617086";
        }
    }

    public string AffordabilityColor => IsAffordableForActiveSlot ? "#1b7f3a" : "#b42318";

    public void RefreshDerivedText()
    {
        OnPropertyChanged(nameof(SelectedMarker));
        OnPropertyChanged(nameof(AffordabilityText));
        OnPropertyChanged(nameof(AffordabilityColor));
    }
}
