using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FixTrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillCurrentAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Las órdenes asignadas antes del historial de asignaciones no tenían ningún registro: se agrega solo la
            // asignación vigente, con su fecha real y sin autor. Las reasignaciones anteriores no se guardaron y no se
            // inventan. Solo inserta; si ya hay registros de una orden, no la toca. ORDER BY fija el orden de los Id nuevos.
            migrationBuilder.Sql("""
                INSERT INTO [ServiceOrderAssignments] ([ServiceOrderId], [FromTechnicianId], [ToTechnicianId], [AssignedAtUtc], [AssignedByUserId], [AssignedByName], [Note])
                SELECT o.[Id], NULL, o.[TechnicianId], o.[TechnicianAssignedAtUtc], NULL, NULL, N'Asignación vigente al activar el historial de asignaciones; los cambios anteriores no se registraron.'
                FROM [ServiceOrders] AS o
                WHERE o.[TechnicianId] IS NOT NULL
                  AND o.[TechnicianAssignedAtUtc] IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM [ServiceOrderAssignments] AS a WHERE a.[ServiceOrderId] = o.[Id])
                ORDER BY o.[Id];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin cambios: las asignaciones son de solo inserción (un trigger impide borrarlas).
        }
    }
}
