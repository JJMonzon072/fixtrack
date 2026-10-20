using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TechnicianSpecialtyAndDeviceSerialRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La especialidad pasa a ser obligatoria. Sin valor por defecto a propósito: si existieran técnicos
            // sin especialidad, la migración falla en lugar de completarlos con un texto vacío o inventado.
            migrationBuilder.AlterColumn<string>(
                name: "Specialty",
                table: "Technicians",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Technicians_Specialty_NotEmpty",
                table: "Technicians",
                sql: "[Specialty] <> ''");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Devices_SerialNumber_NotEmpty",
                table: "Devices",
                sql: "[SerialNumber] IS NULL OR [SerialNumber] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Technicians_Specialty_NotEmpty",
                table: "Technicians");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Devices_SerialNumber_NotEmpty",
                table: "Devices");

            migrationBuilder.AlterColumn<string>(
                name: "Specialty",
                table: "Technicians",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);
        }
    }
}
