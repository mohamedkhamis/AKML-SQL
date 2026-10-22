using AkmlSql.Engine.Pairing;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 043 follow-up (review): the origin of a bridge request is the web page, not the socket. The transport starts each
/// connection as its socket; an accepted handshake names the page (<c>HandshakeRequest.ClientInstanceId</c>), and the name must
/// reach the transport's read loop although the handshake is answered in a child async context.
/// </summary>
public class BridgeConnectionTests
{
    private const string Page = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Outside_a_connection_there_is_no_origin_and_adopting_does_nothing()
    {
        Assert.Null(BridgeConnection.Current);

        BridgeConnection.Adopt(Page);

        Assert.Null(BridgeConnection.Current);
    }

    [Fact]
    public void A_connection_is_its_socket_until_it_names_its_page()
    {
        using (BridgeConnection.Set("sock-1"))
        {
            Assert.Equal("socket:sock-1", BridgeConnection.Current);
            BridgeConnection.Adopt(Page);
            Assert.Equal("page:" + Page, BridgeConnection.Current);
        }
        Assert.Null(BridgeConnection.Current);
    }

    [Fact]
    public async Task A_page_named_in_a_child_async_context_reaches_the_read_loop_and_later_requests()
    {
        using (BridgeConnection.Set("sock-1"))
        {
            // The handshake handler runs inside the router's async method: an AsyncLocal assigned there would not flow back.
            await Task.Run(async () =>
            {
                await Task.Yield();
                BridgeConnection.Adopt(Page);
            });

            Assert.Equal("page:" + Page, BridgeConnection.Current);
            Assert.Equal("page:" + Page, await Task.Run(() => BridgeConnection.Current));   // a request answered on the pool
        }
    }

    [Fact]
    public void A_socket_keeps_the_first_page_it_adopted()
    {
        using (BridgeConnection.Set("sock-1"))
        {
            BridgeConnection.Adopt(Page);
            BridgeConnection.Adopt("another-page-0123456789");

            Assert.Equal("page:" + Page, BridgeConnection.Current);
        }
    }

    [Fact]
    public void Each_connection_has_its_own_origin()
    {
        using (BridgeConnection.Set("sock-1"))
        {
            BridgeConnection.Adopt(Page);
            using (BridgeConnection.Set("sock-2"))
                Assert.Equal("socket:sock-2", BridgeConnection.Current);
            Assert.Equal("page:" + Page, BridgeConnection.Current);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789abcde")]                                                       // 15 characters
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]     // 65 characters
    [InlineData("0123456789abcdef:0123")]
    [InlineData("0123456789abcdef 0123")]
    [InlineData("0123456789abcdef/0123")]
    public void An_id_that_does_not_look_like_one_is_ignored(string? id)
    {
        using (BridgeConnection.Set("sock-1"))
        {
            BridgeConnection.Adopt(id);
            Assert.Equal("socket:sock-1", BridgeConnection.Current);
        }
    }

    [Theory]
    [InlineData("0123456789abcdef")]
    [InlineData("3f2b8c1e-7d4a-4c5b-9e6f-0a1b2c3d4e5f")]
    [InlineData("AbCdEf_-0123456789AbCdEf_-0123456789AbCdEf_-0123456789AbCdEf_-")]
    public void A_guid_or_url_safe_token_is_adopted(string id)
    {
        using (BridgeConnection.Set("sock-1"))
        {
            BridgeConnection.Adopt(id);
            Assert.Equal("page:" + id, BridgeConnection.Current);
        }
    }
}
