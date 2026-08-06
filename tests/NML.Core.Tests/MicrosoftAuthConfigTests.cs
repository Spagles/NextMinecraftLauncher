using NML.Core.Auth.Microsoft;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the Microsoft auth constants are correct for the modern v2.0 device-code flow.
/// </summary>
public class MicrosoftAuthConfigTests
{
    [Fact]
    public void Scope_Uses_Modern_XboxLive_Signin()
    {
        // The modern v2.0 scope must be "XboxLive.signin offline_access" (not the legacy
        // "service::user.auth.xboxlive.com::MBI_SSL").
        MicrosoftAuthProvider.Scope.Should().Be("XboxLive.signin offline_access");
    }

    [Fact]
    public void Scope_Includes_Offline_Access_For_Refresh()
    {
        // Without offline_access, no refresh token is returned and silent re-login is impossible.
        MicrosoftAuthProvider.Scope.Should().Contain("offline_access");
    }

    [Fact]
    public void ClientId_Is_Not_Empty()
    {
        MicrosoftAuthProvider.ClientId.Should().NotBeNullOrEmpty();
    }
}
