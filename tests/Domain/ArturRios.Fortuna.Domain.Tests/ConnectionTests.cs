using ArturRios.Fortuna.Domain.Currencies;
using ArturRios.Fortuna.Domain.Ingestion;
using ArturRios.Fortuna.Domain.Transactions;
using ArturRios.Fortuna.Domain.Users;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Domain.Tests;

public sealed class ConnectionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 7, 0, 0, TimeSpan.Zero);

    [UnitFact]
    public void GivenValidPluggyDetails_WhenConnectionCreated_ThenOnlyProtectedTokenIsHeld()
    {
        var cipher = new byte[] { 1, 2, 3 };

        var connection = new Connection(
            User(), TransactionSourceType.Pluggy, "  item-id  ", cipher, Now);
        cipher[0] = 9;

        Assert.NotEqual(Guid.Empty, connection.PublicId);
        Assert.Equal("item-id", connection.ExternalReference);
        Assert.Equal(new byte[] { 1, 2, 3 }, connection.AccessTokenCipher);
        Assert.Equal(ConnectionStatus.Active, connection.Status);
        Assert.Equal(Now, connection.CreatedAt);
        Assert.Equal(Now, connection.UpdatedAt);
    }

    [UnitTheory]
    [InlineData(TransactionSourceType.Manual)]
    [InlineData((TransactionSourceType)99)]
    public void GivenUnsupportedSource_WhenConnectionCreated_ThenItIsRejected(
        TransactionSourceType source)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Connection(
            User(), source, "item-id", [1], Now));
    }

    [UnitFact]
    public void GivenInvalidDetails_WhenConnectionCreated_ThenTheyAreRejected()
    {
        var user = User();

        Assert.Throws<ArgumentException>(() => new Connection(
            user, TransactionSourceType.Pluggy, " ", [1], Now));
        Assert.Throws<ArgumentException>(() => new Connection(
            user, TransactionSourceType.Pluggy, "item", [], Now));
    }

    [UnitFact]
    public void GivenReauthenticationRequired_WhenNewSessionProvided_ThenConnectionIsActive()
    {
        var connection = new Connection(
            User(), TransactionSourceType.Pluggy, "old-item", [1, 2], Now);
        connection.MarkRequiresReauthentication(Now.AddMinutes(1));
        var cipher = new byte[] { 3, 4 };

        connection.Reauthenticate("  new-item  ", cipher, Now.AddMinutes(2));
        cipher[0] = 9;

        Assert.Equal("new-item", connection.ExternalReference);
        Assert.Equal(new byte[] { 3, 4 }, connection.AccessTokenCipher);
        Assert.Equal(ConnectionStatus.Active, connection.Status);
        Assert.Equal(Now.AddMinutes(2), connection.UpdatedAt);
    }

    [UnitFact]
    public void GivenActiveConnection_WhenReauthenticated_ThenTransitionIsRejected()
    {
        var connection = new Connection(
            User(), TransactionSourceType.Pluggy, "item", [1], Now);

        Assert.Throws<InvalidOperationException>(() => connection.Reauthenticate(
            "new-item", [2], Now.AddMinutes(1)));
    }

    private static UserProfile User() => new(
        Guid.NewGuid(), "Owner", new Currency("BRL", "Brazilian real", 2), Now);
}
