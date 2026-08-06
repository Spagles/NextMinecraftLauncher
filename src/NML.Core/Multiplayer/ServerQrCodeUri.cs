using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace NML.Core.Multiplayer;

/// <summary>
/// Builds the shareable QR-code payload for a Minecraft server. The URI scheme
/// <c>mc://connect?host=...&amp;port=...&amp;name=...</c> is a launcher-convention format that
/// encodes everything a player needs to connect — the QR encodes this string, and any phone scanner
/// can read it as plain text. Pure + unit-tested.
/// <para>
/// The host is URL-encoded so special characters (IPv6 brackets, spaces in hostnames) survive the
/// round-trip. The port is omitted when it's the default 25565 (shorter QR = easier to scan).
/// </para>
/// </summary>
public static class ServerQrCodeUri
{
    /// <summary>Build the QR payload string for a server entry.</summary>
    public static string Build(string name, string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host is required.", nameof(host));

        var parts = new List<string> { $"host={WebUtility.UrlEncode(host)}" };
        if (port != 25565 && port > 0)
            parts.Add($"port={port}");
        if (!string.IsNullOrWhiteSpace(name))
            parts.Add($"name={WebUtility.UrlEncode(name)}");

        return $"mc://connect?{string.Join("&", parts)}";
    }

    /// <summary>Parse a QR payload back into (name, host, port). Returns null when the URI is not a
    /// valid <c>mc://connect?...</c> payload. Used by the import-from-scan path and by tests.</summary>
    public static (string Name, string Host, int Port)? Parse(string uri)
    {
        const string prefix = "mc://connect?";
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string query = uri[prefix.Length..];
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string key = pair[..eq];
            string value = pair[(eq + 1)..];
            dict[key] = WebUtility.UrlDecode(value);
        }

        if (!dict.TryGetValue("host", out string? host) || string.IsNullOrEmpty(host))
            return null;

        int port = 25565;
        if (dict.TryGetValue("port", out string? portStr) && int.TryParse(portStr, out int p))
            port = p;

        string name = dict.TryGetValue("name", out string? n) ? n : host;
        return (name, host, port);
    }
}
