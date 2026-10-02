using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    // Kézzel kiegészítve: a napi árfolyam-előzményből devizánként csak a legfrissebb sor marad meg
    // (a számlák a felhasznált árfolyamot a saját szövegükben őrzik), különben az új egyedi index
    // nem hozható létre.
    /// <inheritdoc />
    public partial class SingleExchangeRatePerCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "ExchangeRates" r
                WHERE EXISTS (
                    SELECT 1 FROM "ExchangeRates" n
                    WHERE n."CurrencyCode" = r."CurrencyCode"
                      AND (n."RateDate", n."FetchedAt", n."Id") > (r."RateDate", r."FetchedAt", r."Id"));
                """);

            migrationBuilder.DropIndex(
                name: "IX_ExchangeRates_CurrencyCode_RateDate",
                table: "ExchangeRates");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyCode",
                table: "ExchangeRates",
                column: "CurrencyCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExchangeRates_CurrencyCode",
                table: "ExchangeRates");

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRates_CurrencyCode_RateDate",
                table: "ExchangeRates",
                columns: new[] { "CurrencyCode", "RateDate" },
                unique: true);
        }
    }
}
