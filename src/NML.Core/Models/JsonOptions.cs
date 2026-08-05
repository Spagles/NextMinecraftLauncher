using System.Text.Json;
using NML.Core.Models.Serialization;

namespace NML.Core.Models;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for parsing all Mojang JSON documents.
/// Uses the custom <see cref="ArgumentElementConverter"/> for polymorphic argument elements.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ArgumentElementConverter() },
    };
}
