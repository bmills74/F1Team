using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Data;
using F1Team.Data.Cache;
using F1Team.Data.Importing;
using F1Team.Core.Calculations;
using F1Team.Core.Models;

namespace F1Team.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly BudgetProjectionCalculator _calculator = new();
    private readonly F1FantasyToolsDataImporter _importer = new();
    private readonly FantasyDataCache _cache = new();
    private readonly AppSettingsCache _settingsCache = new();
    private decimal _myActualBudget;
    private decimal _rivalActualBudget;
    private decimal _candidateActualBudget;
    private string _myRemainingBudgetText = "0.0";
    private string _rivalRemainingBudgetText = "0.0";
    private string _candidateRemainingBudgetText = "0.0";
    private TeamProjection _userProjection;
    private TeamProjection _rivalProjection;
    private TeamProjection _candidateProjection;
    private string _rivalName = "Rival";
    private string _assetSearchText = string.Empty;
    private int _selectedComparisonTabIndex;
    private string _dataStatus = "Using sample data. Refresh to import F1FantasyTools data.";
    private string _raceTitle = "Sample Data";
    private string _upcomingRaceDetails = "Refresh data to show the upcoming race.";
    private string _simulationTitle = "Mock projections";
    private bool _isRefreshing;
    private bool _isLoadingSettings;
    private AssetSlotViewModel? _activeSlot;
    private FantasyAsset? _myX2Driver;
    private FantasyAsset? _rivalX2Driver;
    private FantasyAsset? _candidateX2Driver;

    public MainWindowViewModel()
    {
        DriverAssets = new ObservableCollection<FantasyAsset>(CreateSampleDrivers());
        ConstructorAssets = new ObservableCollection<FantasyAsset>(CreateSampleConstructors());
        AllAssets = new ObservableCollection<FantasyAsset>(DriverAssets.Concat(ConstructorAssets));
        AssetPickerItems = new ObservableCollection<AssetPickerItemViewModel>(AllAssets.Select(asset => new AssetPickerItemViewModel(asset)));
        AssetPickerView = CollectionViewSource.GetDefaultView(AssetPickerItems);
        AssetPickerView.Filter = FilterAssetPicker;
        SortAssetPicker("ExpectedPoints");

        UserDriverSlots = CreateSlots("Driver", FantasyAssetType.Driver, 5, AssetSlotViewModel.UserTeamKey);
        UserConstructorSlots = CreateSlots("Constructor", FantasyAssetType.Constructor, 2, AssetSlotViewModel.UserTeamKey);
        RivalDriverSlots = CreateSlots("Driver", FantasyAssetType.Driver, 5, AssetSlotViewModel.RivalTeamKey);
        RivalConstructorSlots = CreateSlots("Constructor", FantasyAssetType.Constructor, 2, AssetSlotViewModel.RivalTeamKey);
        CandidateDriverSlots = CreateSlots("Driver", FantasyAssetType.Driver, 5, AssetSlotViewModel.CandidateTeamKey);
        CandidateConstructorSlots = CreateSlots("Constructor", FantasyAssetType.Constructor, 2, AssetSlotViewModel.CandidateTeamKey);

        SeedSelections();

        _myActualBudget = UserSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m);
        _rivalActualBudget = RivalSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m);
        _candidateActualBudget = CandidateSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m);
        _myX2Driver = UserDriverSlots.FirstOrDefault(slot => slot.SelectedAsset is not null)?.SelectedAsset;
        _rivalX2Driver = RivalDriverSlots.FirstOrDefault(slot => slot.SelectedAsset is not null)?.SelectedAsset;
        _candidateX2Driver = CandidateDriverSlots.FirstOrDefault(slot => slot.SelectedAsset is not null)?.SelectedAsset;
        _userProjection = CalculateProjection("My Team", UserSlots, MyActualBudget, MyX2Driver);
        _rivalProjection = CalculateProjection(_rivalName, RivalSlots, RivalActualBudget, RivalX2Driver);
        _candidateProjection = CalculateProjection("Candidate Team", CandidateSlots, CandidateActualBudget, CandidateX2Driver);
        RefreshDataCommand = new AsyncCommand(RefreshDataAsync, () => !IsRefreshing);
        ActivateSlotCommand = new RelayCommand(parameter => ActivateSlot(parameter as AssetSlotViewModel));
        ClearSlotCommand = new RelayCommand(parameter => ClearSlot(parameter as AssetSlotViewModel));
        SetMyBudgetCommand = new RelayCommand(_ => SetMyBudgetFromRemaining());
        SetRivalBudgetCommand = new RelayCommand(_ => SetRivalBudgetFromRemaining());
        SetCandidateBudgetCommand = new RelayCommand(_ => SetCandidateBudgetFromRemaining());
        ResetCandidateCommand = new RelayCommand(_ => ResetCandidateToCurrentTeam());
        SelectAssetCommand = new RelayCommand(
            parameter => SelectAsset(parameter as AssetPickerItemViewModel),
            parameter => parameter is AssetPickerItemViewModel item && item.IsSelectable);
        QuickSortCommand = new RelayCommand(parameter => SortAssetPicker(parameter?.ToString() ?? "ExpectedPoints"));
        ActivateSlot(UserDriverSlots[0]);
        Recalculate();

        _ = InitializeSavedStateAsync();
    }

    public ObservableCollection<FantasyAsset> DriverAssets { get; }

    public ObservableCollection<FantasyAsset> ConstructorAssets { get; }

    public ObservableCollection<FantasyAsset> AllAssets { get; }

    public ObservableCollection<AssetPickerItemViewModel> AssetPickerItems { get; }

    public ICollectionView AssetPickerView { get; }

    public ObservableCollection<AssetSlotViewModel> UserDriverSlots { get; }

    public ObservableCollection<AssetSlotViewModel> UserConstructorSlots { get; }

    public ObservableCollection<AssetSlotViewModel> RivalDriverSlots { get; }

    public ObservableCollection<AssetSlotViewModel> RivalConstructorSlots { get; }

    public ObservableCollection<AssetSlotViewModel> CandidateDriverSlots { get; }

    public ObservableCollection<AssetSlotViewModel> CandidateConstructorSlots { get; }

    public IReadOnlyList<FantasyAsset> UserSelectedDrivers => UserDriverSlots
        .Select(slot => slot.SelectedAsset)
        .Where(asset => asset is not null)
        .Cast<FantasyAsset>()
        .ToArray();

    public IReadOnlyList<FantasyAsset> RivalSelectedDrivers => RivalDriverSlots
        .Select(slot => slot.SelectedAsset)
        .Where(asset => asset is not null)
        .Cast<FantasyAsset>()
        .ToArray();

    public IReadOnlyList<FantasyAsset> CandidateSelectedDrivers => CandidateDriverSlots
        .Select(slot => slot.SelectedAsset)
        .Where(asset => asset is not null)
        .Cast<FantasyAsset>()
        .ToArray();

    public ICommand RefreshDataCommand { get; }

    public ICommand ActivateSlotCommand { get; }

    public ICommand ClearSlotCommand { get; }

    public ICommand SetMyBudgetCommand { get; }

    public ICommand SetRivalBudgetCommand { get; }

    public ICommand SetCandidateBudgetCommand { get; }

    public ICommand ResetCandidateCommand { get; }

    public ICommand SelectAssetCommand { get; }

    public ICommand QuickSortCommand { get; }

    public decimal MyActualBudget
    {
        get => _myActualBudget;
        private set
        {
            if (SetProperty(ref _myActualBudget, value))
            {
                Recalculate();
                QueueSaveSettings();
            }
        }
    }

    public string MyRemainingBudgetText
    {
        get => _myRemainingBudgetText;
        set
        {
            if (!SetProperty(ref _myRemainingBudgetText, value))
            {
                return;
            }

            QueueSaveSettings();
        }
    }

    public decimal RivalActualBudget
    {
        get => _rivalActualBudget;
        private set
        {
            if (SetProperty(ref _rivalActualBudget, value))
            {
                Recalculate();
                QueueSaveSettings();
            }
        }
    }

    public string RivalRemainingBudgetText
    {
        get => _rivalRemainingBudgetText;
        set
        {
            if (!SetProperty(ref _rivalRemainingBudgetText, value))
            {
                return;
            }

            QueueSaveSettings();
        }
    }

    public decimal CandidateActualBudget
    {
        get => _candidateActualBudget;
        private set
        {
            if (SetProperty(ref _candidateActualBudget, value))
            {
                Recalculate();
                QueueSaveSettings();
            }
        }
    }

    public string CandidateRemainingBudgetText
    {
        get => _candidateRemainingBudgetText;
        set
        {
            if (!SetProperty(ref _candidateRemainingBudgetText, value))
            {
                return;
            }

            QueueSaveSettings();
        }
    }

    public string RivalName
    {
        get => _rivalName;
        set
        {
            if (SetProperty(ref _rivalName, value))
            {
                Recalculate();
                QueueSaveSettings();
            }
        }
    }

    public string AssetSearchText
    {
        get => _assetSearchText;
        set
        {
            if (SetProperty(ref _assetSearchText, value))
            {
                AssetPickerView.Refresh();
            }
        }
    }

    public int SelectedComparisonTabIndex
    {
        get => _selectedComparisonTabIndex;
        set
        {
            if (SetProperty(ref _selectedComparisonTabIndex, value))
            {
                OnPropertyChanged(nameof(CompareVsRival));
                OnPropertyChanged(nameof(CompareVsCandidate));
                OnComparisonChanged();
                QueueSaveSettings();
            }
        }
    }

    public bool CompareVsRival
    {
        get => SelectedComparisonTabIndex == 0;
        set
        {
            if (value)
            {
                SelectedComparisonTabIndex = 0;
            }
        }
    }

    public bool CompareVsCandidate
    {
        get => SelectedComparisonTabIndex == 1;
        set
        {
            if (value)
            {
                SelectedComparisonTabIndex = 1;
            }
        }
    }

    public FantasyAsset? MyX2Driver
    {
        get => _myX2Driver;
        set
        {
            if (SetProperty(ref _myX2Driver, value))
            {
                Recalculate();
                QueueSaveSettings();
            }
        }
    }

    public FantasyAsset? RivalX2Driver
    {
        get => _rivalX2Driver;
        set
        {
            if (SetProperty(ref _rivalX2Driver, value))
            {
                Recalculate();
                QueueSaveSettings();
            }
        }
    }

    public FantasyAsset? CandidateX2Driver
    {
        get => _candidateX2Driver;
        set
        {
            if (SetProperty(ref _candidateX2Driver, value))
            {
                Recalculate();
                QueueSaveSettings();
            }
        }
    }

    public TeamProjection UserProjection
    {
        get => _userProjection;
        private set => SetProperty(ref _userProjection, value);
    }

    public TeamProjection RivalProjection
    {
        get => _rivalProjection;
        private set => SetProperty(ref _rivalProjection, value);
    }

    public TeamProjection CandidateProjection
    {
        get => _candidateProjection;
        private set => SetProperty(ref _candidateProjection, value);
    }

    public string TeamStatusText
    {
        get
        {
            var emptyDrivers = TeamProjection.RequiredDriverCount - UserProjection.DriverCount;
            var emptyConstructors = TeamProjection.RequiredConstructorCount - UserProjection.ConstructorCount;
            var emptySlots = emptyDrivers + emptyConstructors;

            if (emptySlots > 0)
            {
                return $"{emptySlots} slot{(emptySlots == 1 ? "" : "s")} to fill";
            }

            if (UserProjection.IsOverBudget)
            {
                return $"{Math.Abs(UserProjection.RemainingBudget):0.0}m over budget";
            }

            return "Team ready";
        }
    }

    public string TeamStatusColor
    {
        get
        {
            var isComplete = UserProjection.IsComplete;
            if (!isComplete)
            {
                return "#8a6d1b"; // amber: still building
            }

            return UserProjection.IsOverBudget ? "#b42318" : "#1b7f3a";
        }
    }

    public string RefreshButtonText => IsRefreshing ? "Refreshing..." : "Refresh Data";

    public TeamProjection ComparisonProjection => SelectedComparisonTabIndex == 1 ? CandidateProjection : RivalProjection;

    public string ComparisonTeamTitle => SelectedComparisonTabIndex == 1 ? "Candidate" : RivalName;

    public decimal ExpectedPointsDifference => UserProjection.ExpectedPoints - ComparisonProjection.ExpectedPoints;

    public decimal ExpectedBudgetChangeDifference => UserProjection.ExpectedBudgetChange - ComparisonProjection.ExpectedBudgetChange;

    public decimal TeamCostDifference => UserProjection.TotalCost - ComparisonProjection.TotalCost;

    public decimal RemainingBudgetDifference => UserProjection.RemainingBudget - ComparisonProjection.RemainingBudget;

    public string DataStatus
    {
        get => _dataStatus;
        private set => SetProperty(ref _dataStatus, value);
    }

    public string RaceTitle
    {
        get => _raceTitle;
        private set => SetProperty(ref _raceTitle, value);
    }

    public string UpcomingRaceDetails
    {
        get => _upcomingRaceDetails;
        private set => SetProperty(ref _upcomingRaceDetails, value);
    }

    public string SimulationTitle
    {
        get => _simulationTitle;
        private set => SetProperty(ref _simulationTitle, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(RefreshButtonText));
                if (RefreshDataCommand is AsyncCommand command)
                {
                    command.RaiseCanExecuteChanged();
                }
            }
        }
    }

    public AssetSlotViewModel? ActiveSlot
    {
        get => _activeSlot;
        private set
        {
            if (_activeSlot == value)
            {
                return;
            }

            if (_activeSlot is not null)
            {
                _activeSlot.IsActive = false;
            }

            _activeSlot = value;

            if (_activeSlot is not null)
            {
                _activeSlot.IsActive = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ActivePickerTitle));
            RefreshAssetPickerState();
        }
    }

    public string ActivePickerTitle
    {
        get
        {
            if (ActiveSlot is null)
            {
                return "Pick a slot on the left, then choose an asset below";
            }

            var teamName = ActiveSlot.TeamKey switch
            {
                AssetSlotViewModel.RivalTeamKey => RivalName,
                AssetSlotViewModel.CandidateTeamKey => "Candidate Team",
                _ => "My Team"
            };
            return $"Picking: {teamName} · {ActiveSlot.Label}";
        }
    }

    private IEnumerable<AssetSlotViewModel> UserSlots => UserDriverSlots.Concat(UserConstructorSlots);

    private IEnumerable<AssetSlotViewModel> RivalSlots => RivalDriverSlots.Concat(RivalConstructorSlots);

    private IEnumerable<AssetSlotViewModel> CandidateSlots => CandidateDriverSlots.Concat(CandidateConstructorSlots);

    private ObservableCollection<AssetSlotViewModel> CreateSlots(string labelPrefix, FantasyAssetType type, int count, string teamKey)
    {
        return new ObservableCollection<AssetSlotViewModel>(
            Enumerable.Range(1, count)
                .Select(index => new AssetSlotViewModel($"{labelPrefix} {index}", type, teamKey, Recalculate)));
    }

    private void Recalculate()
    {
        EnsureX2DriversAreSelected();
        UserProjection = CalculateProjection("My Team", UserSlots, MyActualBudget, MyX2Driver);
        RivalProjection = CalculateProjection(RivalName, RivalSlots, RivalActualBudget, RivalX2Driver);
        CandidateProjection = CalculateProjection("Candidate Team", CandidateSlots, CandidateActualBudget, CandidateX2Driver);
        RefreshX2SlotMarkers();
        SyncRemainingBudgetTextFromProjections();
        OnComparisonChanged();
        OnPropertyChanged(nameof(TeamStatusText));
        OnPropertyChanged(nameof(TeamStatusColor));
        OnPropertyChanged(nameof(ActivePickerTitle));
        OnPropertyChanged(nameof(UserSelectedDrivers));
        OnPropertyChanged(nameof(RivalSelectedDrivers));
        OnPropertyChanged(nameof(CandidateSelectedDrivers));
        RefreshAssetPickerState();
    }

    private void OnComparisonChanged()
    {
        OnPropertyChanged(nameof(ComparisonProjection));
        OnPropertyChanged(nameof(ComparisonTeamTitle));
        OnPropertyChanged(nameof(ExpectedPointsDifference));
        OnPropertyChanged(nameof(ExpectedBudgetChangeDifference));
        OnPropertyChanged(nameof(TeamCostDifference));
        OnPropertyChanged(nameof(RemainingBudgetDifference));
        OnPropertyChanged(nameof(ActivePickerTitle));
        RefreshAssetPickerState();
    }

    private TeamProjection CalculateProjection(
        string name,
        IEnumerable<AssetSlotViewModel> slots,
        decimal actualBudget,
        FantasyAsset? x2Driver)
    {
        var assets = slots
            .Select(slot => slot.SelectedAsset)
            .Where(asset => asset is not null)
            .Cast<FantasyAsset>()
            .ToArray();

        var projection = _calculator.Project(new FantasyTeam(name, assets), actualBudget);
        if (x2Driver is null || !assets.Any(asset => asset.Id == x2Driver.Id && asset.Type == FantasyAssetType.Driver))
        {
            return projection;
        }

        return projection with { ExpectedPoints = projection.ExpectedPoints + x2Driver.ExpectedPoints };
    }

    private async Task InitializeSavedStateAsync()
    {
        _isLoadingSettings = true;
        AppSettingsSnapshot? settings = null;

        try
        {
            settings = await _settingsCache.TryLoadAsync();
            if (settings is not null)
            {
                ApplySettings(settings);
            }
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not load saved picks. Using defaults. {ex.Message}";
        }
        finally
        {
            _isLoadingSettings = false;
        }

        await LoadCachedDataAsync(settings);
        QueueSaveSettings();
    }

    private async Task LoadCachedDataAsync(AppSettingsSnapshot? settings)
    {
        try
        {
            var cached = await _cache.TryLoadAsync();
            if (cached is null)
            {
                return;
            }

            ApplyDataSet(cached, keepExistingSelections: true);
            if (settings is not null)
            {
                ApplySettingsSelections(settings);
                Recalculate();
            }

            DataStatus = $"Loaded cached data fetched {cached.RaceContext.FetchedAtUtc.LocalDateTime:g}.";
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not load cached data. Using sample data. {ex.Message}";
        }
    }

    private void ApplySettings(AppSettingsSnapshot settings)
    {
        RivalName = settings.RivalName;
        ApplySettingsSelections(settings);

        var myCost = UserSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m);
        var rivalCost = RivalSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m);
        var candidateCost = CandidateSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m);

        MyActualBudget = settings.MyActualBudget > 0m
            ? settings.MyActualBudget
            : settings.MyRemainingBudget != 0m
                ? myCost + settings.MyRemainingBudget
                : settings.CurrentBudget;

        RivalActualBudget = settings.RivalActualBudget > 0m
            ? settings.RivalActualBudget
            : rivalCost + settings.RivalRemainingBudget;

        CandidateActualBudget = settings.CandidateActualBudget > 0m
            ? settings.CandidateActualBudget
            : candidateCost + settings.CandidateRemainingBudget;

        SelectedComparisonTabIndex = settings.SelectedComparisonTabIndex is 0 or 1
            ? settings.SelectedComparisonTabIndex
            : 0;

        Recalculate();
    }

    private void ApplySettingsSelections(AppSettingsSnapshot settings)
    {
        RestoreSelections(UserDriverSlots, settings.UserDriverIds);
        RestoreSelections(UserConstructorSlots, settings.UserConstructorIds);
        RestoreSelections(RivalDriverSlots, settings.RivalDriverIds);
        RestoreSelections(RivalConstructorSlots, settings.RivalConstructorIds);
        if (settings.CandidateDriverIds is not null)
        {
            RestoreSelections(CandidateDriverSlots, settings.CandidateDriverIds);
        }

        if (settings.CandidateConstructorIds is not null)
        {
            RestoreSelections(CandidateConstructorSlots, settings.CandidateConstructorIds);
        }

        MyX2Driver = FindSelectedDriver(UserDriverSlots, settings.MyX2DriverId);
        RivalX2Driver = FindSelectedDriver(RivalDriverSlots, settings.RivalX2DriverId);
        CandidateX2Driver = FindSelectedDriver(CandidateDriverSlots, settings.CandidateX2DriverId);
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            IsRefreshing = true;
            DataStatus = "Refreshing F1FantasyTools data...";
            var dataSet = await _importer.ImportAsync();
            ApplyDataSet(dataSet, keepExistingSelections: true);
            await _cache.SaveAsync(dataSet);
            QueueSaveSettings();
            DataStatus = $"Live data refreshed {dataSet.RaceContext.FetchedAtUtc.LocalDateTime:g}.";
        }
        catch (Exception ex)
        {
            DataStatus = $"Refresh failed. Keeping current data. {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void ApplyDataSet(FantasyDataSet dataSet, bool keepExistingSelections)
    {
        var previousUserSelections = keepExistingSelections ? CaptureSelectedIds(UserSlots) : Array.Empty<string>();
        var previousRivalSelections = keepExistingSelections ? CaptureSelectedIds(RivalSlots) : Array.Empty<string>();
        var previousCandidateSelections = keepExistingSelections ? CaptureSelectedIds(CandidateSlots) : Array.Empty<string>();
        var previousMyX2DriverId = MyX2Driver?.Id;
        var previousRivalX2DriverId = RivalX2Driver?.Id;
        var previousCandidateX2DriverId = CandidateX2Driver?.Id;

        ReplaceAssets(DriverAssets, dataSet.Drivers);
        ReplaceAssets(ConstructorAssets, dataSet.Constructors);
        ReplaceAssets(AllAssets, DriverAssets.Concat(ConstructorAssets));
        ReplacePickerItems(dataSet.Drivers.Concat(dataSet.Constructors));

        RaceTitle = $"Round {dataSet.RaceContext.RoundNumber}: {dataSet.RaceContext.RaceName}";
        UpcomingRaceDetails = FormatUpcomingRaceDetails(dataSet.RaceContext);
        SimulationTitle = dataSet.RaceContext.SimulationName ?? "Imported projections";

        if (keepExistingSelections)
        {
            RestoreSelections(UserSlots, previousUserSelections);
            RestoreSelections(RivalSlots, previousRivalSelections);
            RestoreSelections(CandidateSlots, previousCandidateSelections);
            MyX2Driver = FindSelectedDriver(UserDriverSlots, previousMyX2DriverId);
            RivalX2Driver = FindSelectedDriver(RivalDriverSlots, previousRivalX2DriverId);
            CandidateX2Driver = FindSelectedDriver(CandidateDriverSlots, previousCandidateX2DriverId);
        }
        else
        {
            SelectDefaultTeam(UserDriverSlots, UserConstructorSlots);
            SelectDefaultTeam(RivalDriverSlots, RivalConstructorSlots);
            SelectDefaultTeam(CandidateDriverSlots, CandidateConstructorSlots);
        }

        Recalculate();
    }

    private void ActivateSlot(AssetSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        ActiveSlot = slot;
    }

    private void ClearSlot(AssetSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        slot.SelectedAsset = null;
        ActivateSlot(slot);
        Recalculate();
        QueueSaveSettings();
    }

    private void SetMyBudgetFromRemaining()
    {
        if (!TryParseBudget(MyRemainingBudgetText, out var remainingBudget))
        {
            return;
        }

        MyActualBudget = UserSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m) + remainingBudget;
        SyncRemainingBudgetTextFromProjections();
    }

    private void SetRivalBudgetFromRemaining()
    {
        if (!TryParseBudget(RivalRemainingBudgetText, out var remainingBudget))
        {
            return;
        }

        RivalActualBudget = RivalSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m) + remainingBudget;
        SyncRemainingBudgetTextFromProjections();
    }

    private void SetCandidateBudgetFromRemaining()
    {
        if (!TryParseBudget(CandidateRemainingBudgetText, out var remainingBudget))
        {
            return;
        }

        CandidateActualBudget = CandidateSlots.Sum(slot => slot.SelectedAsset?.Price ?? 0m) + remainingBudget;
        SyncRemainingBudgetTextFromProjections();
    }

    private void ResetCandidateToCurrentTeam()
    {
        CopySelections(UserDriverSlots, CandidateDriverSlots);
        CopySelections(UserConstructorSlots, CandidateConstructorSlots);
        CandidateActualBudget = MyActualBudget;
        CandidateX2Driver = FindSelectedDriver(CandidateDriverSlots, MyX2Driver?.Id);
        ActivateSlot(CandidateDriverSlots.FirstOrDefault() ?? CandidateConstructorSlots.FirstOrDefault());
        Recalculate();
        QueueSaveSettings();
    }

    private static void CopySelections(
        IReadOnlyList<AssetSlotViewModel> sourceSlots,
        IReadOnlyList<AssetSlotViewModel> targetSlots)
    {
        var slotCount = Math.Min(sourceSlots.Count, targetSlots.Count);
        for (var index = 0; index < slotCount; index++)
        {
            targetSlots[index].SelectedAsset = sourceSlots[index].SelectedAsset;
        }
    }

    private void SelectAsset(AssetPickerItemViewModel? item)
    {
        if (item is null || ActiveSlot is null || !item.IsSelectable)
        {
            return;
        }

        ActiveSlot.SelectedAsset = item.Asset;
        MoveToNextSlot();
        Recalculate();
        QueueSaveSettings();
    }

    private void EnsureX2DriversAreSelected()
    {
        if (MyX2Driver is not null && !UserSelectedDrivers.Any(driver => driver.Id == MyX2Driver.Id))
        {
            MyX2Driver = UserSelectedDrivers.FirstOrDefault();
        }

        if (RivalX2Driver is not null && !RivalSelectedDrivers.Any(driver => driver.Id == RivalX2Driver.Id))
        {
            RivalX2Driver = RivalSelectedDrivers.FirstOrDefault();
        }

        if (CandidateX2Driver is not null && !CandidateSelectedDrivers.Any(driver => driver.Id == CandidateX2Driver.Id))
        {
            CandidateX2Driver = CandidateSelectedDrivers.FirstOrDefault();
        }
    }

    private void RefreshX2SlotMarkers()
    {
        foreach (var slot in UserDriverSlots)
        {
            slot.IsX2Driver = slot.SelectedAsset is not null && slot.SelectedAsset.Id == MyX2Driver?.Id;
        }

        foreach (var slot in RivalDriverSlots)
        {
            slot.IsX2Driver = slot.SelectedAsset is not null && slot.SelectedAsset.Id == RivalX2Driver?.Id;
        }

        foreach (var slot in CandidateDriverSlots)
        {
            slot.IsX2Driver = slot.SelectedAsset is not null && slot.SelectedAsset.Id == CandidateX2Driver?.Id;
        }

        foreach (var slot in UserConstructorSlots.Concat(RivalConstructorSlots).Concat(CandidateConstructorSlots))
        {
            slot.IsX2Driver = false;
        }
    }

    private void SyncRemainingBudgetTextFromProjections()
    {
        var myRemainingText = UserProjection.RemainingBudget.ToString("0.0");
        if (_myRemainingBudgetText != myRemainingText)
        {
            _myRemainingBudgetText = myRemainingText;
            OnPropertyChanged(nameof(MyRemainingBudgetText));
        }

        var rivalRemainingText = RivalProjection.RemainingBudget.ToString("0.0");
        if (_rivalRemainingBudgetText != rivalRemainingText)
        {
            _rivalRemainingBudgetText = rivalRemainingText;
            OnPropertyChanged(nameof(RivalRemainingBudgetText));
        }

        var candidateRemainingText = CandidateProjection.RemainingBudget.ToString("0.0");
        if (_candidateRemainingBudgetText != candidateRemainingText)
        {
            _candidateRemainingBudgetText = candidateRemainingText;
            OnPropertyChanged(nameof(CandidateRemainingBudgetText));
        }
    }

    private void MoveToNextSlot()
    {
        if (ActiveSlot is null)
        {
            return;
        }

        var slots = GetSlotsForTeam(ActiveSlot.TeamKey).ToArray();
        var currentIndex = Array.IndexOf(slots, ActiveSlot);
        var nextEmpty = slots
            .Skip(currentIndex + 1)
            .Concat(slots.Take(currentIndex + 1))
            .FirstOrDefault(slot => slot.AssetType == ActiveSlot.AssetType && slot.SelectedAsset is null);

        if (nextEmpty is not null)
        {
            ActivateSlot(nextEmpty);
        }
    }

    private bool FilterAssetPicker(object item)
    {
        if (item is not AssetPickerItemViewModel asset)
        {
            return false;
        }

        if (ActiveSlot is not null && asset.Type != ActiveSlot.AssetType)
        {
            return false;
        }

        var search = _assetSearchText?.Trim();
        if (string.IsNullOrEmpty(search))
        {
            return true;
        }

        return asset.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               asset.Abbreviation.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<AssetSlotViewModel> GetSlotsForTeam(string teamKey)
    {
        return teamKey switch
        {
            AssetSlotViewModel.RivalTeamKey => RivalSlots,
            AssetSlotViewModel.CandidateTeamKey => CandidateSlots,
            _ => UserSlots
        };
    }

    private TeamProjection GetProjectionForTeam(string teamKey)
    {
        return teamKey switch
        {
            AssetSlotViewModel.RivalTeamKey => RivalProjection,
            AssetSlotViewModel.CandidateTeamKey => CandidateProjection,
            _ => UserProjection
        };
    }

    private void SortAssetPicker(string sortKey)
    {
        var direction = sortKey is "Name" or "Price"
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;

        AssetPickerView.SortDescriptions.Clear();
        AssetPickerView.SortDescriptions.Add(new SortDescription(sortKey, direction));

        if (sortKey != "Name")
        {
            AssetPickerView.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
        }

        AssetPickerView.Refresh();
    }

    private void RefreshAssetPickerState()
    {
        if (!IsInitialized())
        {
            return;
        }

        var userAssetIds = UserSlots
            .Select(slot => slot.SelectedAsset?.Id)
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet();
        var rivalAssetIds = RivalSlots
            .Select(slot => slot.SelectedAsset?.Id)
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet();
        var candidateAssetIds = CandidateSlots
            .Select(slot => slot.SelectedAsset?.Id)
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet();

        var activeSelectedId = ActiveSlot?.SelectedAsset?.Id;
        var activeTeamAssetIds = ActiveSlot is null
            ? userAssetIds
            : GetSlotsForTeam(ActiveSlot.TeamKey)
                .Select(slot => slot.SelectedAsset?.Id)
                .Where(id => id is not null)
                .Cast<string>()
                .ToHashSet();
        var replacementCredit = ActiveSlot?.SelectedAsset?.Price ?? 0m;
        var activeTeamRemainingBudget = ActiveSlot is null ? UserProjection.RemainingBudget : GetProjectionForTeam(ActiveSlot.TeamKey).RemainingBudget;

        foreach (var item in AssetPickerItems)
        {
            item.IsSelectedByUser = userAssetIds.Contains(item.Id);
            item.IsSelectedByRival = rivalAssetIds.Contains(item.Id);
            item.IsSelectedByCandidate = candidateAssetIds.Contains(item.Id);

            var isCorrectType = ActiveSlot is null || item.Type == ActiveSlot.AssetType;
            var alreadySelectedInActiveTeam = activeTeamAssetIds.Contains(item.Id) && item.Id != activeSelectedId;
            var affordable = ActiveSlot is null || activeTeamRemainingBudget + replacementCredit - item.Price >= 0m;

            item.IsAffordableForActiveSlot = affordable;
            item.IsSelectable = ActiveSlot is not null && isCorrectType && !alreadySelectedInActiveTeam;
            item.RefreshDerivedText();
        }

        AssetPickerView.Refresh();

        if (SelectAssetCommand is RelayCommand selectCommand)
        {
            selectCommand.RaiseCanExecuteChanged();
        }
    }

    private bool IsInitialized()
    {
        return UserDriverSlots is not null &&
               UserConstructorSlots is not null &&
               RivalDriverSlots is not null &&
               RivalConstructorSlots is not null &&
               CandidateDriverSlots is not null &&
               CandidateConstructorSlots is not null &&
               AssetPickerItems is not null &&
               AssetPickerView is not null;
    }

    private void SelectDefaultTeam(
        ObservableCollection<AssetSlotViewModel> driverSlots,
        ObservableCollection<AssetSlotViewModel> constructorSlots)
    {
        var drivers = DriverAssets
            .OrderByDescending(asset => asset.ExpectedPoints)
            .Take(driverSlots.Count)
            .ToArray();
        var constructors = ConstructorAssets
            .OrderByDescending(asset => asset.ExpectedPoints)
            .Take(constructorSlots.Count)
            .ToArray();

        for (var index = 0; index < driverSlots.Count; index++)
        {
            driverSlots[index].SelectedAsset = index < drivers.Length ? drivers[index] : null;
        }

        for (var index = 0; index < constructorSlots.Count; index++)
        {
            constructorSlots[index].SelectedAsset = index < constructors.Length ? constructors[index] : null;
        }
    }

    private static string[] CaptureSelectedIds(IEnumerable<AssetSlotViewModel> slots)
    {
        return slots
            .Select(slot => slot.SelectedAsset?.Id ?? "")
            .ToArray();
    }

    private static bool TryParseBudget(string value, out decimal budget)
    {
        var normalized = value.Replace(',', '.');
        if (decimal.TryParse(normalized, out var parsed) && parsed >= 0m)
        {
            budget = decimal.Round(parsed, 1);
            return true;
        }

        budget = 0m;
        return false;
    }

    private void RestoreSelections(IEnumerable<AssetSlotViewModel> slots, IReadOnlyList<string> selectedIds)
    {
        var allAssetsById = AllAssets.ToDictionary(asset => asset.Id);
        var slotArray = slots.ToArray();

        for (var index = 0; index < slotArray.Length; index++)
        {
            var selectedId = index < selectedIds.Count ? selectedIds[index] : "";
            slotArray[index].SelectedAsset = allAssetsById.TryGetValue(selectedId, out var asset) ? asset : null;
        }
    }

    private static FantasyAsset? FindSelectedDriver(
        IEnumerable<AssetSlotViewModel> driverSlots,
        string? selectedDriverId)
    {
        var selectedDrivers = driverSlots
            .Select(slot => slot.SelectedAsset)
            .Where(asset => asset is not null)
            .Cast<FantasyAsset>()
            .ToArray();

        return selectedDriverId is null
            ? selectedDrivers.FirstOrDefault()
            : selectedDrivers.FirstOrDefault(driver => driver.Id == selectedDriverId) ?? selectedDrivers.FirstOrDefault();
    }

    private void QueueSaveSettings()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsCache.SaveAsync(CreateSettingsSnapshot());
        }
        catch (Exception ex)
        {
            DataStatus = $"Could not save settings. {ex.Message}";
        }
    }

    private AppSettingsSnapshot CreateSettingsSnapshot()
    {
        return new AppSettingsSnapshot(
            UserProjection.CurrentBudget,
            RivalName,
            CaptureSelectedIds(UserDriverSlots),
            CaptureSelectedIds(UserConstructorSlots),
            CaptureSelectedIds(RivalDriverSlots),
            CaptureSelectedIds(RivalConstructorSlots),
            UserProjection.RemainingBudget,
            RivalProjection.RemainingBudget,
            MyActualBudget,
            RivalActualBudget,
            MyX2Driver?.Id,
            RivalX2Driver?.Id,
            CaptureSelectedIds(CandidateDriverSlots),
            CaptureSelectedIds(CandidateConstructorSlots),
            CandidateProjection.RemainingBudget,
            CandidateActualBudget,
            CandidateX2Driver?.Id,
            SelectedComparisonTabIndex);
    }

    private static void ReplaceAssets(ObservableCollection<FantasyAsset> target, IEnumerable<FantasyAsset> assets)
    {
        target.Clear();
        foreach (var asset in assets)
        {
            target.Add(asset);
        }
    }

    private static string FormatUpcomingRaceDetails(RaceContext raceContext)
    {
        var locationParts = new[]
            {
                raceContext.CircuitName,
                raceContext.CountryName
            }
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var location = string.Join(", ", locationParts);
        var sprint = raceContext.SprintWeekend ? "Sprint weekend" : "Standard weekend";

        return string.IsNullOrWhiteSpace(location)
            ? $"Upcoming race: {raceContext.RaceName} ({sprint})"
            : $"Upcoming race: {raceContext.RaceName} at {location} ({sprint})";
    }

    private void ReplacePickerItems(IEnumerable<FantasyAsset> assets)
    {
        AssetPickerItems.Clear();
        foreach (var asset in assets)
        {
            AssetPickerItems.Add(new AssetPickerItemViewModel(asset));
        }

        RefreshAssetPickerState();
    }

    private void SeedSelections()
    {
        UserDriverSlots[0].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "NOR");
        UserDriverSlots[1].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "PIA");
        UserDriverSlots[2].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "RUS");
        UserDriverSlots[3].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "ANT");
        UserDriverSlots[4].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "BEA");
        UserConstructorSlots[0].SelectedAsset = ConstructorAssets.First(asset => asset.Abbreviation == "MCL");
        UserConstructorSlots[1].SelectedAsset = ConstructorAssets.First(asset => asset.Abbreviation == "HAA");

        RivalDriverSlots[0].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "VER");
        RivalDriverSlots[1].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "NOR");
        RivalDriverSlots[2].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "HAM");
        RivalDriverSlots[3].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "ALB");
        RivalDriverSlots[4].SelectedAsset = DriverAssets.First(asset => asset.Abbreviation == "BEA");
        RivalConstructorSlots[0].SelectedAsset = ConstructorAssets.First(asset => asset.Abbreviation == "MER");
        RivalConstructorSlots[1].SelectedAsset = ConstructorAssets.First(asset => asset.Abbreviation == "VRB");

        CandidateDriverSlots[0].SelectedAsset = UserDriverSlots[0].SelectedAsset;
        CandidateDriverSlots[1].SelectedAsset = UserDriverSlots[1].SelectedAsset;
        CandidateDriverSlots[2].SelectedAsset = UserDriverSlots[2].SelectedAsset;
        CandidateDriverSlots[3].SelectedAsset = UserDriverSlots[3].SelectedAsset;
        CandidateDriverSlots[4].SelectedAsset = UserDriverSlots[4].SelectedAsset;
        CandidateConstructorSlots[0].SelectedAsset = UserConstructorSlots[0].SelectedAsset;
        CandidateConstructorSlots[1].SelectedAsset = UserConstructorSlots[1].SelectedAsset;
    }

    private static IEnumerable<FantasyAsset> CreateSampleDrivers()
    {
        yield return Driver("VER", "Max Verstappen", 30.2m, 42.4m, 0.10m);
        yield return Driver("NOR", "Lando Norris", 29.4m, 45.8m, 0.20m);
        yield return Driver("PIA", "Oscar Piastri", 27.9m, 43.1m, 0.20m);
        yield return Driver("RUS", "George Russell", 24.2m, 36.9m, 0.10m);
        yield return Driver("HAM", "Lewis Hamilton", 22.4m, 32.7m, 0.00m);
        yield return Driver("LEC", "Charles Leclerc", 23.6m, 34.8m, 0.00m);
        yield return Driver("ANT", "Kimi Antonelli", 16.2m, 24.5m, 0.30m);
        yield return Driver("ALB", "Alex Albon", 12.8m, 18.4m, 0.10m);
        yield return Driver("BEA", "Oliver Bearman", 8.7m, 13.6m, 0.40m);
        yield return Driver("HUL", "Nico Hulkenberg", 9.3m, 14.1m, 0.20m);
    }

    private static IEnumerable<FantasyAsset> CreateSampleConstructors()
    {
        yield return Constructor("MCL", "McLaren", 34.2m, 78.0m, 0.30m);
        yield return Constructor("MER", "Mercedes", 29.6m, 66.2m, 0.20m);
        yield return Constructor("RED", "Red Bull", 27.8m, 58.4m, 0.10m);
        yield return Constructor("FER", "Ferrari", 26.5m, 54.7m, 0.00m);
        yield return Constructor("VRB", "Racing Bulls", 12.4m, 22.0m, 0.40m);
        yield return Constructor("HAA", "Haas", 10.1m, 18.6m, 0.30m);
    }

    private static FantasyAsset Driver(string abbreviation, string name, decimal price, decimal points, decimal budgetChange)
    {
        return CreateSampleAsset(abbreviation, name, FantasyAssetType.Driver, price, points, budgetChange);
    }

    private static FantasyAsset Constructor(string abbreviation, string name, decimal price, decimal points, decimal budgetChange)
    {
        return CreateSampleAsset(abbreviation, name, FantasyAssetType.Constructor, price, points, budgetChange);
    }

    // Sample data has no live detail feed, so derive plausible form / required-points / odds
    // values from the headline numbers to exercise the new asset detail tooltip.
    private static FantasyAsset CreateSampleAsset(
        string abbreviation,
        string name,
        FantasyAssetType type,
        decimal price,
        decimal points,
        decimal budgetChange)
    {
        var priorRace = decimal.Round(points * 0.9m, 1);
        var priorPrior = decimal.Round(points * 1.05m, 1);
        var required = new RequiredPoints(
            Terrible: decimal.Round(points * 0.3m, 1),
            Poor: decimal.Round(points * 0.6m, 1),
            Good: decimal.Round(points * 1.15m, 1),
            Great: decimal.Round(points * 1.5m, 1));

        // Higher expected budget change skews the odds towards a price rise.
        var riseBias = Math.Clamp(50m + budgetChange * 80m, 5m, 95m);
        var odds = new PriceChangeOdds(
            Terrible: decimal.Round((100m - riseBias) * 0.35m, 0),
            Poor: decimal.Round((100m - riseBias) * 0.65m, 0),
            Good: decimal.Round(riseBias * 0.6m, 0),
            Great: decimal.Round(riseBias * 0.4m, 0));

        return new FantasyAsset(
            abbreviation,
            abbreviation,
            type,
            price,
            points,
            budgetChange,
            name,
            PriorRacePoints: priorRace,
            PriorPriorRacePoints: priorPrior,
            RequiredPoints: required,
            PriceChangeOdds: odds);
    }
}
