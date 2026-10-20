using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Technicians",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Devices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Technicians");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Customers");
        }
    }
}
