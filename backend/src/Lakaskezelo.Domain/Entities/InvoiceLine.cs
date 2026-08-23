namespace Lakaskezelo.Domain.Entities;

public class InvoiceLine
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
