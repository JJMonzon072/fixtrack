namespace FixTrack.Tests.SqlServer;

/// <summary>La base desechable solo acepta servidores locales: crea y elimina bases.</summary>
public sealed class DisposableSqlServerDatabaseTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1,14333")]
    [InlineData("tcp:127.0.0.1,1433")]
    [InlineData("(local)")]
    [InlineData(".\\SQLEXPRESS")]
    [InlineData("[::1],1433")]
    public void Local_servers_are_accepted(string dataSource) =>
        DisposableSqlServerDatabase.EnsureLocalServer(dataSource);

    [Theory]
    [InlineData("tcp:FixTrackDB.mssql.somee.com,1433")]
    [InlineData("vb4300.mgmt.somee.com")]
    [InlineData("10.0.0.5")]
    [InlineData("servidor.example.com\\SQLEXPRESS")]
    public void Remote_servers_are_rejected(string dataSource)
    {
        var error = Assert.Throws<InvalidOperationException>(() => DisposableSqlServerDatabase.EnsureLocalServer(dataSource));

        Assert.Contains("servidor local", error.Message);
    }
}
