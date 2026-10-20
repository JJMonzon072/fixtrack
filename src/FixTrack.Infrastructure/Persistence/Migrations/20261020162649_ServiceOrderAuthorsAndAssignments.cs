using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceOrderAuthorsAndAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangedByName",
                table: "ServiceOrderStatusHistory",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangedByUserId",
                table: "ServiceOrderStatusHistory",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceOrderAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServiceOrderId = table.Column<int>(type: "int", nullable: false),
                    FromTechnicianId = table.Column<int>(type: "int", nullable: true),
                    ToTechnicianId = table.Column<int>(type: "int", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    AssignedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AssignedByName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceOrderAssignments", x => x.Id);
                    table.CheckConstraint("CK_ServiceOrderAssignments_DifferentTechnician", "[FromTechnicianId] IS NULL OR [FromTechnicianId] <> [ToTechnicianId]");
                    table.ForeignKey(
                        name: "FK_ServiceOrderAssignments_ServiceOrders_ServiceOrderId",
                        column: x => x.ServiceOrderId,
                        principalTable: "ServiceOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceOrderAssignments_Technicians_FromTechnicianId",
                        column: x => x.FromTechnicianId,
                        principalTable: "Technicians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceOrderAssignments_Technicians_ToTechnicianId",
                        column: x => x.ToTechnicianId,
                        principalTable: "Technicians",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderAssignments_FromTechnicianId",
                table: "ServiceOrderAssignments",
                column: "FromTechnicianId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderAssignments_ServiceOrderId_AssignedAtUtc",
                table: "ServiceOrderAssignments",
                columns: new[] { "ServiceOrderId", "AssignedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrderAssignments_ToTechnicianId",
                table: "ServiceOrderAssignments",
                column: "ToTechnicianId");

            // Las asignaciones son de solo inserción, como el historial de estados. CREATE TRIGGER debe ser la
            // primera instrucción de su lote; EXEC lo permite también dentro del script idempotente.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_ServiceOrderAssignments_AppendOnly]
                ON [dbo].[ServiceOrderAssignments]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 50001, N''El registro de asignaciones de una orden es de solo inserción: no puede modificarse ni eliminarse.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceOrderAssignments");

            migrationBuilder.DropColumn(
                name: "ChangedByName",
                table: "ServiceOrderStatusHistory");

            migrationBuilder.DropColumn(
                name: "ChangedByUserId",
                table: "ServiceOrderStatusHistory");
        }
    }
}
