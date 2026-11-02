using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    // Kézzel kiegészítve: a SaaS előtti adatok az alapértelmezett cégbe kerülnek (ld. Up() vége).
    /// <inheritdoc />
    public partial class CompanyTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "UtilityReminderLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "UtilityCostEntries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Tenants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "PropertyOwners",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Properties",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Owners",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Invoices",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "InvoiceLines",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppSettings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "AppSettings",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "CompanyId",
                value: new Guid("d0000000-0000-4000-8000-000000000001"));

            migrationBuilder.InsertData(
                table: "Companies",
                columns: new[] { "Id", "CreatedAt", "Name" },
                values: new object[] { new Guid("d0000000-0000-4000-8000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Alapértelmezett cég" });

            migrationBuilder.CreateIndex(
                name: "IX_UtilityReminderLogs_CompanyId",
                table: "UtilityReminderLogs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityCostEntries_CompanyId",
                table: "UtilityCostEntries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_CompanyId",
                table: "Users",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_CompanyId",
                table: "Tenants",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyOwners_CompanyId",
                table: "PropertyOwners",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Properties_CompanyId",
                table: "Properties",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Owners_CompanyId",
                table: "Owners",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyId",
                table: "Invoices",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_CompanyId",
                table: "InvoiceLines",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_CompanyId",
                table: "AppSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AppSettings_Companies_CompanyId",
                table: "AppSettings",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLines_Companies_CompanyId",
                table: "InvoiceLines",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Companies_CompanyId",
                table: "Invoices",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Owners_Companies_CompanyId",
                table: "Owners",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Companies_CompanyId",
                table: "Properties",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyOwners_Companies_CompanyId",
                table: "PropertyOwners",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_Companies_CompanyId",
                table: "Tenants",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UtilityCostEntries_Companies_CompanyId",
                table: "UtilityCostEntries",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UtilityReminderLogs_Companies_CompanyId",
                table: "UtilityReminderLogs",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        
            // Kézzel: a cég neve a (régi, egyetlen) beállítás-sor számlakibocsátó neve, ha meg van adva.
            migrationBuilder.Sql("""UPDATE "Companies" c SET "Name" = s."IssuerName" FROM "AppSettings" s WHERE c."Id" = 'd0000000-0000-4000-8000-000000000001' AND s."CompanyId" = c."Id" AND coalesce(s."IssuerName", '') <> '';""");

            // Kézzel: az alapértelmezett érték csak a meglévő sorok átsorolásához kellett — új sor soha ne
            // kerülhessen csendben az első céghez (a CompanyId-t a DbContext tölti ki és ellenőrzi).
            migrationBuilder.Sql("""ALTER TABLE "AppSettings" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "InvoiceLines" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "Invoices" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "Owners" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "Properties" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "PropertyOwners" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "Tenants" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "Users" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "UtilityCostEntries" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
            migrationBuilder.Sql("""ALTER TABLE "UtilityReminderLogs" ALTER COLUMN "CompanyId" DROP DEFAULT;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppSettings_Companies_CompanyId",
                table: "AppSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLines_Companies_CompanyId",
                table: "InvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Companies_CompanyId",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Owners_Companies_CompanyId",
                table: "Owners");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Companies_CompanyId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_PropertyOwners_Companies_CompanyId",
                table: "PropertyOwners");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_Companies_CompanyId",
                table: "Tenants");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_UtilityCostEntries_Companies_CompanyId",
                table: "UtilityCostEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_UtilityReminderLogs_Companies_CompanyId",
                table: "UtilityReminderLogs");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_UtilityReminderLogs_CompanyId",
                table: "UtilityReminderLogs");

            migrationBuilder.DropIndex(
                name: "IX_UtilityCostEntries_CompanyId",
                table: "UtilityCostEntries");

            migrationBuilder.DropIndex(
                name: "IX_Users_CompanyId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_CompanyId",
                table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_PropertyOwners_CompanyId",
                table: "PropertyOwners");

            migrationBuilder.DropIndex(
                name: "IX_Properties_CompanyId",
                table: "Properties");

            migrationBuilder.DropIndex(
                name: "IX_Owners_CompanyId",
                table: "Owners");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_CompanyId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_CompanyId",
                table: "InvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_AppSettings_CompanyId",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "UtilityReminderLogs");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "UtilityCostEntries");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "PropertyOwners");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Owners");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppSettings");
        }
    }
}
