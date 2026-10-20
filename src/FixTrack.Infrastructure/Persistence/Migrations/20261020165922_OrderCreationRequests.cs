using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderCreationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreationRequestId",
                table: "ServiceOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_ServiceOrders_CreationRequestId",
                table: "ServiceOrders",
                column: "CreationRequestId",
                unique: true,
                filter: "[CreationRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ServiceOrders_CreationRequestId",
                table: "ServiceOrders");

            migrationBuilder.DropColumn(
                name: "CreationRequestId",
                table: "ServiceOrders");
        }
    }
}
