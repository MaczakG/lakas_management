using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class PropertySheet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BuildYear",
                table: "Properties",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Properties",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Deposit",
                table: "Properties",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ElectricityMeter",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FloorArea",
                table: "Properties",
                type: "numeric(9,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FloorDoor",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GasMeter",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hrsz",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Properties",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "Properties",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropertyType",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Rooms",
                table: "Properties",
                type: "numeric(4,1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "Properties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaterMeter",
                table: "Properties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildYear",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Deposit",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "ElectricityMeter",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "FloorArea",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "FloorDoor",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "GasMeter",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Hrsz",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "PropertyType",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Rooms",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "WaterMeter",
                table: "Properties");
        }
    }
}
