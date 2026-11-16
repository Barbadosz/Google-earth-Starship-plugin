using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarshipMissionTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSimulationLaunchTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SimulationLaunchTimeUtc",
                table: "Missions",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SimulationLaunchTimeUtc",
                table: "Missions");
        }
    }
}
