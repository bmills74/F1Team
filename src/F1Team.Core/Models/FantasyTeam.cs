namespace F1Team.Core.Models;

public sealed record FantasyTeam(string Name, IReadOnlyList<FantasyAsset> Assets)
{
    public IReadOnlyList<FantasyAsset> Drivers => Assets
        .Where(asset => asset.Type == FantasyAssetType.Driver)
        .ToArray();

    public IReadOnlyList<FantasyAsset> Constructors => Assets
        .Where(asset => asset.Type == FantasyAssetType.Constructor)
        .ToArray();
}
