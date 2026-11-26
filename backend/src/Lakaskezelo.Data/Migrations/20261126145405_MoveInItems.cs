using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lakaskezelo.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoveInItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MoveInItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InChecklist = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoveInItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MoveInItems_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MoveInItems_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MoveInItems_CompanyId",
                table: "MoveInItems",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_MoveInItems_PropertyId",
                table: "MoveInItems",
                column: "PropertyId");

            // A korábbi szabad szöveg soronként egy-egy sor lesz: a „Megnevezés: megjegyzés” alakú sor a (első)
            // kettőspontnál kettéválik, a többi sor csak megnevezés. Az üres sorok kimaradnak; jegyzőkönyvre egyik sem
            // kerül (azt a felhasználó pipálja be).
            migrationBuilder.Sql("""
                INSERT INTO "MoveInItems" ("Id", "CompanyId", "PropertyId", "Label", "Note", "InChecklist", "SortOrder")
                SELECT gen_random_uuid(), p."CompanyId", p."Id",
                       left(CASE WHEN btrim(left(l.line, greatest(l.pos - 1, 0))) <> '' THEN btrim(left(l.line, l.pos - 1)) ELSE btrim(l.line) END, 200),
                       nullif(left(CASE WHEN btrim(left(l.line, greatest(l.pos - 1, 0))) <> '' THEN btrim(substr(l.line, l.pos + 1)) ELSE '' END, 1000), ''),
                       false, (l.ord - 1)::int
                FROM "Properties" p
                CROSS JOIN LATERAL (
                    SELECT t.line, t.ord, position(':' in t.line) AS pos
                    FROM regexp_split_to_table(p."MoveInInfo", E'\r?\n') WITH ORDINALITY AS t(line, ord)
                ) l
                WHERE p."MoveInInfo" IS NOT NULL AND btrim(l.line) <> '';
                """);

            migrationBuilder.DropColumn(
                name: "MoveInInfo",
                table: "Properties");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MoveInItems");

            migrationBuilder.AddColumn<string>(
                name: "MoveInInfo",
                table: "Properties",
                type: "character varying(5000)",
                maxLength: 5000,
                nullable: true);
        }
    }
}
