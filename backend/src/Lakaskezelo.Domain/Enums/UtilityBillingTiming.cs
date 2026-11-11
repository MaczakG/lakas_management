namespace Lakaskezelo.Domain.Enums;

// Melyik hónap rezsije kerül a bizonylatra. Az int érték tárolódik: a sorrend nem változhat.
public enum UtilityBillingTiming
{
    SameMonth = 0,     // alap: a bizonylat hónapjának rezsije (a bérleti díjjal együtt)
    PreviousMonth = 1, // egy hónap csúszással: az előző hónap rezsije (pl. a decemberi bizonylaton a novemberi rezsi)
}
