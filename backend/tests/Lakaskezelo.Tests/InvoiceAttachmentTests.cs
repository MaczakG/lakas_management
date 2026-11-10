using Lakaskezelo.Api.Billing;
using Lakaskezelo.Domain.Entities;
using Lakaskezelo.Domain.Enums;

namespace Lakaskezelo.Tests;

public class InvoiceAttachmentTests
{
    [Theory]
    // A bérlő beállítása dönt, a kézi válasz ilyenkor nem számít.
    [InlineData(InvoiceAttachmentPreference.Send, null, true)]
    [InlineData(InvoiceAttachmentPreference.Send, false, true)]
    [InlineData(InvoiceAttachmentPreference.DontSend, null, false)]
    [InlineData(InvoiceAttachmentPreference.DontSend, true, false)]
    // Nincs beállítva: a kézi bizonylatkészítéskor adott válasz dönt; válasz nélkül (időzített küldés) nem megy.
    [InlineData(InvoiceAttachmentPreference.Ask, true, true)]
    [InlineData(InvoiceAttachmentPreference.Ask, false, false)]
    [InlineData(InvoiceAttachmentPreference.Ask, null, false)]
    public void Attachments_follow_tenant_preference_then_the_answer(InvoiceAttachmentPreference preference, bool? answer, bool expected)
    {
        var tenant = new Tenant { InvoiceAttachments = preference };
        Assert.Equal(expected, InvoiceGenerationService.ShouldSendAttachments(tenant, answer));
    }

    [Fact]
    public void Without_tenant_only_an_explicit_yes_sends_attachments()
    {
        Assert.False(InvoiceGenerationService.ShouldSendAttachments(null, null));
        Assert.True(InvoiceGenerationService.ShouldSendAttachments(null, true));
    }

    [Fact]
    public void New_tenant_has_no_preference_set()
    {
        Assert.Equal(InvoiceAttachmentPreference.Ask, new Tenant().InvoiceAttachments);
    }
}
