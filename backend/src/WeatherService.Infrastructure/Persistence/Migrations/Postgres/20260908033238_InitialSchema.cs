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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    location_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    retrieved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecast_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "forecast_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    forecast_snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    min_temperature_c = table.Column<double>(type: "double precision", nullable: false),
                    max_temperature_c = table.Column<double>(type: "double precision", nullable: false),
                    condition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecast_days", x => x.id);
                    table.ForeignKey(
                        name: "fk_forecast_days_forecast_snapshots_forecast_snapshot_id",
                        column: x => x.forecast_snapshot_id,
                        principalTable: "forecast_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_forecast_days_forecast_snapshot_id_date",
                table: "forecast_days",
                columns: new[] { "forecast_snapshot_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshots_location_key_retrieved_at",
                table: "forecast_snapshots",
                columns: new[] { "location_key", "retrieved_at_utc" });
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
