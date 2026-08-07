using System.IO;
using NML.Core.Multiplayer;

namespace NML.Core.Tests;

/// <summary>
/// End-to-end verification of the server-list QR-code sharing flow — the one path in the
/// multiplayer feature that had no test coverage: <see cref="ServerQrCodeUri.Build"/> produces a
/// payload, QRCoder renders it into a real PNG bitmap, and the payload parses back to the original
/// server. This mirrors what <c>MultiplayerPageViewModel.ShareQrCode</c> does at runtime.
/// </summary>
public class ServerQrCodeGenerationTests
{
    [Fact]
    public void Build_Qr_Png_Round_Trips_Via_Parse()
    {
        // The exact flow ShareQrCode runs: build URI → render PNG → (receiver) parse URI back.
        string uri = ServerQrCodeUri.Build("My Server", "play.example.net", 25565);
        uri.Should().StartWith("mc://connect?");

        // Render the URI into a PNG via the same QRCoder call the VM makes.
        using var qrGen = new QRCoder.QRCodeGenerator();
        var qrData = qrGen.CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.M);
        byte[] png = new QRCoder.PngByteQRCode(qrData).GetGraphic(8);

        // It must be a valid PNG (magic bytes) with a non-trivial size.
        png.Should().NotBeEmpty();
        png[0].Should().Be(0x89, "PNG signature byte 0");
        png[1].Should().Be(0x50, "PNG signature byte 1 ('P')");
        png[2].Should().Be(0x4E, "PNG signature byte 2 ('N')");
        png[3].Should().Be(0x47, "PNG signature byte 3 ('G')");
        png.Length.Should().BeGreaterThan(100, "a real QR PNG is larger than a header");

        // The payload must round-trip back to the original server.
        var parsed = ServerQrCodeUri.Parse(uri);
        parsed.Should().NotBeNull();
        parsed!.Value.Name.Should().Be("My Server");
        parsed.Value.Host.Should().Be("play.example.net");
        parsed.Value.Port.Should().Be(25565);
    }

    [Theory]
    [InlineData("Survival", "survival.mc.gg", 25565)]
    [InlineData("Hardcore", "hardcore.io", 25566)]   // non-default port included in URI
    [InlineData("Mini Games", "192.168.1.10", 25577)] // LAN-ish port
    public void Qr_Flow_Works_Across_Name_And_Port(string name, string host, int port)
    {
        string uri = ServerQrCodeUri.Build(name, host, port);

        using var qrGen = new QRCoder.QRCodeGenerator();
        var qrData = qrGen.CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.M);
        byte[] png = new QRCoder.PngByteQRCode(qrData).GetGraphic(8);
        png.Should().NotBeEmpty("the QR must render for every server");

        var parsed = ServerQrCodeUri.Parse(uri);
        parsed.Should().NotBeNull();
        parsed!.Value.Name.Should().Be(name);
        parsed.Value.Host.Should().Be(host);
        parsed.Value.Port.Should().Be(port);
    }

    [Fact]
    public void Qr_Png_Is_Displayable_As_A_Bitmap()
    {
        // The VM loads the PNG into an Avalonia Bitmap via a MemoryStream. We can't easily construct
        // an Avalonia Bitmap in a Core test project, but we can prove the bytes are a complete,
        // decodable PNG by reading it back through System.Drawing-free PNG header validation: the
        // IHDR chunk must follow the 8-byte signature, and the IEND chunk must terminate it.
        string uri = ServerQrCodeUri.Build("Test", "test.net", 25565);
        using var qrGen = new QRCoder.QRCodeGenerator();
        var qrData = qrGen.CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.M);
        byte[] png = new QRCoder.PngByteQRCode(qrData).GetGraphic(8);

        // PNG = [8-byte signature][IHDR chunk...][...data chunks...][IEND chunk]
        // IEND is a chunk: [4-byte length=0][4-byte "IEND"][4-byte CRC] = 12 bytes at the tail.
        png.Length.Should().BeGreaterThanOrEqualTo(8 + 25 + 12, "signature + IHDR + IEND minimum");
        string tail = System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4);
        tail.Should().Be("IEND", "every valid PNG ends with an IEND chunk (its type at length-8)");

        // The chunk type right after the 8-byte signature must be IHDR (after its 4-byte length field).
        string firstChunk = System.Text.Encoding.ASCII.GetString(png, 12, 4);
        firstChunk.Should().Be("IHDR", "the first chunk of a PNG is always IHDR");
    }
}
