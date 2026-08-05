namespace NML.Core.Skins;

/// <summary>
/// Builds URLs for rendering a Minecraft player's skin. Uses Crafatar (the standard community
/// skin-rendering service) for avatars, 3D head renders, and full-body renders. Falls back to
/// the default skin (Steve/Alex) for offline accounts whose UUID has no real skin attached.
/// </summary>
public sealed class SkinService
{
    /// <summary>Crafatar base URL. A reliable, widely-used free skin rendering service.</summary>
    public const string CrafatarBase = "https://crafatar.com";

    /// <summary>
    /// Build a 2D avatar (the player's face) URL.
    /// </summary>
    /// <param name="uuid">Player UUID (with or without dashes).</param>
    /// <param name="size">Image size in pixels (8–512).</param>
    public string AvatarUrl(string uuid, int size = 64)
    {
        ValidateUuid(uuid);
        return $"{CrafatarBase}/avatars/{Normalize(uuid)}?size={ClampSize(size)}&overlay";
    }

    /// <summary>
    /// Build a 3D head render URL (isometric, the classic launcher look).
    /// </summary>
    public string HeadRenderUrl(string uuid, int scale = 4)
    {
        ValidateUuid(uuid);
        return $"{CrafatarBase}/renders/head/{Normalize(uuid)}?scale={ClampScale(scale)}&overlay";
    }

    /// <summary>
    /// Build a 3D full-body render URL (the player's whole skin).
    /// </summary>
    public string BodyRenderUrl(string uuid, int scale = 4)
    {
        ValidateUuid(uuid);
        return $"{CrafatarBase}/renders/body/{Normalize(uuid)}?scale={ClampScale(scale)}&overlay";
    }

    /// <summary>
    /// Build a skin-texture download URL (the raw skin PNG from Mojang's textures server).
    /// </summary>
    public string SkinTextureUrl(string uuid)
    {
        ValidateUuid(uuid);
        return $"{CrafatarBase}/skins/{Normalize(uuid)}";
    }

    /// <summary>Normalize a UUID: strip dashes so Crafatar accepts both forms.</summary>
    public static string Normalize(string uuid) =>
        uuid.Replace("-", string.Empty).ToLowerInvariant();

    /// <summary>Determine whether a UUID is a real online UUID (and thus likely to have a skin).</summary>
    /// <remarks>
    /// Offline UUIDs are MD5-derived (version 3); Mojang online UUIDs are version 4. If the
    /// account is offline, Crafatar returns the default skin — but we can short-circuit by
    /// checking the version nibble.
    /// </remarks>
    public static bool IsLikelyOfflineUuid(string uuid)
    {
        string n = Normalize(uuid);
        if (n.Length < 13) return true;
        // Version nibble is the 13th hex char. '3' = v3 (MD5/offline), '4' = v4 (random/online).
        char version = n[12];
        return version != '4';
    }

    private static void ValidateUuid(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            throw new ArgumentException("UUID is required.", nameof(uuid));
    }

    private static int ClampSize(int size) => Math.Clamp(size, 8, 512);
    private static int ClampScale(int scale) => Math.Clamp(scale, 1, 16);
}
