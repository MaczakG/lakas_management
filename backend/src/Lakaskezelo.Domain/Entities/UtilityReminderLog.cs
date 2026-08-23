namespace Lakaskezelo.Domain.Entities;

// Nyilvántartja, hogy egy ingatlanhoz egy adott hónapra már kiment-e a hiányzó-rezsi emlékeztető,
// hogy a napi ellenőrzés ne küldjön minden nap újra levelet ugyanazért a hónapért.
public class UtilityReminderLog
{
    public Guid Id { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime SentAt { get; set; }
}
