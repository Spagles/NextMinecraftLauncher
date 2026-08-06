using System.Net;
using System.Net.Sockets;
using System.Text;
using NML.Core.Multiplayer;

namespace NML.Core.Tests;

/// <summary>
/// Verifies the multiplayer server-list feature end-to-end:
/// <list type="bullet">
/// <item><see cref="ServerListStore"/> round-trips entries to/from servers.json and
///   preserves ordering, dedupes by host:port, and supports reordering.</item>
/// <item><see cref="ServerPinger"/> speaks the Server-List-Ping protocol against a real
///   loopback TCP server, parsing MOTD, player counts, protocol version, and latency.</item>
/// <item>Legacy §-code stripping is exercised directly (covers the text-cleaning helper).</item>
/// </list>
/// The pinger test binds a one-shot <see cref="TcpListener"/> on an ephemeral port so it
/// runs without external dependencies and without flaky real-world servers.
/// </summary>
public class MultiplayerServerListTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nml-mp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---------- ServerListStore persistence ----------

    [Fact]
    public void Store_RoundTrips_And_Dedupes_By_Host_Port()
    {
        string dir = TempDir();
        try
        {
            var store = new ServerListStore(dir);
            store.Add(new ServerEntry { Name = "Survival", Host = "play.example.net", Port = 25565 });
            store.Add(new ServerEntry { Name = "Creative", Host = "creative.example.net", Port = 25570 });

            var loaded = store.LoadAll();
            loaded.Should().HaveCount(2);
            loaded.Should().Contain(s => s.Name == "Survival" && s.Host == "play.example.net" && s.Port == 25565);

            // Adding the same host:port again replaces the prior entry (dedupe by endpoint).
            store.Add(new ServerEntry { Name = "Survival Renamed", Host = "play.example.net", Port = 25565 });
            loaded = store.LoadAll();
            loaded.Should().HaveCount(2);
            loaded.Should().NotContain(s => s.Name == "Survival");
            loaded.Should().Contain(s => s.Name == "Survival Renamed");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Store_Remove_And_Move_Reorder_The_List()
    {
        string dir = TempDir();
        try
        {
            var store = new ServerListStore(dir);
            store.Add(new ServerEntry { Name = "A", Host = "a.example", Port = 1 });
            store.Add(new ServerEntry { Name = "B", Host = "b.example", Port = 2 });
            store.Add(new ServerEntry { Name = "C", Host = "c.example", Port = 3 });

            // Move C (index 2) up to index 0.
            store.Move("C", 0);
            var loaded = store.LoadAll();
            loaded.Select(s => s.Name).Should().Equal("C", "A", "B");

            // Remove A.
            store.Remove("A");
            store.LoadAll().Select(s => s.Name).Should().Equal("C", "B");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Store_Move_OutOfRange_Is_A_NoOp()
    {
        string dir = TempDir();
        try
        {
            var store = new ServerListStore(dir);
            store.Add(new ServerEntry { Name = "Only", Host = "x.example", Port = 1 });
            store.Move("Only", 5); // out of range
            store.Move("nonexistent", 0); // missing
            store.LoadAll().Select(s => s.Name).Should().Equal("Only");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------- ServerPinger protocol round-trip ----------

    [Fact]
    public async Task Pinger_Parses_Status_Response_From_Loopback_Server()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Run a one-shot server that responds to the SLP handshake + status request.
        var serverTask = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            // Read the handshake packet (length + id + body) — we don't need to parse it.
            await ReadPacketAsync(stream);
            // Read the status request packet.
            await ReadPacketAsync(stream);

            // Send back a status response with a known MOTD / player count / version.
            string json = "{\"version\":{\"name\":\"1.20.1\",\"protocol\":763}," +
                          "\"players\":{\"max\":20,\"online\":7}," +
                          "\"description\":{\"text\":\"§aWelcome§r\\nSecond line\"}}";
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

            // Build the packet *content*: packet id (VarInt=0) + string length (VarInt) + JSON.
            using var content = new MemoryStream();
            WriteVarInt(content, 0);                          // packet id = 0 (status response)
            WriteVarInt(content, jsonBytes.Length);           // string length prefix
            content.Write(jsonBytes, 0, jsonBytes.Length);
            byte[] contentBytes = content.ToArray();

            // Frame = packetLength (VarInt = len of content) followed by content.
            using var frame = new MemoryStream();
            WriteVarInt(frame, contentBytes.Length);          // packet length prefix
            frame.Write(contentBytes, 0, contentBytes.Length);
            await stream.WriteAsync(frame.ToArray());
            await stream.FlushAsync();
            // Hold the connection open briefly so the pinger can finish reading before the
            // server-side dispose sends an RST. (Closing immediately races the read.)
            await Task.Delay(500);
        });

        var pinger = new ServerPinger();
        ServerPingSnapshot snap = await pinger.PingAsync("127.0.0.1", port, timeoutMs: 3000);

        await serverTask; // ensure the server task completed without exceptions.
        listener.Stop();

        snap.VersionName.Should().Be("1.20.1");
        snap.ProtocolVersion.Should().Be(763);
        snap.OnlinePlayers.Should().Be(7);
        snap.MaxPlayers.Should().Be(20);
        // Legacy §-codes stripped, two-line MOTD split.
        snap.MotdLine1.Should().Be("Welcome");
        snap.MotdLine2.Should().Be("Second line");
        snap.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Pinger_Unreachable_Throws_On_Connection_Failure()
    {
        // Bind and immediately release a port so it's almost certainly free / refusing.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var pinger = new ServerPinger();
        Func<Task> act = () => pinger.PingAsync("127.0.0.1", port, timeoutMs: 800);
        // Either a connection-refused SocketException or the timeout cancellation surfaces —
        // both mean "server unreachable", which is what the launcher needs to know.
        (await act.Should().ThrowAsync<Exception>())
            .Which.Should().BeAssignableTo<SystemException>();
    }

    [Fact]
    public void Pinger_Strips_Legacy_Color_Codes()
    {
        ServerPinger.StripLegacyCodes("§aGreen§rPlain").Should().Be("GreenPlain");
        ServerPinger.StripLegacyCodes("No codes").Should().Be("No codes");
        ServerPinger.StripLegacyCodes("").Should().Be("");
        // A dangling section sign with no following char is preserved (defensive — no code to strip).
        ServerPinger.StripLegacyCodes("§").Should().Be("§");
    }

    // ---------- helpers mirroring the protocol ----------

    private static async Task ReadPacketAsync(NetworkStream s)
    {
        // length VarInt then that many bytes
        int len = await ReadVarIntAsync(s);
        byte[] buf = new byte[len];
        int total = 0;
        while (total < len)
        {
            int n = await s.ReadAsync(buf.AsMemory(total, len - total));
            if (n == 0) return;
            total += n;
        }
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream s)
    {
        int result = 0, shift = 0;
        byte[] one = new byte[1];
        while (true)
        {
            int n = await s.ReadAsync(one.AsMemory(0, 1));
            if (n == 0) throw new EndOfStreamException();
            byte b = one[0];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
        }
    }

    private static void WriteVarInt(Stream s, int value)
    {
        uint v = (uint)value;
        while (true)
        {
            if ((v & ~0x7Fu) == 0) { s.WriteByte((byte)v); return; }
            s.WriteByte((byte)(v & 0x7F | 0x80));
            v >>= 7;
        }
    }
}
