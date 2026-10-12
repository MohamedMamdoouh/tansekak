using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tansekak.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudentResultsImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Leftover rows predate this format (they carried track and case text). The table is empty
            // after ClearStudentResults; this makes that explicit before the columns go away.
            migrationBuilder.Sql("""DELETE FROM "StudentResults";""");

            migrationBuilder.DropIndex(
                name: "IX_StudentResults_AdmissionYearId_Track_TotalDegree_SeatingNo",
                table: "StudentResults");

            migrationBuilder.DropIndex(
                name: "IX_StudentResults_SeatingNo",
                table: "StudentResults");

            migrationBuilder.DropColumn(
                name: "StudentCaseDesc",
                table: "StudentResults");

            migrationBuilder.DropColumn(
                name: "Track",
                table: "StudentResults");

            migrationBuilder.CreateTable(
                name: "StudentResultImportEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    AdmissionYearId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    RowCount = table.Column<int>(type: "integer", nullable: true),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PerformedByEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentResultImportEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentResultImportEvents_AdmissionYears_AdmissionYearId",
                        column: x => x.AdmissionYearId,
                        principalTable: "AdmissionYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentResultImportEvents_AdmissionYearId_PerformedAt",
                table: "StudentResultImportEvents",
                columns: new[] { "AdmissionYearId", "PerformedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentResultImportEvents");

            migrationBuilder.AddColumn<string>(
                name: "StudentCaseDesc",
                table: "StudentResults",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte>(
                name: "Track",
                table: "StudentResults",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentResults_AdmissionYearId_Track_TotalDegree_SeatingNo",
                table: "StudentResults",
                columns: new[] { "AdmissionYearId", "Track", "TotalDegree", "SeatingNo" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentResults_SeatingNo",
                table: "StudentResults",
                column: "SeatingNo");
        }
    }
}
