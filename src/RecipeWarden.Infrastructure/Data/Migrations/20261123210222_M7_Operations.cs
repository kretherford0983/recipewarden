using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M7_Operations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DetailsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ScheduleId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<string>(type: "TEXT", nullable: true),
                    DestinationFolder = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Encrypted = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeyFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    StartedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DeletedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Cron = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DestinationFolder = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    KeepLast = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepDaily = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepWeekly = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepMonthly = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<string>(type: "TEXT", nullable: true),
                    NextRunAt = table.Column<string>(type: "TEXT", nullable: true),
                    RetryAttempt = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRetryAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HealthCheckResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CheckName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RanAt = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthCheckResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StorageSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TakenAt = table.Column<string>(type: "TEXT", nullable: false),
                    DbBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    WalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FreePages = table.Column<long>(type: "INTEGER", nullable: false),
                    MediaBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ImportBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LogBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    DataVolumeTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    DataVolumeFree = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupVolumeTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    BackupVolumeFree = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_At",
                table: "AuditEntries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_ScheduleId_Status",
                table: "BackupRecords",
                columns: new[] { "ScheduleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_StartedAt",
                table: "BackupRecords",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_HealthCheckResults_CheckName_RanAt",
                table: "HealthCheckResults",
                columns: new[] { "CheckName", "RanAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageSnapshots_TakenAt",
                table: "StorageSnapshots",
                column: "TakenAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "BackupRecords");

            migrationBuilder.DropTable(
                name: "BackupSchedules");

            migrationBuilder.DropTable(
                name: "HealthCheckResults");

            migrationBuilder.DropTable(
                name: "StorageSnapshots");
        }
    }
}
