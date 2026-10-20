using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false, collation: "Modern_Spanish_100_CI_AI"),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.CheckConstraint("CK_Customers_Deactivation", "([IsActive] = 1 AND [DeactivatedAtUtc] IS NULL) OR ([IsActive] = 0 AND [DeactivatedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Customers_Email_Format", "[Email] IS NULL OR [Email] LIKE '%_@_%._%'");
                    table.CheckConstraint("CK_Customers_FullName_NotEmpty", "[FullName] <> ''");
                    table.CheckConstraint("CK_Customers_Phone_NotEmpty", "[Phone] <> ''");
                });

            migrationBuilder.CreateTable(
                name: "Technicians",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false, collation: "Modern_Spanish_100_CI_AI"),
                    Phone = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    Specialty = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Technicians", x => x.Id);
                    table.CheckConstraint("CK_Technicians_Deactivation", "([IsActive] = 1 AND [DeactivatedAtUtc] IS NULL) OR ([IsActive] = 0 AND [DeactivatedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_Technicians_Email_Format", "[Email] IS NULL OR [Email] LIKE '%_@_%._%'");
                    table.CheckConstraint("CK_Technicians_FullName_NotEmpty", "[FullName] <> ''");
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.CheckConstraint("CK_Devices_Brand_NotEmpty", "[Brand] <> ''");
                    table.CheckConstraint("CK_Devices_Type", "[Type] BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_Devices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    TechnicianId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    ReportedIssue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Diagnosis = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    FinalCost = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    PromisedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrders", x => x.Id);
                    table.CheckConstraint("CK_ServiceOrders_Delivery", "([Status] = 6 AND [DeliveredAtUtc] IS NOT NULL) OR ([Status] <> 6 AND [DeliveredAtUtc] IS NULL)");
                    table.CheckConstraint("CK_ServiceOrders_EstimatedCost", "[EstimatedCost] IS NULL OR [EstimatedCost] >= 0");
                    table.CheckConstraint("CK_ServiceOrders_FinalCost", "[FinalCost] IS NULL OR [FinalCost] >= 0");
                    table.CheckConstraint("CK_ServiceOrders_Priority", "[Priority] BETWEEN 1 AND 4");
                    table.CheckConstraint("CK_ServiceOrders_PromisedAt", "[PromisedAtUtc] IS NULL OR [PromisedAtUtc] >= [ReceivedAtUtc]");
                    table.CheckConstraint("CK_ServiceOrders_ReportedIssue_NotEmpty", "[ReportedIssue] <> ''");
                    table.CheckConstraint("CK_ServiceOrders_Status", "[Status] BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_ServiceOrders_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceOrders_Technicians_TechnicianId",
                        column: x => x.TechnicianId,
                        principalTable: "Technicians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceOrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServiceOrderId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrderStatusHistory", x => x.Id);
                    table.CheckConstraint("CK_ServiceOrderStatusHistory_FromStatus", "[FromStatus] IS NULL OR ([FromStatus] BETWEEN 1 AND 7 AND [FromStatus] <> [ToStatus])");
                    table.CheckConstraint("CK_ServiceOrderStatusHistory_ToStatus", "[ToStatus] BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_ServiceOrderStatusHistory_ServiceOrders_ServiceOrderId",
                        column: x => x.ServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_IsActive_FullName",
                table: "Customers",
                columns: new[] { "IsActive", "FullName" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Phone",
                table: "Customers",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "UX_Customers_Email",
                table: "Customers",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_CustomerId",
                table: "Devices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "UX_Devices_Brand_SerialNumber",
                table: "Devices",
                columns: new[] { "Brand", "SerialNumber" },
                unique: true,
                filter: "[SerialNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_DeviceId",
                table: "ServiceOrders",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_Status_PromisedAtUtc",
                table: "ServiceOrders",
                columns: new[] { "Status", "PromisedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_TechnicianId",
                table: "ServiceOrders",
                column: "TechnicianId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderStatusHistory_ServiceOrderId_ChangedAtUtc",
                table: "ServiceOrderStatusHistory",
                columns: new[] { "ServiceOrderId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Technicians_IsActive_FullName",
                table: "Technicians",
                columns: new[] { "IsActive", "FullName" });

            migrationBuilder.CreateIndex(
                name: "UX_Technicians_Email",
                table: "Technicians",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            // El historial de estados es de solo inserción: el trigger rechaza cualquier UPDATE o DELETE.
            // CREATE TRIGGER debe ser la primera instrucción de su lote; EXEC lo permite también dentro
            // de los bloques IF que genera el script idempotente (dotnet ef migrations script --idempotent).
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_ServiceOrderStatusHistory_AppendOnly]
                ON [dbo].[ServiceOrderStatusHistory]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 50001, N''El historial de estados de una orden es de solo inserción: no puede modificarse ni eliminarse.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceOrderStatusHistory");

            migrationBuilder.DropTable(
                name: "ServiceOrders");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Technicians");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
