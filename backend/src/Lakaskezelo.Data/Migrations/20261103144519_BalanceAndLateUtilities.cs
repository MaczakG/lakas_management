using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class BalanceAndLateUtilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExcludedFromBilling",
                table: "UtilityCostEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "InvoiceId",
                table: "UtilityCostEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RentPaymentTiming",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "TenantPaysSzja",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TenantPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantPayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantPayments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UtilityCostEntries_InvoiceId",
                table: "UtilityCostEntries",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_CompanyId",
                table: "TenantPayments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_TenantId_Date",
                table: "TenantPayments",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.AddForeignKey(
                name: "FK_UtilityCostEntries_Invoices_InvoiceId",
                table: "UtilityCostEntries",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // A meglévő rezsi tételek a hónapjuk aktív (kiállított, nem sztornózott) bizonylatához kötődnek — ez zárolja
            // őket. A bizonylat nélküli, már elmúlt hónapra szóló tételek (a rendszer használata előttiek) nem kerülnek
            // automatikusan bizonylatra; a sztornózott vagy sikertelen bizonylatú hónapok tételei viszont igen.
            // Meglévő adatok: az időszak aktív, kiállított bizonylatán szereplő tételek hozzá kötődnek (zároltak). A korábbi
            // hónapok bizonylat nélküli tételei (soha nem bizonylatolt vagy csak sikertelen próbálkozás) nem kerülnek
            // utólag a következő bizonylatra — kivéve a sztornózott időszakét: azt a sztornó épp újraszámlázásra nyitotta meg.
            migrationBuilder.Sql("""
                UPDATE "UtilityCostEntries" u SET "InvoiceId" = i."Id"
                FROM "Invoices" i
                WHERE i."PropertyId" = u."PropertyId" AND i."PeriodYear" = u."Year" AND i."PeriodMonth" = u."Month"
                  AND i."CancelledAt" IS NULL AND i."CancelsInvoiceId" IS NULL AND i."Status" IN (0, 1);

                UPDATE "UtilityCostEntries" u SET "ExcludedFromBilling" = true
                WHERE u."InvoiceId" IS NULL
                  AND make_date(u."Year", u."Month", 1) < date_trunc('month', now() AT TIME ZONE 'Europe/Budapest')::date
                  AND NOT EXISTS (SELECT 1 FROM "Invoices" i
                                  WHERE i."PropertyId" = u."PropertyId" AND i."PeriodYear" = u."Year" AND i."PeriodMonth" = u."Month"
                                    AND i."CancelledAt" IS NOT NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UtilityCostEntries_Invoices_InvoiceId",
                table: "UtilityCostEntries");

            migrationBuilder.DropTable(
                name: "TenantPayments");

            migrationBuilder.DropIndex(
                name: "IX_UtilityCostEntries_InvoiceId",
                table: "UtilityCostEntries");

            migrationBuilder.DropColumn(
                name: "ExcludedFromBilling",
                table: "UtilityCostEntries");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "UtilityCostEntries");

            migrationBuilder.DropColumn(
                name: "RentPaymentTiming",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TenantPaysSzja",
                table: "Tenants");
        }
    }
}
