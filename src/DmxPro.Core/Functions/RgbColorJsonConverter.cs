using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmxPro.Core.Functions;

/// <summary>Writes <see cref="RgbColor"/> as "#RRGGBB" and reads it back; anything else is a format error.</summary>
public sealed class RgbColorJsonConverter : JsonConverter<RgbColor>
{
    public override RgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && RgbColor.TryParseHex(reader.GetString(), out var color))
        {
            return color;
        }

        throw new JsonException("A colour must be a \"#RRGGBB\" string.");
    }

    public override void Write(Utf8JsonWriter writer, RgbColor value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToHex());
    }
}
