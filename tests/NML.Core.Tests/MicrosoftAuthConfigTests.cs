using NML.Core.Auth.Microsoft;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the Microsoft auth constants are correct for the modern v2.0 device-code flow.
/// </summary>
public class MicrosoftAuthConfigTests
{
    [Fact]
    public void Scope_Uses_Legacy_MBI_SSL()
    {
        // The legacy client_id 00000000402b5328 requires the legacy scope.
        MicrosoftAuthProvider.Scope.Should().Be("service::user.auth.xboxlive.com::MBI_SSL");
    }

    [Fact]
    public void Scope_Supports_XboxLive_Auth()
    {
        // The scope must grant access to Xbox Live for the MSA → XBL exchange.
        MicrosoftAuthProvider.Scope.Should().Contain("xboxlive.com");
    }

    [Fact]
    public void ClientId_Is_Not_Empty()
    {
        MicrosoftAuthProvider.ClientId.Should().NotBeNullOrEmpty();
    }
}
