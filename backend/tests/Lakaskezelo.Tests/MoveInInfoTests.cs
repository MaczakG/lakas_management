using Lakaskezelo.Api.Contracts;
using Lakaskezelo.Api.Controllers;
using Lakaskezelo.Domain.Entities;

namespace Lakaskezelo.Tests;

public class MoveInInfoTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Fact]
    public void CleanItems_trims_skips_empty_rows_and_requires_a_label()
    {
        var (items, error) = MoveInInfo.CleanItems([
            new(" Kapukód ", " 1234 ", true), new("  ", "  ", false), new("Wifi", "otthon\r\njelszó: kert", false), new("Kulcsok", " ", true)]);
        Assert.Null(error);
        Assert.Equal([new("Kapukód", "1234", true), new("Wifi", "otthon\njelszó: kert", false), new MoveInItemDto("Kulcsok", null, true)], items);

        Assert.NotNull(MoveInInfo.CleanItems([new("", "csak megjegyzés", false)]).Error);
        Assert.NotNull(MoveInInfo.CleanItems([new(new string('x', 201), null, false)]).Error);
        Assert.Empty(MoveInInfo.CleanItems(null).Items);
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
    public void Body_lists_rows_in_a_table_and_encodes_html()
    {
        var (html, plain) = MoveInInfo.Body(" Kiss Anna ", "Tata, Kocsi u. 17.",
            [new MoveInItem { Label = "Kapukód", Note = "<1234>" }, new MoveInItem { Label = "Wifi", Note = "otthon & kert\njelszó" }, new MoveInItem { Label = "Kulcsok" }]);

        Assert.Contains("Kedves Kiss Anna!", plain);
        Assert.Contains("tudnivalókat.\nIngatlan: Tata, Kocsi u. 17.\n\n", plain);
        Assert.Contains("Kapukód: <1234>\nWifi: otthon & kert\njelszó\nKulcsok\n\n", plain);
        Assert.Contains("&lt;1234&gt;</td>", html);
        Assert.Contains("otthon &amp; kert<br>jelsz", html);
        Assert.DoesNotContain("<1234>", html);
    }
}
