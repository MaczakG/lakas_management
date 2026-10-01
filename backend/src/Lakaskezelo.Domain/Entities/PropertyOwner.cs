namespace Lakaskezelo.Domain.Entities;

// Egy ingatlannak több tulajdonosa is lehet (társtulajdon, öröklés)
public class PropertyOwner
{
    public Guid Id { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public Guid OwnerId { get; set; }
    public Owner? Owner { get; set; }

    // Tulajdoni hányad tört alakban (pl. "1/2"), a számlán a bérleti díj jogosultjainál jelenik
    // meg. Üresen a számla egyenlő arányt (1/n) feltételez.
    public string? Share { get; set; }
}
