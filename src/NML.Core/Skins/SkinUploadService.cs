using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NML.Core.Skins;

/// <summary>Supported skin models for upload.</summary>
public enum SkinVariant { Classic, Slim }

/// <summary>
/// Uploads a skin PNG to a Minecraft account via the Mojang/Minecraft Services API.
/// For Microsoft accounts, the access token returned by the auth flow works directly.
/// The endpoint is <c>POST https://api.minecraftservices.com/minecraft/skins</c> with a
/// multipart form (the PNG file, variant = "classic" or "slim").
/// </summary>
public sealed class SkinUploadService
{
    private const string SkinsEndpoint = "https://api.minecraftservices.com/minecraft/skins";

    /// <summary>
    /// Upload <paramref name="skinPngPath"/> as the active skin for the account owning
    /// <paramref name="accessToken"/>. Replaces any existing skin.
    /// </summary>
    public async Task UploadAsync(string accessToken, string skinPngPath, SkinVariant variant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token is required to upload a skin.", nameof(accessToken));
        if (!File.Exists(skinPngPath))
            throw new FileNotFoundException("Skin PNG not found.", skinPngPath);

        using var form = new MultipartFormDataContent();
        await using var fs = File.OpenRead(skinPngPath);
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", Path.GetFileName(skinPngPath));
        form.Add(new StringContent(variant == SkinVariant.Slim ? "slim" : "classic"), "variant");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await http.PostAsync(SkinsEndpoint, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Skin upload failed ({(int)resp.StatusCode} {resp.StatusCode}): {body}");
        }
    }

    /// <summary>Reset the account's skin to the default (Steve/Alex).</summary>
    public async Task ResetAsync(string accessToken, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await http.DeleteAsync($"{SkinsEndpoint}/active", ct);
        if (!resp.IsSuccessStatusCode && resp.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Skin reset failed ({resp.StatusCode}): {body}");
        }
    }
}
