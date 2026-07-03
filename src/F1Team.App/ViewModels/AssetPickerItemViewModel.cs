using F1Team.Core.Models;

namespace F1Team.App.ViewModels;

public sealed class AssetPickerItemViewModel : ObservableObject
{
    private bool _isSelectedByUser;
    private bool _isSelectedByRival;
    private bool _isSelectedByCandidate;
    private bool _isAffordableForActiveSlot = true;
    private bool _isSelectable = true;

    public AssetPickerItemViewModel(FantasyAsset asset)
    {
        Asset = asset;
    }

    public FantasyAsset Asset { get; }

    public string Id => Asset.Id;

    public string Abbreviation => Asset.Abbreviation;

    public string Name => Asset.Name;

    public FantasyAssetType Type => Asset.Type;

    public decimal Price => Asset.Price;

    public decimal ExpectedPoints => Asset.ExpectedPoints;

    public decimal ExpectedBudgetChange => Asset.ExpectedBudgetChange;

    public decimal PointsPerMillion => Price == 0m ? 0m : decimal.Round(ExpectedPoints / Price, 2);

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
