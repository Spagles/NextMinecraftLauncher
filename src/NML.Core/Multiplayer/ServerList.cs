using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NML.Core.Models;

namespace NML.Core.Multiplayer;

/// <summary>
/// A user-saved multiplayer server entry. Persisted in <c>servers.json</c> by
/// <see cref="ServerListStore"/>. The <see cref="LastPing"/> snapshot is refreshed by
/// <see cref="ServerPinger"/> and cached for UI display.
/// </summary>
public sealed class ServerEntry
{
    /// <summary>Friendly display name shown in the server list.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Host name or IP address (no port suffix).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>TCP port (1–65535). Defaults to 25565.</summary>
    public int Port { get; set; } = 25565;

    /// <summary>True when the entry accepts the vanilla Microsoft account; false for
    /// cracked/offline servers. Stored so the UI can show the lock icon.</summary>
    public bool OnlineMode { get; set; } = true;

    /// <summary>Last successful ping snapshot, or null when never pinged / unreachable.</summary>
    public ServerPingSnapshot? LastPing { get; set; }

    /// <summary>Optional notes the user attached to this entry.</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Immutable snapshot of one server's <c>Server List Ping</c> response. Cheap to cache and
/// thread-safe to surface on the UI thread.
/// </summary>
public sealed class ServerPingSnapshot
{
    /// <summary>MOTD line 1, with legacy section-sign color codes stripped.</summary>
    public string MotdLine1 { get; init; } = string.Empty;

    /// <summary>MOTD line 2, with legacy color codes stripped.</summary>
    public string MotdLine2 { get; init; } = string.Empty;

    /// <summary>Number of players currently online.</summary>
    public int OnlinePlayers { get; init; }

    /// <summary>Maximum player capacity the server reports.</summary>
    public int MaxPlayers { get; init; }

    /// <summary>Reported protocol version number (e.g. 763 for 1.20.1).</summary>
    public int ProtocolVersion { get; init; }

    /// <summary>Human-readable version name (e.g. "1.20.1").</summary>
    public string VersionName { get; init; } = string.Empty;

    /// <summary>Round-trip latency in milliseconds.</summary>
    public int LatencyMs { get; init; }

    /// <summary>Icon data URL ("data:image/png;base64,...") or null if the server sent none.</summary>
    public string? FaviconDataUrl { get; init; }
}

/// <summary>
/// Persists the saved-server list as <c>servers.json</c> in the launcher's settings
/// directory. Mirrors the <c>InstanceStore</c> pattern: load-all / save-all on each mutation.
/// </summary>
public sealed class ServerListStore
{
    private readonly string _serversFile;

    public ServerListStore(string settingsDir)
    {
        Directory.CreateDirectory(settingsDir);
        _serversFile = Path.Combine(settingsDir, "servers.json");
    }

    /// <summary>Load all saved servers, or an empty list if none configured.</summary>
    public List<ServerEntry> LoadAll()
    {
        if (!File.Exists(_serversFile)) return new List<ServerEntry>();
        string json = File.ReadAllText(_serversFile);
        return JsonSerializer.Deserialize<List<ServerEntry>>(json) ?? new List<ServerEntry>();
    }

    /// <summary>Persist the full list, pretty-printed.</summary>
    public void SaveAll(IEnumerable<ServerEntry> servers)
    {
        var opts = new JsonSerializerOptions(JsonOptions.Default) { WriteIndented = true };
        string json = JsonSerializer.Serialize(servers.ToList(), opts);
        File.WriteAllText(_serversFile, json);
    }

    public void Add(ServerEntry server)
    {
        var all = LoadAll();
        // De-dupe by host:port — replacing any prior entry with the same endpoint.
        all.RemoveAll(s => s.Host == server.Host && s.Port == server.Port);
        all.Add(server);
        SaveAll(all);
    }

    public void Remove(string name)
    {
        var all = LoadAll();
        all.RemoveAll(s => s.Name == name);
        SaveAll(all);
    }

    /// <summary>Reorder a server to a new zero-based position in the list.</summary>
    public void Move(string name, int newIndex)
    {
        var all = LoadAll();
        int cur = all.FindIndex(s => s.Name == name);
        if (cur < 0 || newIndex < 0 || newIndex >= all.Count || cur == newIndex) return;
        var item = all[cur];
        all.RemoveAt(cur);
        all.Insert(newIndex, item);
        SaveAll(all);
    }
}

/// <summary>
/// Implements the Minecraft <c>Server List Ping</c> (SLP) handshake + status request for
/// modern (1.7+, protocol 5+) servers. One TCP connection per ping; the response JSON is
/// parsed into a <see cref="ServerPingSnapshot"/>. Fails fast (timeout / RST) and surfaces
/// the failure as a thrown <see cref="SocketException"/> so the caller can mark the server
/// unreachable rather than hanging the UI.
/// </summary>
public sealed class ServerPinger
{
    private const int DefaultTimeoutMs = 5000;

    /// <summary>
    /// Ping a server and return its status snapshot. Throws on connection failure or timeout.
    /// </summary>
    /// <param name="host">Host name or IP (no port).</param>
    /// <param name="port">TCP port.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ServerPingSnapshot> PingAsync(string host, int port, CancellationToken cancellationToken = default)
        => await PingAsync(host, port, DefaultTimeoutMs, cancellationToken).ConfigureAwait(false);

    /// <summary> overload with an explicit timeout (used by the UI for "quick re-ping").</summary>
    public async Task<ServerPingSnapshot> PingAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        // Combine the caller's token with our timeout.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
        using var stream = client.GetStream();
        stream.ReadTimeout = timeoutMs;
        stream.WriteTimeout = timeoutMs;

        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        // --- Handshake packet (id 0): protocol=-1 (status), server addr, port, next=1 (status) ---
        byte[] handshake = BuildHandshake(host, (ushort)port);
        await stream.WriteAsync(handshake, cts.Token).ConfigureAwait(false);

        // --- Status request packet (id 0, empty payload) ---
        await stream.WriteAsync(new byte[] { 0x01, 0x00 }, cts.Token).ConfigureAwait(false);

        // --- Read the response: VarInt length prefix, then packet id VarInt, then JSON string ---
        int packetLength = await ReadVarIntAsync(stream, cts.Token).ConfigureAwait(false);
        byte[] payload = new byte[packetLength];
        await ReadExactAsync(stream, payload, cts.Token).ConfigureAwait(false);

        // payload[0..n] = packet id VarInt, then VarInt string length, then UTF-8 JSON.
        int offset = 0;
        int packetId = ReadVarInt(payload, ref offset);
        if (packetId != 0)
            throw new InvalidDataException($"Unexpected status response packet id {packetId}.");
        int jsonLen = ReadVarInt(payload, ref offset);
        string json = Encoding.UTF8.GetString(payload, offset, jsonLen);

        long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
        int latencyMs = (int)(elapsedTicks * 1000L / System.Diagnostics.Stopwatch.Frequency);

        return ParseStatusJson(json, latencyMs);
    }

    /// <summary>Build the SLP handshake packet bytes (length-prefixed).</summary>
    private static byte[] BuildHandshake(string host, ushort port)
    {
        // Payload: protocol(VarInt=-1) + string-len(VarInt) + UTF-8 host + ushort port + next-state(VarInt=1)
        byte[] hostBytes = Encoding.UTF8.GetBytes(host);
        using var body = new MemoryStream();
        WriteVarInt(body, -1);              // protocol version: -1 = "status"
        WriteVarInt(body, hostBytes.Length);
        body.Write(hostBytes, 0, hostBytes.Length);
        body.WriteByte((byte)(port >> 8));  // unsigned big-endian port
        body.WriteByte((byte)(port & 0xFF));
        WriteVarInt(body, 1);               // next state: 1 = status

        byte[] bodyBytes = body.ToArray();
        using var packet = new MemoryStream();
        WriteVarInt(packet, bodyBytes.Length); // packet length prefix
        WriteVarInt(packet, 0);                // packet id = 0 (handshake)
        packet.Write(bodyBytes, 0, bodyBytes.Length);
        return packet.ToArray();
    }

    private static ServerPingSnapshot ParseStatusJson(string json, int latencyMs)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Version block.
        int proto = 0;
        string versionName = string.Empty;
        if (root.TryGetProperty("version", out var ver))
        {
            if (ver.TryGetProperty("protocol", out var p)) proto = p.GetInt32();
            if (ver.TryGetProperty("name", out var n)) versionName = n.GetString() ?? string.Empty;
        }

        // Players block.
        int online = 0, max = 0;
        if (root.TryGetProperty("players", out var pl))
        {
            if (pl.TryGetProperty("online", out var o)) online = o.GetInt32();
            if (pl.TryGetProperty("max", out var m)) max = m.GetInt32();
        }

        // Description: 1.13+ is an object {text,extra[...]}, 1.7–1.12 is a plain string.
        string motd1 = string.Empty, motd2 = string.Empty;
        if (root.TryGetProperty("description", out var desc))
            (motd1, motd2) = ParseDescription(desc);

        string? favicon = null;
        if (root.TryGetProperty("favicon", out var fav) && fav.ValueKind == JsonValueKind.String)
            favicon = fav.GetString();

        return new ServerPingSnapshot
        {
            MotdLine1 = motd1,
            MotdLine2 = motd2,
            OnlinePlayers = online,
            MaxPlayers = max,
            ProtocolVersion = proto,
            VersionName = versionName,
            LatencyMs = latencyMs,
            FaviconDataUrl = favicon,
        };
    }

    /// <summary>Extract up to two MOTD lines from either the modern chat-component object
    /// or the legacy plain-string description, stripping legacy §-codes.</summary>
    private static (string line1, string line2) ParseDescription(JsonElement desc)
    {
        string raw;
        if (desc.ValueKind == JsonValueKind.String)
            raw = desc.GetString() ?? string.Empty;
        else
        {
            // Build a flat text string from the component tree, preserving explicit newlines.
            var sb = new StringBuilder();
            FlattenComponent(desc, sb);
            raw = sb.ToString();
        }

        // Split on literal newline for two-line MOTDs.
        string[] parts = raw.Split('\n');
        string l1 = StripLegacyCodes(parts[0]).Trim();
        string l2 = parts.Length > 1 ? StripLegacyCodes(string.Join('\n', parts.Skip(1))).Trim() : string.Empty;
        return (l1, l2);
    }

    private static void FlattenComponent(JsonElement el, StringBuilder sb)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            sb.Append(el.GetString() ?? string.Empty);
            return;
        }
        if (el.ValueKind != JsonValueKind.Object) return;
        if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            sb.Append(t.GetString() ?? string.Empty);
        if (el.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
            foreach (var child in extra.EnumerateArray())
                FlattenComponent(child, sb);
    }

    /// <summary>Strip Minecraft legacy color/format codes (§ followed by one char).</summary>
    public static string StripLegacyCodes(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '§' && i + 1 < s.Length) { i++; continue; }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    // --- VarInt helpers (Minecraft's LEB128 variant: 7 bits/byte, sign-extended) ---

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

    private static int ReadVarInt(byte[] buf, ref int offset)
    {
        int result = 0, shift = 0;
        byte b;
        do
        {
            if (offset >= buf.Length) throw new EndOfStreamException("VarInt exceeds buffer.");
            b = buf[offset++];
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream s, CancellationToken ct)
    {
        int result = 0, shift = 0;
        byte[] one = new byte[1];
        while (true)
        {
            int n = await s.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Stream closed while reading VarInt.");
            byte b = one[0];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
            shift += 7;
            if (shift > 35) throw new InvalidDataException("VarInt too large.");
        }
    }

    private static async Task ReadExactAsync(NetworkStream s, byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await s.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (n == 0) throw new EndOfStreamException("Stream closed before full read.");
            total += n;
        }
    }
}
