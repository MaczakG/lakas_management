using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveInInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MoveInInfoSentAt",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MoveInInfo",
                table: "Properties",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MoveInInfoSendable",
                table: "Properties",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MoveInInfoSentAt",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "MoveInInfo",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "MoveInInfoSendable",
                table: "Properties");
        }
    }
}
