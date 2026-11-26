namespace Lakaskezelo.Domain.Entities;

// Beköltözési tudnivaló (az ingatlan adatlap „Beköltözés” fülének egy sora): megnevezés és megjegyzés, pl.
// „Kapukód” – „1234”. Az összes sor kiküldhető e-mailben a bérlőnek; a bepipáltak (InChecklist) az átadás-átvételi
// jegyzőkönyv ellenőrzőlistájára is rákerülnek.
public class MoveInItem : ICompanyOwned
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PropertyId { get; set; }
    public Property? Property { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool InChecklist { get; set; }
    public int SortOrder { get; set; }
}
