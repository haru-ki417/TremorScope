using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TremorScope.Infrastructure.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Patients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PseudonymId = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MeasuredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Hand = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Pattern = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CloudSyncedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PacsSentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Recordings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Condition = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleRate = table.Column<double>(type: "REAL", nullable: false),
                    AxisCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MissingSamples = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Samples = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PeakFrequencyHz = table.Column<double>(type: "REAL", nullable: false),
                    RmsAccelerationMg = table.Column<double>(type: "REAL", nullable: false),
                    EstimatedDisplacementMm = table.Column<double>(type: "REAL", nullable: false),
                    Regularity = table.Column<double>(type: "REAL", nullable: false),
                    BandsJson = table.Column<string>(type: "TEXT", nullable: false),
                    QualityLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    QualityMessagesJson = table.Column<string>(type: "TEXT", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recordings_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Patients_LocalId",
                table: "Patients",
                column: "LocalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_PseudonymId",
                table: "Patients",
                column: "PseudonymId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recordings_SessionId",
                table: "Recordings",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PatientId_MeasuredAtUtc",
                table: "Sessions",
                columns: new[] { "PatientId", "MeasuredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Recordings");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Patients");
        }
    }
}
