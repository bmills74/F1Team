using F1Team.Core.Models;

namespace F1Team.App.ViewModels;

public sealed class AssetSlotViewModel : ObservableObject
{
    public const string UserTeamKey = "User";
    public const string RivalTeamKey = "Rival";
    public const string CandidateTeamKey = "Candidate";

    private readonly Action _selectionChanged;
    private FantasyAsset? _selectedAsset;
    private bool _isActive;
    private bool _isX2Driver;

    public AssetSlotViewModel(string label, FantasyAssetType assetType, string teamKey, Action selectionChanged)
    {
        Label = label;
        AssetType = assetType;
        TeamKey = teamKey;
        _selectionChanged = selectionChanged;
    }

    public string Label { get; }

    public FantasyAssetType AssetType { get; }

    public string TeamKey { get; }

    public bool IsUserSlot => TeamKey == UserTeamKey;

    public bool IsRivalSlot => TeamKey == RivalTeamKey;

    public bool IsCandidateSlot => TeamKey == CandidateTeamKey;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsX2Driver
    {
        get => _isX2Driver;
        set => SetProperty(ref _isX2Driver, value);
    }

    public FantasyAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (SetProperty(ref _selectedAsset, value))
            {
                OnPropertyChanged(nameof(Price));
                OnPropertyChanged(nameof(ExpectedPoints));
                OnPropertyChanged(nameof(ExpectedBudgetChange));
                OnPropertyChanged(nameof(BudgetChangeColor));
                _selectionChanged();
            }
        }
    }

    public decimal? Price => SelectedAsset?.Price;

    public decimal? ExpectedPoints => SelectedAsset?.ExpectedPoints;

    public decimal? ExpectedBudgetChange => SelectedAsset?.ExpectedBudgetChange;

    public string EmptyText => AssetType == FantasyAssetType.Driver ? "Select driver" : "Select constructor";

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
}
