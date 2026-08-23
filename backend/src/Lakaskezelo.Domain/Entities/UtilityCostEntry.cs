namespace Lakaskezelo.Domain.Entities;

// Rezsi tétel — egy adott ingatlan adott havi rezsi-sora (pl. Áram, Gáz, Közös költség)
public class UtilityCostEntry
{
    public Guid Id { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
