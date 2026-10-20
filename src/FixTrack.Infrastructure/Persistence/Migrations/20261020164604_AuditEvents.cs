using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Action = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    EntityType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    EntityId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_Summary_NotEmpty", "[Summary] <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_EntityType_EntityId_OccurredAtUtc",
                table: "AuditEvents",
                columns: new[] { "EntityType", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAtUtc",
                table: "AuditEvents",
                column: "OccurredAtUtc");

            // Los eventos de auditoría son de solo inserción. CREATE TRIGGER debe ser la primera instrucción de su lote;
            // EXEC lo permite también dentro del script idempotente.
            migrationBuilder.Sql("""
                EXEC(N'CREATE TRIGGER [dbo].[TR_AuditEvents_AppendOnly]
                ON [dbo].[AuditEvents]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (SELECT 1 FROM deleted)
                        THROW 50001, N''El registro de auditoría es de solo inserción: no puede modificarse ni eliminarse.'', 1;
                END');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
