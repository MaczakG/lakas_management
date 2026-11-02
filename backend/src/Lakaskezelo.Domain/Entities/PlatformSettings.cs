namespace Lakaskezelo.Domain.Entities;

// A platform (az üzemeltető) egyetlen beállítás-sora: az előfizetési díjak és a fizetési/számlázási
// szolgáltatók kulcsai. Nem cégadat — csak az üzemeltető cég adminisztrátorai látják/szerkesztik
// (Platform:OperatorCompanyId). A titkos kulcsokat az API nem adja vissza (csak felülírni lehet).
public class PlatformSettings
{
    public static readonly Guid SingletonId = new("22222222-2222-4222-8222-222222222222");

    public Guid Id { get; set; }

    // Főkapcsoló: amíg ki van kapcsolva, senki nem fizet és semmi nem zárolódik.
    public bool BillingEnabled { get; set; }

    // Havi díj aktív ingatlanonként (Ft). PricesIncludeVat: a díj bruttó (az ÁFÁ-t tartalmazza) — ilyenkor
    // a terhelt összeg pontosan ennyi × ingatlanszám, a nettó és az ÁFA ebből adódik.
    public decimal MonthlyPricePerProperty { get; set; } = 1990m;
    public bool PricesIncludeVat { get; set; } = true;
    public decimal YearlyDiscountPercent { get; set; } = 15m;
    public int TrialDays { get; set; } = 14;
    public int GraceDays { get; set; } = 7;
    // ÁFA-kulcs (pl. 27), vagy ha alanyi adómentes, a mentesség kódja (pl. "AAM") — utóbbi az erősebb.
    public decimal VatPercent { get; set; } = 27m;
    public string? VatExemption { get; set; }

    public string BarionEnvironment { get; set; } = "Test";
    public string? BarionPosKey { get; set; }
    public string? BarionPayeeEmail { get; set; }

    public string? BillingoApiKey { get; set; }
    public int? BillingoBlockId { get; set; }

    // Az üzemeltető (szolgáltató) adatai — az ÁSZF és az adatkezelési tájékoztató ezekkel töltődik ki.
    public string? OperatorName { get; set; }
    public string? OperatorAddress { get; set; }
    public string? OperatorTaxNumber { get; set; }
    public string? OperatorRegistrationNumber { get; set; }
    public string? OperatorEmail { get; set; }
    public string? OperatorPhone { get; set; }
    // A jogi dokumentumok hatályos változata (pl. "2026-11-03") — regisztrációkor ezt fogadja el a cég.
    public string TermsVersion { get; set; } = "2026-11-03";

    public DateTime UpdatedAt { get; set; }
}
