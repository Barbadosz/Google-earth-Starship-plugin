using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StarshipMissionTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    License = table.Column<string>(type: "TEXT", nullable: true),
                    Reliability = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Missions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MissionCode = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedLaunchTimeUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ActualLaunchTimeUtc = table.Column<long>(type: "INTEGER", nullable: true),
                    IsSimulated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Missions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    ModelPath = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MissionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehicleId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    OffsetSeconds = table.Column<double>(type: "REAL", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    AltitudeMeters = table.Column<double>(type: "REAL", nullable: true),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MissionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MissionEvents_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MissionEvents_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TelemetrySamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VehicleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    AltitudeMeters = table.Column<double>(type: "REAL", nullable: true),
                    VelocityMps = table.Column<double>(type: "REAL", nullable: true),
                    HeadingDegrees = table.Column<double>(type: "REAL", nullable: true),
                    PitchDegrees = table.Column<double>(type: "REAL", nullable: true),
                    RollDegrees = table.Column<double>(type: "REAL", nullable: true),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    IsEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetrySamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetrySamples_DataSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TelemetrySamples_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trajectories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VehicleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", nullable: false),
                    License = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trajectories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trajectories_DataSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Trajectories_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trajectories_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrajectoryPoints",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TrajectoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OffsetSeconds = table.Column<double>(type: "REAL", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    AltitudeMeters = table.Column<double>(type: "REAL", nullable: false),
                    VelocityMps = table.Column<double>(type: "REAL", nullable: true),
                    HeadingDegrees = table.Column<double>(type: "REAL", nullable: true),
                    PitchDegrees = table.Column<double>(type: "REAL", nullable: true),
                    RollDegrees = table.Column<double>(type: "REAL", nullable: true),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrajectoryPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrajectoryPoints_Trajectories_TrajectoryId",
                        column: x => x.TrajectoryId,
                        principalTable: "Trajectories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataSources_Name",
                table: "DataSources",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MissionEvents_MissionId_OffsetSeconds",
                table: "MissionEvents",
                columns: new[] { "MissionId", "OffsetSeconds" });

            migrationBuilder.CreateIndex(
                name: "IX_MissionEvents_VehicleId",
                table: "MissionEvents",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_Missions_MissionCode",
                table: "Missions",
                column: "MissionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySamples_SourceId",
                table: "TelemetrySamples",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetrySamples_VehicleId_TimestampUtc",
                table: "TelemetrySamples",
                columns: new[] { "VehicleId", "TimestampUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trajectories_MissionId",
                table: "Trajectories",
                column: "MissionId");

            migrationBuilder.CreateIndex(
                name: "IX_Trajectories_SourceId",
                table: "Trajectories",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Trajectories_VehicleId_Type",
                table: "Trajectories",
                columns: new[] { "VehicleId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrajectoryPoints_TrajectoryId_OffsetSeconds",
                table: "TrajectoryPoints",
                columns: new[] { "TrajectoryId", "OffsetSeconds" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_MissionId_Type",
                table: "Vehicles",
                columns: new[] { "MissionId", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MissionEvents");

            migrationBuilder.DropTable(
                name: "TelemetrySamples");

            migrationBuilder.DropTable(
                name: "TrajectoryPoints");

            migrationBuilder.DropTable(
                name: "Trajectories");

            migrationBuilder.DropTable(
                name: "DataSources");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Missions");
        }
    }
}
