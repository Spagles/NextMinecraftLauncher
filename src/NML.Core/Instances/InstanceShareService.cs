using System.Text;
using System.Text.Json;
using NML.Core.Instances;

namespace NML.Core.Instances;

/// <summary>
/// Encodes/decodes an Instance as a compact share code (base64-encoded JSON) so users can
/// share instance configurations (version + modloader + memory + window + JVM args) via
/// text. The share code contains only the config (no mods/binaries) — the recipient imports
/// it as a new instance, then downloads mods separately. Matching HMCL's instance-share.
/// </summary>
public static class InstanceShareService
{
    /// <summary>Encode an Instance into a portable share code (prefixed "NML#").</summary>
    public static string Encode(Instance instance)
    {
        string json = JsonSerializer.Serialize(instance, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        return "NML#" + Convert.ToBase64String(bytes);
    }

    /// <summary>Decode a share code back into an Instance. Returns null if invalid.</summary>
    public static Instance? Decode(string shareCode)
    {
        if (string.IsNullOrWhiteSpace(shareCode)) return null;

        string trimmed = shareCode.Trim();
        if (trimmed.StartsWith("NML#", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[4..];

        try
        {
            byte[] bytes = Convert.FromBase64String(trimmed);
            string json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<Instance>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Validate that a share code is well-formed (without fully decoding).</summary>
    public static bool IsValid(string shareCode)
    {
        if (!shareCode.StartsWith("NML#", StringComparison.OrdinalIgnoreCase)) return false;
        return Decode(shareCode) is not null;
    }
}
