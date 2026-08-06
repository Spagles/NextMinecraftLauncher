using NML.Core.Auth.Microsoft;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the Microsoft auth constants and code path (without real credentials).
/// These tests prove the flow is correctly configured for the legacy login.live.com API.
/// </summary>
public class MsAuthFlowCodePathTests
{
    [Fact]
    public void ClientId_Is_Legacy_Minecraft_Id()
    {
        MicrosoftAuthProvider.ClientId.Should().Be("00000000402b5328");
    }

    [Fact]
    public void Scope_Is_Legacy_MBI_SSL()
    {
        MicrosoftAuthProvider.Scope.Should().Be("service::user.auth.xboxlive.com::MBI_SSL");
    }

    [Fact]
    public void AuthorizeUrl_Is_LoginLiveCom()
    {
        MicrosoftAuthProvider.AuthorizeUrl.Should().StartWith("https://login.live.com/");
        MicrosoftAuthProvider.AuthorizeUrl.Should().Contain("oauth20_authorize");
    }

    [Fact]
    public void RedirectUri_Is_Desktop_Srf()
    {
        MicrosoftAuthProvider.RedirectUri.Should().Contain("oauth20_desktop.srf");
    }

    [Fact]
    public void TokenExchangeUrl_Is_LoginLiveCom_Token_Endpoint()
    {
        MicrosoftAuthProvider.TokenExchangeUrl.Should().StartWith("https://login.live.com/");
        MicrosoftAuthProvider.TokenExchangeUrl.Should().Contain("oauth20_token");
    }
}
