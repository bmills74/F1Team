# F1 Fantasy Team Calculator Notes

## Goal

Build a Windows desktop app for picking an F1 Fantasy team for the upcoming race. The MVP should make it quick to compare team choices using expected points, expected budget movement, and remaining budget.

## Recommended Tech

- App framework: WPF on .NET 7 for current Visual Studio compatibility, with an easy upgrade path to .NET 8 or later
- Pattern: lightweight MVVM
- Data format: local JSON snapshots refreshed from F1FantasyTools
- Storage: simple local files for MVP

This gives us a native Windows app with strong table/list UI support, easy data binding, and enough polish without turning the first version into a platform project.

## MVP Requirements

1. Set current budget
   - User enters their current available team budget.
   - Budget should be editable at any time.
   - App recalculates remaining budget immediately.

2. Select and swap team assets
   - Team should support the fantasy structure:
     - 5 drivers
     - 2 constructors
   - User can choose assets from imported driver and constructor lists.
   - App shows total team cost.
   - App shows remaining budget.
   - Remaining budget is normal when >= 0.
   - Remaining budget is red when < 0.

3. Show projected output
   - For each selected asset:
     - price
     - expected points
     - expected budget gain/loss
     - required points for budget change levels
   - For the whole team:
     - total expected points
     - total expected budget gain/loss
     - current total cost
     - budget remaining/over budget

4. Add a rival team
   - User can create at least one rival.
   - Rival has a name.
   - Rival can have an expected team selected in the same way as the user's team.
   - Rival summary shows:
     - expected points
     - expected budget gain/loss
   - App compares user's expected points and budget change against rival.

## Data Source

Primary data source for MVP:

- `https://f1fantasytools.com/budget-builder`

The page is a Next.js app. The useful data is embedded in the page payload rather than exposed as a plain table. The importer should:

1. Download the page HTML.
2. Extract the embedded payload containing:
   - `bbTableAssets.drivers`
   - `bbTableAssets.constructors`
   - `currentRace`
   - `analystSims`
3. Normalize it into app-friendly records.
4. Save a local JSON cache so the app still opens if refresh fails.

Fallback:

- Allow loading from a local JSON file if the site structure changes.
- Keep the importer isolated so it can be fixed without touching the calculator logic.

## Core Data Model

### FantasyAsset

- `Id`
- `Abbreviation`
- `Name` if available
- `Type`: Driver or Constructor
- `TeamColor`
- `Price`
- `ExpectedPoints`
- `ExpectedBudgetChange`
- `PriorRacePoints`
- `PriorPriorRacePoints`
- `RequiredTerriblePoints`
- `RequiredPoorPoints`
- `RequiredGoodPoints`
- `RequiredGreatPoints`
- `OddsTerrible`
- `OddsPoor`
- `OddsGood`
- `OddsGreat`

### RaceContext

- `RaceId`
- `RaceName`
- `RoundNumber`
- `CountryName`
- `CircuitName`
- `SprintWeekend`
- `SimulationName`
- `FetchedAtUtc`

### FantasyTeam

- `Name`
- `SelectedDriverIds`
- `SelectedConstructorIds`
- Calculated:
  - `TotalCost`
  - `ExpectedPoints`
  - `ExpectedBudgetChange`

### AppSettings

- `CurrentBudget`
- `LastDataRefreshUtc`
- `LastSelectedTeam`
- `Rivals`

## UI Layout

### Main Window

Top bar:

- race name and sim name
- refresh data button
- current budget input
- total cost
- budget remaining

Main content:

- left: user's selected team
- center: searchable asset lists for drivers and constructors
- right: projections and rival comparison

Bottom/status area:

- data refresh status
- last fetched time
- warning if cached data is being used

## MVP Screens

### Team Builder

The primary screen. User can:

- set budget
- select 5 drivers
- select 2 constructors
- swap assets
- see totals update immediately

### Rival Comparison

Can live as a side panel for MVP. User can:

- add/edit rival name
- select rival expected team
- compare expected points and budget movement

## Calculation Rules

Total cost:

```text
sum(selected drivers prices) + sum(selected constructors prices)
```

Budget remaining:

```text
current budget - total cost
```

Expected points:

```text
sum(selected assets expected points)
```

Expected budget change:

```text
sum(selected assets expected budget gain/loss)
```

Expected budget change can be calculated from the simulation probability distribution:

```text
sum(price change amount * probability)
```

For example:

```text
(-0.6 * 0.10) + (-0.2 * 0.20) + (0.2 * 0.30) + (0.6 * 0.40)
```

The exact price movement amounts differ by tier, so the importer or calculator must preserve enough data to calculate this correctly.

## Project Structure

```text
F1Team/
  src/
    F1Team.App/
      Views/
      ViewModels/
      App.xaml
      MainWindow.xaml
    F1Team.Core/
      Models/
      Services/
      Calculations/
    F1Team.Data/
      Importing/
      Cache/
  data/
    snapshots/
  tests/
    F1Team.Core.Tests/
```

## Build Milestones

1. Scaffold solution
   - Create WPF app.
   - Create Core project.
   - Create Data project.
   - Wire references.

2. Add core models and calculations
   - Asset model.
   - Team model.
   - Budget and projection calculator.
   - Unit tests for total cost, remaining budget, expected points, and budget change.

3. Build importer
   - Download Budget Builder page.
   - Extract embedded data.
   - Normalize to app model.
   - Save/load cache.

4. Build MVP UI
   - Budget input.
   - User team selectors.
   - Driver and constructor tables.
   - Totals panel.
   - Over-budget red state.

5. Add rival panel
   - Add rival name.
   - Select rival expected team.
   - Display rival expected points and budget gain/loss.
   - Display user vs rival difference.

6. Polish and persistence
   - Save current budget.
   - Save selected user team.
   - Save rival team.
   - Show last refresh time and cache warning.

## Current Status

- Solution scaffolded.
- Core fantasy asset/team/projection models added.
- Projection calculator added and covered by tests.
- WPF MVP shell added with sample data.
- Refresh button added.
- F1FantasyTools Budget Builder importer added.
- Importer extracts `bbTableAssets`, `currentRace`, and `analystSims` from the embedded Next.js payload.
- Imported data is normalized into `FantasyDataSet`.
- Expected budget gain/loss is calculated from `price_change_probability` keys such as `pm6` and `pp3`.
- Latest imported data is cached under local app data.
- App loads cached data on startup and falls back to sample data if cache/import fails.

## Open Questions

- Should we model official F1 Fantasy constraints exactly for MVP, such as DRS Boost, chips, or team/constructor limits?
- Should the user team and rival team support manual expected points override?
- Should budget be stored in millions, for example `118.4`, everywhere internally?
- Should we include ownership percentages if we can recover them later?

## Initial MVP Defaults

- Budget entered in millions.
- One user team.
- One rival team.
- No chips.
- No DRS Boost.
- No login.
- Local-only storage.
- Manual edits allowed later, but not required for first pass.
