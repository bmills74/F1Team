namespace F1Team.Core.Models;

public sealed record RequiredPoints(
    decimal? Terrible = null,
    decimal? Poor = null,
    decimal? Good = null,
    decimal? Great = null)
{
    public decimal? ForLevel(PriceChangeLevel level)
    {
        return level switch
        {
            PriceChangeLevel.Terrible => Terrible,
            PriceChangeLevel.Poor => Poor,
            PriceChangeLevel.Good => Good,
            PriceChangeLevel.Great => Great,
            _ => null
        };
    }
}
