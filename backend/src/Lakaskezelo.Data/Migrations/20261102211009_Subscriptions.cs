using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class Subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyPricePerProperty = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    PricesIncludeVat = table.Column<bool>(type: "boolean", nullable: false),
                    YearlyDiscountPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    GraceDays = table.Column<int>(type: "integer", nullable: false),
                    VatPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    VatExemption = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    BarionEnvironment = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BarionPosKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BarionPayeeEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BillingoApiKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingoBlockId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    PropertyCount = table.Column<int>(type: "integer", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    UnitPriceIncludesVat = table.Column<bool>(type: "boolean", nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    VatPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    VatExemption = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BarionPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RecurrenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BillingoDocumentId = table.Column<int>(type: "integer", nullable: true),
                    BillingoPublicUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BillingoError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BillingoAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPayments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Interval = table.Column<int>(type: "integer", nullable: false),
                    TrialEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GraceEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelAtPeriodEnd = table.Column<bool>(type: "boolean", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrialReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BarionRecurrenceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BarionTraceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CardRegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BillingName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingTaxNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BillingPostCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    BillingCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BillingStreet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BillingEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BillingoPartnerId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PlatformSettings",
                columns: new[] { "Id", "BarionEnvironment", "BarionPayeeEmail", "BarionPosKey", "BillingEnabled", "BillingoApiKey", "BillingoBlockId", "GraceDays", "MonthlyPricePerProperty", "PricesIncludeVat", "TrialDays", "UpdatedAt", "VatExemption", "VatPercent", "YearlyDiscountPercent" },
                values: new object[] { new Guid("22222222-2222-4222-8222-222222222222"), "Test", null, null, false, null, null, 7, 1990m, true, 14, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 27m, 15m });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_BarionPaymentId",
                table: "SubscriptionPayments",
                column: "BarionPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_CompanyId",
                table: "SubscriptionPayments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_Status_CreatedAt",
                table: "SubscriptionPayments",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_CompanyId",
                table: "Subscriptions",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettings");

            migrationBuilder.DropTable(
                name: "SubscriptionPayments");

            migrationBuilder.DropTable(
                name: "Subscriptions");
        }
    }
}
