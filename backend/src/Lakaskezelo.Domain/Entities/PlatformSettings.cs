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

    public decimal MonthlyNetPricePerProperty { get; set; } = 990m;
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

    public DateTime UpdatedAt { get; set; }
}
