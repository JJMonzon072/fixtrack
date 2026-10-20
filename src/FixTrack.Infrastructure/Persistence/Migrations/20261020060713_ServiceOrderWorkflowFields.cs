using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceOrderWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IntakeNotes",
                table: "ServiceOrders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TechnicianAssignedAtUtc",
                table: "ServiceOrders",
                type: "datetime2(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ServiceOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceOrders_ReceivedAtUtc",
                table: "ServiceOrders",
                column: "ReceivedAtUtc");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceOrders_FinalCost_OnDelivery",
                table: "ServiceOrders",
                sql: "([Status] = 6 AND [FinalCost] IS NOT NULL) OR ([Status] <> 6 AND [FinalCost] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceOrders_TechnicianAssignment",
                table: "ServiceOrders",
                sql: "([TechnicianId] IS NULL AND [TechnicianAssignedAtUtc] IS NULL) OR ([TechnicianId] IS NOT NULL AND [TechnicianAssignedAtUtc] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceOrders_ReceivedAtUtc",
                table: "ServiceOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceOrders_FinalCost_OnDelivery",
                table: "ServiceOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceOrders_TechnicianAssignment",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "IntakeNotes",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "TechnicianAssignedAtUtc",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ServiceOrders");
        }
    }
}
