using Noot.Analytics;

[TestClass]
public class AnonymousIdTests
{
    [TestMethod]
    public void Hash_Deterministic()
    {
        Assert.AreEqual( AnonymousId.Hash( 76561198000000000 ), AnonymousId.Hash( 76561198000000000 ) );
    }

    [TestMethod]
    public void Hash_NotRawSteamId()
    {
        var raw = "76561198000000000";
        Assert.AreNotEqual( raw, AnonymousId.Hash( 76561198000000000 ) );
    }

    [TestMethod]
    public void Hash_Zero_ReturnsEmpty()
    {
        Assert.AreEqual( "", AnonymousId.Hash( 0 ) );
    }

    [TestMethod]
    public void Hash_DifferentInputs_DifferentOutputs()
    {
        Assert.AreNotEqual( AnonymousId.Hash( 1 ), AnonymousId.Hash( 2 ) );
    }
}
