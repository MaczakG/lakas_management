using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Api.Billing;

// A bizonylat fizetettsége a hozzá kötött befizetésekből (TenantPayment.InvoiceId): a befizetett összeg és — ha elérte a
// végösszeget — a teljes kifizetés napja (az a befizetés, amellyel a halmozott összeg elérte a végösszeget).
public static class InvoicePayments
{
    // A befizetések mentése UTÁN hívandó (az adatbázis állapotából számol); a hívó menti el az eredményt.
    public static async Task RecalculateAsync(LakaskezeloDbContext db, Invoice invoice, CancellationToken ct)
    {
        var payments = await db.TenantPayments.Where(p => p.InvoiceId == invoice.Id)
            .OrderBy(p => p.Date).ThenBy(p => p.CreatedAt)
            .Select(p => new { p.Date, p.Amount })
            .ToListAsync(ct);
        invoice.PaidAmount = payments.Sum(p => p.Amount);
        invoice.PaidAt = null;
        if (invoice.AmountTotal <= 0 || invoice.PaidAmount < invoice.AmountTotal) return;
        var running = 0m;
        foreach (var p in payments)
        {
            running += p.Amount;
            if (running >= invoice.AmountTotal) { invoice.PaidAt = p.Date; break; }
        }
    }
}
