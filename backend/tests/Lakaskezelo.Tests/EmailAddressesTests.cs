using Lakaskezelo.Api.Notifications;

namespace Lakaskezelo.Tests;

public class EmailAddressesTests
{
    [Fact]
    public void Split_accepts_comma_semicolon_space_and_new_line()
    {
        Assert.Equal(["a@example.com", "b@example.com", "c@example.com", "d@example.com"],
            EmailAddresses.Split(" a@example.com, b@example.com;c@example.com\nd@example.com "));
    }

    [Fact]
    public void Split_drops_repeated_addresses_and_keeps_the_order()
    {
        Assert.Equal(["b@example.com", "a@example.com"], EmailAddresses.Split("b@example.com, a@example.com, B@Example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , ; ")]
    public void Normalize_treats_blank_as_null(string? value)
    {
        Assert.Null(EmailAddresses.Normalize(value, out var error));
        Assert.Null(error);
        Assert.Null(EmailAddresses.First(value));
    }

    [Fact]
    public void Normalize_joins_the_addresses_with_comma_and_space()
    {
        Assert.Equal("a@example.com, b@example.com", EmailAddresses.Normalize("a@example.com;b@example.com", out var error));
        Assert.Null(error);
        Assert.Equal("a@example.com", EmailAddresses.First("a@example.com, b@example.com"));
    }

    [Fact]
    public void Normalize_names_the_invalid_address()
    {
        Assert.Null(EmailAddresses.Normalize("a@example.com, nem-cim", out var error));
        Assert.Contains("nem-cim", error);
    }

    [Fact]
    public void Normalize_limits_the_number_of_addresses()
    {
        var many = string.Join(", ", Enumerable.Range(1, EmailAddresses.MaxCount + 1).Select(i => $"cim{i}@example.com"));
        Assert.Null(EmailAddresses.Normalize(many, out var error));
        Assert.NotNull(error);

        var allowed = string.Join(", ", Enumerable.Range(1, EmailAddresses.MaxCount).Select(i => $"cim{i}@example.com"));
        Assert.Equal(allowed, EmailAddresses.Normalize(allowed, out error));
        Assert.Null(error);
    }
}
