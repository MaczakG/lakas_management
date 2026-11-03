using Lakaskezelo.Api.Billing;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Tests;

public class BalanceRulesTests
{
    [Theory]
    [InlineData(2026, 12, 2024, 1)]  // 36 hónap a decemberrel együtt
    [InlineData(2026, 1, 2023, 2)]
    [InlineData(2026, 3, 2023, 4)]
    public void Late_utility_window_is_36_months_inclusive(int year, int month, int fromYear, int fromMonth)
    {
        Assert.Equal((fromYear, fromMonth), InvoiceGenerationService.BillingWindowStart(year, month));
        var span = (year * 12 + month) - (fromYear * 12 + fromMonth) + 1;
        Assert.Equal(InvoiceGenerationService.LateUtilityMonths, span);
    }

    [Fact]
    public void Rent_period_follows_payment_timing()
    {
        Assert.Equal((2026, 12), InvoiceGenerationService.RentPeriod(new Tenant { RentPaymentTiming = RentPaymentTiming.Advance }, 2026, 12));
        Assert.Equal((2026, 11), InvoiceGenerationService.RentPeriod(new Tenant { RentPaymentTiming = RentPaymentTiming.Arrears }, 2026, 12));
        Assert.Equal((2025, 12), InvoiceGenerationService.RentPeriod(new Tenant { RentPaymentTiming = RentPaymentTiming.Arrears }, 2026, 1));
        Assert.Equal((2026, 5), InvoiceGenerationService.RentPeriod(null, 2026, 5));
    }

    [Fact]
    public void Szja_is_deducted_only_for_private_landlords_when_tenant_pays()
    {
        var privateLandlord = new Company { IsBusiness = false };
        var business = new Company { IsBusiness = true };
        var payer = new Tenant { TenantPaysSzja = true };
        var nonPayer = new Tenant { TenantPaysSzja = false };

        Assert.Equal(-27_750m, InvoiceGenerationService.SzjaDeduction(privateLandlord, false, payer, 185_000m));
        Assert.Equal(-15m, InvoiceGenerationService.SzjaDeduction(privateLandlord, false, payer, 100m));
        Assert.Equal(0m, InvoiceGenerationService.SzjaDeduction(privateLandlord, false, nonPayer, 185_000m));
        Assert.Equal(0m, InvoiceGenerationService.SzjaDeduction(business, false, payer, 185_000m));
        Assert.Equal(0m, InvoiceGenerationService.SzjaDeduction(privateLandlord, true, payer, 185_000m));
        Assert.Equal(0m, InvoiceGenerationService.SzjaDeduction(privateLandlord, false, null, 185_000m));
    }
}
