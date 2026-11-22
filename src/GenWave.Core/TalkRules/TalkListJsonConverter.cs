using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenWave.Core.TalkRules;

/// <summary>Reads and writes every <see cref="TalkList{T}"/> as a plain JSON array.</summary>
internal sealed class TalkListJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(TalkList<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter?)Activator.CreateInstance(
            typeof(TalkListConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
}
