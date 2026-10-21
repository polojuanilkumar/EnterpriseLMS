using Xunit;
namespace LMS.Tests.Integration;
public sealed class SqlTargetSafetyTests
{
    [Fact]
    public void Only_owned_loopback_unique_database_is_allowed()
    {
        var id = Guid.NewGuid().ToString("N"); var database = "LMS_Integration_" + id;
        DisposableSqlServer.ValidateTarget("127.0.0.1", 15433, database, database, id, id, "127.0.0.1");
        Assert.Throws<InvalidOperationException>(() => DisposableSqlServer.ValidateTarget("localhost", 15433, database, database, id, id, "127.0.0.1"));
        Assert.Throws<InvalidOperationException>(() => DisposableSqlServer.ValidateTarget("127.0.0.1", 15433, "EnterpriseLMS", database, id, id, "127.0.0.1"));
        Assert.Throws<InvalidOperationException>(() => DisposableSqlServer.ValidateTarget("127.0.0.1", 15433, database, database, "foreign", id, "127.0.0.1"));
        Assert.Throws<InvalidOperationException>(() => DisposableSqlServer.ValidateTarget("127.0.0.1", 15433, database, database, id, id, "0.0.0.0"));
        Assert.Throws<InvalidOperationException>(() => DisposableSqlServer.ValidateTarget("127.0.0.1", 1433, database, "other", id, id, "127.0.0.1"));
    }
}
