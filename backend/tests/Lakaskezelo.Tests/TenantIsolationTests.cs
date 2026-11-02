using Lakaskezelo.Data;
using Lakaskezelo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lakaskezelo.Tests;

// A több-cégesség (SaaS) alapgaranciái a DbContext szintjén: egy cég nem lát, nem ír át és nem
// hivatkozik másik cég adatára — akkor sem, ha a kód elfelejt szűrni.
public class TenantIsolationTests
{
    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    private readonly string _dbName = Guid.NewGuid().ToString();

    private LakaskezeloDbContext Db(Guid? company)
    {
        var context = new CompanyContext();
        context.Set(company);
        var options = new DbContextOptionsBuilder<LakaskezeloDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new LakaskezeloDbContext(options, context);
    }

    private static Property NewProperty(string name) => new() { Id = Guid.NewGuid(), Name = name, InvoicePrefix = "T" };

    private async Task<(Guid propA, Guid propB)> SeedTwoCompaniesAsync()
    {
        await using (var db = Db(null))
        {
            db.Companies.AddRange(new Company { Id = CompanyA, Name = "A" }, new Company { Id = CompanyB, Name = "B" });
            await db.SaveChangesAsync();
        }
        var propA = NewProperty("A lakás");
        var propB = NewProperty("B lakás");
        await using (var db = Db(CompanyA)) { db.Properties.Add(propA); await db.SaveChangesAsync(); }
        await using (var db = Db(CompanyB)) { db.Properties.Add(propB); await db.SaveChangesAsync(); }
        return (propA.Id, propB.Id);
    }

    [Fact]
    public async Task Each_company_sees_only_its_own_rows()
    {
        var (propA, propB) = await SeedTwoCompaniesAsync();

        await using var dbA = Db(CompanyA);
        await using var dbB = Db(CompanyB);
        Assert.Equal([propA], await dbA.Properties.Select(p => p.Id).ToListAsync());
        Assert.Equal([propB], await dbB.Properties.Select(p => p.Id).ToListAsync());
        Assert.Null(await dbA.Properties.FindAsync(propB));
        Assert.False(await dbB.Properties.AnyAsync(p => p.Id == propA));
    }

    [Fact]
    public async Task Without_company_context_nothing_is_visible()
    {
        await SeedTwoCompaniesAsync();
        await using var db = Db(null);
        Assert.Empty(await db.Properties.ToListAsync());
        Assert.Equal(2, await db.Properties.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task New_rows_get_the_current_company()
    {
        await SeedTwoCompaniesAsync();
        await using var db = Db(CompanyB);
        var owner = new Owner { Id = Guid.NewGuid(), Name = "Tulaj" };
        db.Owners.Add(owner);
        await db.SaveChangesAsync();
        Assert.Equal(CompanyB, owner.CompanyId);
    }

    [Fact]
    public async Task Saving_without_company_context_is_rejected()
    {
        await SeedTwoCompaniesAsync();
        await using var db = Db(null);
        db.Owners.Add(new Owner { Id = Guid.NewGuid(), Name = "Gazdátlan" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Rows_of_another_company_cannot_be_inserted_modified_or_deleted()
    {
        var (_, propB) = await SeedTwoCompaniesAsync();

        await using (var db = Db(CompanyA))
        {
            db.Owners.Add(new Owner { Id = Guid.NewGuid(), Name = "Csempészett", CompanyId = CompanyB });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = Db(CompanyA))
        {
            var foreign = await db.Properties.IgnoreQueryFilters().SingleAsync(p => p.Id == propB);
            foreign.Name = "Feltört";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = Db(CompanyA))
        {
            var foreign = await db.Properties.IgnoreQueryFilters().SingleAsync(p => p.Id == propB);
            db.Properties.Remove(foreign);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = Db(CompanyB))
        {
            Assert.Equal("B lakás", (await db.Properties.SingleAsync()).Name);
        }
    }

    [Fact]
    public async Task CompanyId_cannot_be_changed_afterwards()
    {
        var (propA, _) = await SeedTwoCompaniesAsync();
        await using var db = Db(CompanyA);
        var property = await db.Properties.SingleAsync(p => p.Id == propA);
        property.CompanyId = CompanyB;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task References_to_another_companys_rows_are_rejected()
    {
        var (propA, propB) = await SeedTwoCompaniesAsync();

        // Új sor másik cég ingatlanára mutatva.
        await using (var db = Db(CompanyA))
        {
            db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Name = "Bérlő", PropertyId = propB });
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        // Meglévő sor hivatkozásának átírása másik cég ingatlanára.
        var tenantId = Guid.NewGuid();
        await using (var db = Db(CompanyA))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Bérlő", PropertyId = propA });
            await db.SaveChangesAsync();
        }
        await using (var db = Db(CompanyA))
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId);
            tenant.PropertyId = propB;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task References_within_the_same_company_are_allowed_even_in_one_save()
    {
        await SeedTwoCompaniesAsync();
        await using var db = Db(CompanyA);
        var property = NewProperty("Új");
        var owner = new Owner { Id = Guid.NewGuid(), Name = "Tulaj" };
        db.Properties.Add(property);
        db.Owners.Add(owner);
        db.PropertyOwners.Add(new PropertyOwner { Id = Guid.NewGuid(), PropertyId = property.Id, OwnerId = owner.Id });
        db.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Name = "Bérlő", PropertyId = property.Id });
        await db.SaveChangesAsync();

        Assert.Single(await db.PropertyOwners.ToListAsync());
        Assert.Equal(CompanyA, (await db.Tenants.SingleAsync()).CompanyId);
    }
}
