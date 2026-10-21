using System.Data;
using Microsoft.Data.SqlClient;

namespace FixTrack.Demo;

/// <summary>
/// Prepara el servidor SQL Server local de la demostración: crea la base y un login propio para FixTrack, dueño solo de
/// esa base. La aplicación, las migraciones y la carga de datos usan ese login; <c>sa</c> solo se usa aquí.
/// </summary>
internal static class DemoDatabase
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(3);

    public static async Task PrepareAsync(DemoSettings settings, CancellationToken cancellationToken)
    {
        var appPassword = DemoSettings.Required(settings.AppPassword, nameof(DemoSettings.AppPassword));
        await using var master = await OpenAsync(settings.ServerAdminConnectionString(), cancellationToken);

        await ExecuteAsync(
            master,
            """
            DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@database);
            IF DB_ID(@database) IS NULL
                EXEC (@sql);
            """,
            cancellationToken,
            ("@database", settings.Database));

        // CREATE LOGIN no admite parámetros: el nombre y la contraseña van escapados con QUOTENAME. Si el login ya existe
        // (el entorno se reinició con los mismos datos), se le vuelve a fijar la contraseña de la configuración.
        await ExecuteAsync(
            master,
            """
            DECLARE @sql nvarchar(max) = CASE WHEN SUSER_ID(@login) IS NULL
                THEN N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''')
                    + N', CHECK_POLICY = ON, DEFAULT_DATABASE = ' + QUOTENAME(@database)
                ELSE N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') END;
            EXEC (@sql);
            """,
            cancellationToken,
            ("@login", settings.AppLogin),
            ("@password", appPassword),
            ("@database", settings.Database));

        master.ChangeDatabase(settings.Database);
        await ExecuteAsync(
            master,
            """
            DECLARE @createUser nvarchar(max) = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
            DECLARE @addOwner nvarchar(max) = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@login);
            IF USER_ID(@login) IS NULL
                EXEC (@createUser);
            IF IS_ROLEMEMBER(N'db_owner', @login) = 0
                EXEC (@addOwner);
            """,
            cancellationToken,
            ("@login", settings.AppLogin));
    }

    /// <summary>
    /// Abre la conexión y espera a que SQL Server acepte inicios de sesión: al crear el contenedor, el servidor tarda
    /// en recuperar sus bases del sistema (más aún emulado en un Mac con Apple Silicon).
    /// </summary>
    public static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (SqlException error) when (DateTime.UtcNow - started < WaitLimit)
            {
                await connection.DisposeAsync();
                Console.WriteLine($"SQL Server todavía no acepta conexiones (error {error.Number}); se reintenta en 3 s.");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, 128) { Value = value });
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
