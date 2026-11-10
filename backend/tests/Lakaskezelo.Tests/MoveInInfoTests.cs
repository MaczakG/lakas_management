using Lakaskezelo.Api.Controllers;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Tests;

public class MoveInInfoTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Fact]
    public void Clean_normalizes_line_endings_and_treats_blank_as_null()
    {
        Assert.Equal("Kapukód: 1234\nWifi: otthon", MoveInInfo.Clean("  Kapukód: 1234\r\nWifi: otthon\r\n\r\n"));
        Assert.Null(MoveInInfo.Clean("  \r\n "));
        Assert.Null(MoveInInfo.Clean(null));
    }

    [Fact]
    public void Recipients_are_current_and_future_tenants_current_first()
    {
        var moved = new Tenant { Id = Guid.NewGuid(), Name = "Kiköltözött", MoveInDate = new(2024, 1, 1), MoveOutDate = new(2026, 10, 31) };
        var current = new Tenant { Id = Guid.NewGuid(), Name = "Jelenlegi", MoveInDate = new(2026, 11, 1) };
        var leavingToday = new Tenant { Id = Guid.NewGuid(), Name = "Ma költözik ki", MoveInDate = new(2025, 1, 1), MoveOutDate = Today };
        var future = new Tenant { Id = Guid.NewGuid(), Name = "Leendő", MoveInDate = new(2026, 12, 1) };
        var later = new Tenant { Id = Guid.NewGuid(), Name = "Később jön", MoveInDate = new(2027, 1, 1) };

        var result = MoveInInfo.Recipients([later, future, moved, leavingToday, current], Today);

        Assert.Equal(["Jelenlegi", "Ma költözik ki", "Leendő", "Később jön"], result.Select(t => t.Name));
    }

    [Fact]
    public void Body_keeps_paragraphs_and_line_breaks_and_encodes_html()
    {
        var (html, plain) = MoveInInfo.Body(" Kiss Anna ", "Tata, Kocsi u. 17.", "Kapukód: <1234>\nWifi: otthon & kert\n\nSzemétszállítás: kedd");

        Assert.Contains("Kedves Kiss Anna!", plain);
        Assert.Contains("tudnivalókat.\nIngatlan: Tata, Kocsi u. 17.\n\n", plain);
        Assert.Contains("Kapukód: <1234>\nWifi: otthon & kert\n\nSzemétszállítás: kedd", plain);
        // (Az ékezetes betűket a HtmlEncode számkóddal írja ki, ezért itt ékezet nélküli részekre nézünk.)
        Assert.Contains("&lt;1234&gt;<br>Wifi: otthon &amp; kert</p>", html);
        Assert.Contains(": kedd</p><p style=\"margin:0 0 12px;\">Ha k", html);
        Assert.DoesNotContain("<1234>", html);
    }
}
