using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoicePartialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Invoices",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            // A már fizetettre állított bizonylatok befizetett összege a hozzájuk kötött befizetésekből.
            migrationBuilder.Sql("""
                UPDATE "Invoices" i SET "PaidAmount" = p.total
                FROM (SELECT "InvoiceId", sum("Amount") AS total FROM "TenantPayments" WHERE "InvoiceId" IS NOT NULL GROUP BY "InvoiceId") p
                WHERE p."InvoiceId" = i."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Invoices");
        }
    }
}
