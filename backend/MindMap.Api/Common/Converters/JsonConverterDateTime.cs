using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindMap.Api.Common.Converters;

/// <summary>
/// 自定义 DateTime JSON 转换器。
/// MySQL 读出的 DateTime 默认 Kind=Unspecified，System.Text.Json 序列化时不带时区后缀，
/// 前端 new Date('2026-08-21T12:34:56') 会把它当作本地时间解析，导致 UTC 时间被误读。
/// 此转换器将 Kind=Unspecified 的 DateTime 视为 UTC，输出带 "Z" 后缀的 ISO 8601 格式，
/// 确保前端 new Date('2026-08-21T12:34:56Z') 正确解析为 UTC 时间。
/// </summary>
public class JsonConverterDateTime : JsonConverter<DateTime>
{
    private const string UtcFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrEmpty(str)) return default;
        return DateTime.Parse(str, null, DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        if (value.Kind == DateTimeKind.Unspecified)
        {
            // Unspecified → 视为 UTC，手动输出带 "Z" 后缀
            writer.WriteStringValue(value.ToString(UtcFormat, CultureInfo.InvariantCulture));
        }
        else if (value.Kind == DateTimeKind.Local)
        {
            // Local → 转为 UTC
            writer.WriteStringValue(value.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture));
        }
        else
        {
            // Utc → 直接输出带 "Z"
            writer.WriteStringValue(value.ToString(UtcFormat, CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>
/// 可空 DateTime 的转换器，处理 null 值。
/// </summary>
public class JsonConverterNullableDateTime : JsonConverter<DateTime?>
{
    private const string UtcFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var str = reader.GetString();
        if (string.IsNullOrEmpty(str)) return null;
        return DateTime.Parse(str, null, DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }
        var dt = value.Value;
        if (dt.Kind == DateTimeKind.Unspecified)
        {
            writer.WriteStringValue(dt.ToString(UtcFormat, CultureInfo.InvariantCulture));
        }
        else if (dt.Kind == DateTimeKind.Local)
        {
            writer.WriteStringValue(dt.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteStringValue(dt.ToString(UtcFormat, CultureInfo.InvariantCulture));
        }
    }
}
