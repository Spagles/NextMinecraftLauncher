using NML.Core.Multiplayer;

namespace NML.Core.Tests;

/// <summary>
/// Verifies <see cref="ServerQrCodeUri"/> — the QR-code payload builder for server sharing. The URI
/// must be round-trippable, omit the default port (shorter QR), and URL-encode special characters.
/// </summary>
public class ServerQrCodeUriTests
{
    [Fact]
    public void Build_Standard_Server_With_Default_Port_Omits_Port()
    {
        string uri = ServerQrCodeUri.Build("Hypixel", "mc.hypixel.net", 25565);
        uri.Should().Be("mc://connect?host=mc.hypixel.net&name=Hypixel");
    }

    [Fact]
    public void Build_Non_Default_Port_Is_Included()
    {
        string uri = ServerQrCodeUri.Build("Survival", "play.example.net", 25570);
        uri.Should().Contain("port=25570");
    }

    [Fact]
    public void Build_URL_Encodes_Host_With_Special_Chars()
    {
        // An IPv6 host or a host with special chars must be URL-encoded.
        string uri = ServerQrCodeUri.Build("Test", "::1", 25565);
        uri.Should().Contain("host=%3A%3A1"); // "::1" URL-encoded
    }

    [Fact]
    public void Build_Throws_When_Host_Empty()
    {
        Action act = () => ServerQrCodeUri.Build("Test", "", 25565);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parse_RoundTrips_Build()
    {
        string uri = ServerQrCodeUri.Build("My Server", "play.example.net", 25599);
        var parsed = ServerQrCodeUri.Parse(uri);
        parsed.Should().NotBeNull();
        parsed!.Value.Name.Should().Be("My Server");
        parsed.Value.Host.Should().Be("play.example.net");
        parsed.Value.Port.Should().Be(25599);
    }

    [Fact]
    public void Parse_Default_Port_When_Omitted()
    {
        // When the QR omits port (default 25565), Parse fills it back in.
        var parsed = ServerQrCodeUri.Parse("mc://connect?host=example.net&name=Test");
        parsed!.Value.Port.Should().Be(25565);
    }

    [Fact]
    public void Parse_Falls_Back_To_Host_As_Name_When_Name_Omitted()
    {
        var parsed = ServerQrCodeUri.Parse("mc://connect?host=example.net");
        parsed!.Value.Name.Should().Be("example.net");
    }

    [Fact]
    public void Parse_Returns_Null_For_Invalid_URI()
    {
        ServerQrCodeUri.Parse("").Should().BeNull();
        ServerQrCodeUri.Parse("https://example.net").Should().BeNull();
        ServerQrCodeUri.Parse("mc://connect?").Should().BeNull(); // no host
    }
}
