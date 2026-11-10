using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class UtilityItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ForwardBilled",
                table: "UtilityCostEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "UtilityItemId",
                table: "UtilityCostEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UtilityItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsFixed = table.Column<bool>(type: "boolean", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    ForwardBilled = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilityItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilityItems_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UtilityItems_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UtilityPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    PreparedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UtilityPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UtilityPeriods_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UtilityPeriods_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UtilityCostEntries_UtilityItemId",
                table: "UtilityCostEntries",
                column: "UtilityItemId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityItems_CompanyId",
                table: "UtilityItems",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityItems_PropertyId",
                table: "UtilityItems",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityPeriods_CompanyId",
                table: "UtilityPeriods",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_UtilityPeriods_PropertyId_Year_Month",
                table: "UtilityPeriods",
                columns: new[] { "PropertyId", "Year", "Month" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UtilityCostEntries_UtilityItems_UtilityItemId",
                table: "UtilityCostEntries",
                column: "UtilityItemId",
                principalTable: "UtilityItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UtilityCostEntries_UtilityItems_UtilityItemId",
                table: "UtilityCostEntries");

            migrationBuilder.DropTable(
                name: "UtilityItems");

            migrationBuilder.DropTable(
                name: "UtilityPeriods");

            migrationBuilder.DropIndex(
                name: "IX_UtilityCostEntries_UtilityItemId",
                table: "UtilityCostEntries");

            migrationBuilder.DropColumn(
                name: "ForwardBilled",
                table: "UtilityCostEntries");

            migrationBuilder.DropColumn(
                name: "UtilityItemId",
                table: "UtilityCostEntries");
        }
    }
}
