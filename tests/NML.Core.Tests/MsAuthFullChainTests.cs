using NML.Core.Auth;
using NML.Core.Auth.Microsoft;
using Microsoft.Extensions.Logging.Abstractions;

namespace NML.Core.Tests;

/// <summary>
/// Tests the FULL Microsoft auth chain (MSA → XBL → XSTS → MC → profile) using
/// a mock IMicrosoftExchange, proving the code path is correct without needing
/// real credentials. This is the closest we can get to E2E without a real account.
/// </summary>
public class MsAuthFullChainTests
{
    private class MockExchange : IMicrosoftExchange
    {
        public Task<DeviceCodeResponse> RequestDeviceCodeAsync(string clientId, string scope, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<MsaTokenResponse> PollForMsaTokenAsync(string clientId, string deviceCode, string scope, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<MsaTokenResponse> RefreshMsaTokenAsync(string clientId, string refreshToken, string scope, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<MsaTokenResponse> ExchangeAuthCodeForMsaTokenAsync(string clientId, string authCode, string redirectUri, string scope, CancellationToken ct = default)
        {
            // Simulate a successful MSA token response
            return Task.FromResult(new MsaTokenResponse
            {
                AccessToken = "mock-msa-token",
                RefreshToken = "mock-refresh-token",
                ExpiresIn = 3600,
            });
        }

        public Task<XblTokenResponse> ExchangeMsaForXblAsync(string msaAccessToken, CancellationToken ct = default)
        {
            return Task.FromResult(new XblTokenResponse
            {
                Token = "mock-xbl-token",
                DisplayClaims = new XblDisplayClaims
                {
                    Xui = new List<XblUserClaim> { new() { UserHash = "mock-uhs" } }
                }
            });
        }

        public Task<XstsTokenResponse> ExchangeXblForXstsAsync(string xblToken, CancellationToken ct = default)
        {
            return Task.FromResult(new XstsTokenResponse
            {
                Token = "mock-xsts-token",
                DisplayClaims = new XblDisplayClaims
                {
                    Xui = new List<XblUserClaim> { new() { UserHash = "mock-uhs" } }
                }
            });
        }

        public Task<MinecraftTokenResponse> ExchangeXstsForMinecraftAsync(string xstsToken, string userHash, CancellationToken ct = default)
        {
            return Task.FromResult(new MinecraftTokenResponse
            {
                AccessToken = "mock-mc-token",
                ExpiresIn = 86400,
            });
        }

        public Task<MinecraftProfile> GetMinecraftProfileAsync(string mcAccessToken, CancellationToken ct = default)
        {
            return Task.FromResult(new MinecraftProfile
            {
                Id = "mock-uuid-1234",
                Name = "TestPlayer",
            });
        }
    }

    [Fact]
    public async Task FullAuthChain_MsaCode_To_Account()
    {
        // This test proves the COMPLETE code path from auth code to Account:
        // ExchangeAuthCodeForMsaToken → ExchangeMsaForXbl → ExchangeXblForXsts
        // → ExchangeXstsForMinecraft → GetMinecraftProfile → Account
        var mockExchange = new MockExchange();
        var provider = new MicrosoftAuthProvider(mockExchange, NullLogger<MicrosoftAuthProvider>.Instance);

        // Call with a fake auth code
        var account = await provider.CompleteLoginWithCodeAsync("fake-auth-code");

        // Verify the full chain produced a valid Account
        account.Should().NotBeNull();
        account.Username.Should().Be("TestPlayer");
        account.Uuid.Should().Be("mock-uuid-1234");
        account.AccessToken.Should().Be("mock-mc-token");
        account.AccountType.Should().Be("msa");
        account.Xuid.Should().Be("mock-uhs");
        account.RefreshToken.Should().Be("mock-refresh-token");
    }

    [Fact]
    public void GetAuthorizeUrl_Produces_Valid_Url()
    {
        var mockExchange = new MockExchange();
        var provider = new MicrosoftAuthProvider(mockExchange, NullLogger<MicrosoftAuthProvider>.Instance);
        
        string url = provider.GetAuthorizeUrl();
        
        url.Should().StartWith("https://login.live.com/oauth20_authorize.srf");
        url.Should().Contain("client_id=00000000402b5328");
        url.Should().Contain("response_type=code");
        url.Should().Contain("redirect_uri=");
        url.Should().Contain("oauth20_desktop.srf");
        url.Should().Contain("scope=");
        url.Should().Contain("xboxlive.com");
    }

    [Fact]
    public async Task FullAuthChain_Stores_Refresh_Token()
    {
        var mockExchange = new MockExchange();
        var provider = new MicrosoftAuthProvider(mockExchange, NullLogger<MicrosoftAuthProvider>.Instance);
        
        var account = await provider.CompleteLoginWithCodeAsync("fake-code");
        
        // The refresh token MUST be stored for silent re-login later
        account.RefreshToken.Should().NotBeNullOrEmpty();
        account.RefreshToken.Should().Be("mock-refresh-token");
    }
}
