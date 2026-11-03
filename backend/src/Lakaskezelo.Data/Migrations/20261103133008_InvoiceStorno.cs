using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceStorno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices");

            // Archiválás → sztornózás: az „archiválva” időpont és felhasználó a „sztornózva” lesz.
            migrationBuilder.RenameColumn(
                name: "ArchivedAt",
                table: "Invoices",
                newName: "CancelledAt");

            migrationBuilder.RenameColumn(
                name: "ArchivedByUserId",
                table: "Invoices",
                newName: "CancelledByUserId");

            migrationBuilder.AddColumn<Guid>(
                name: "CancelsInvoiceId",
                table: "Invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CancelsInvoiceId",
                table: "Invoices",
                column: "CancelsInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Invoices_CancelsInvoiceId",
                table: "Invoices",
                column: "CancelsInvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // A korábban archivált (kiállított) számlákhoz sztornó bizonylat készül: negatív sorokkal, „…-S” sorszámmal,
            // legenerált állapotban. A PDF-je az első letöltéskor készül el (InvoicePdfStore).
            migrationBuilder.Sql("""
                CREATE TEMP TABLE storno_map ON COMMIT DROP AS
                SELECT "Id" AS original_id, gen_random_uuid() AS storno_id
                FROM "Invoices" WHERE "CancelledAt" IS NOT NULL AND "CancelsInvoiceId" IS NULL AND "Status" <> 2;

                INSERT INTO "Invoices" ("Id", "CompanyId", "PropertyId", "TenantId", "PeriodYear", "PeriodMonth", "Number", "IssuedAt",
                    "DueDate", "AmountTotal", "Status", "CancelsInvoiceId")
                SELECT m.storno_id, i."CompanyId", i."PropertyId", i."TenantId", i."PeriodYear", i."PeriodMonth", i."Number" || '-S',
                    i."CancelledAt", (i."CancelledAt" AT TIME ZONE 'UTC')::date, -i."AmountTotal", 0, i."Id"
                FROM "Invoices" i JOIN storno_map m ON m.original_id = i."Id";

                INSERT INTO "InvoiceLines" ("Id", "CompanyId", "InvoiceId", "Label", "Amount", "Position")
                SELECT gen_random_uuid(), l."CompanyId", m.storno_id, l."Label", -l."Amount", l."Position"
                FROM "InvoiceLines" l JOIN storno_map m ON m.original_id = l."InvoiceId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices",
                columns: new[] { "PropertyId", "PeriodYear", "PeriodMonth" },
                unique: true,
                filter: "\"CancelledAt\" IS NULL AND \"CancelsInvoiceId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A sztornó bizonylatok törlődnek (az eredetik archiváltként maradnak).
            migrationBuilder.Sql("""
                DELETE FROM "InvoiceLines" WHERE "InvoiceId" IN (SELECT "Id" FROM "Invoices" WHERE "CancelsInvoiceId" IS NOT NULL);
                DELETE FROM "Invoices" WHERE "CancelsInvoiceId" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Invoices_CancelsInvoiceId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_CancelsInvoiceId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CancelsInvoiceId",
                table: "Invoices");

            migrationBuilder.RenameColumn(
                name: "CancelledByUserId",
                table: "Invoices",
                newName: "ArchivedByUserId");

            migrationBuilder.RenameColumn(
                name: "CancelledAt",
                table: "Invoices",
                newName: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PropertyId_PeriodYear_PeriodMonth",
                table: "Invoices",
                columns: new[] { "PropertyId", "PeriodYear", "PeriodMonth" },
                unique: true,
                filter: "\"ArchivedAt\" IS NULL");
        }
    }
}
