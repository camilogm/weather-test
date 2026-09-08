using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeatherService.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forecast_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    RetrievedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "forecast_days",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ForecastSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    MinTemperatureC = table.Column<double>(type: "double precision", nullable: false),
                    MaxTemperatureC = table.Column<double>(type: "double precision", nullable: false),
                    Condition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_days", x => x.Id);
                    table.ForeignKey(
                        name: "FK_forecast_days_forecast_snapshots_ForecastSnapshotId",
                        column: x => x.ForecastSnapshotId,
                        principalTable: "forecast_snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_forecast_days_ForecastSnapshotId_Date",
                table: "forecast_days",
                columns: new[] { "ForecastSnapshotId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshots_location_key_retrieved_at",
                table: "forecast_snapshots",
                columns: new[] { "LocationKey", "RetrievedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_days");

            migrationBuilder.DropTable(
                name: "forecast_snapshots");
        }
    }
}
