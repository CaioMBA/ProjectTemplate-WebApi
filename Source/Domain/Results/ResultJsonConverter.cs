using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domain.Results;

public sealed class ResultJsonConverter : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        return typeToConvert == typeof(Result)
               || (typeToConvert.IsGenericType
                   && typeToConvert.GetGenericTypeDefinition() == typeof(Result<>));
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);

        if (typeToConvert == typeof(Result))
        {
            return new NonGenericResultConverter();
        }

        var valueType = typeToConvert.GetGenericArguments()[0];

        return (JsonConverter)Activator.CreateInstance(
            typeof(GenericResultConverter<>).MakeGenericType(valueType))!;
    }

    private const string IsSuccessProperty = "isSuccess";

    private const string ErrorProperty = "error";

    private const string ValueProperty = "value";

    private static (bool IsSuccess, Error Error, JsonElement? Value) ReadEnvelope(
        ref Utf8JsonReader reader,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);

        var root = document.RootElement;

        var isSuccess = root.TryGetProperty(IsSuccessProperty, out var successElement)
                        && successElement.GetBoolean();

        var error = root.TryGetProperty(ErrorProperty, out var errorElement)
                    && errorElement.ValueKind is not JsonValueKind.Null
            ? errorElement.Deserialize<Error>(options) ?? Error.None
            : Error.None;

        JsonElement? value = root.TryGetProperty(ValueProperty, out var valueElement)
                             && valueElement.ValueKind is not JsonValueKind.Null
            ? valueElement.Clone()
            : null;

        return (isSuccess, error, value);
    }

    private sealed class NonGenericResultConverter : JsonConverter<Result>
    {
        public override Result Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var (isSuccess, error, _) = ReadEnvelope(ref reader, options);

            return isSuccess ? Result.Success() : Result.Failure(error);
        }

        public override void Write(Utf8JsonWriter writer, Result value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartObject();
            writer.WriteBoolean(IsSuccessProperty, value.IsSuccess);

            writer.WritePropertyName(ErrorProperty);
            JsonSerializer.Serialize(writer, value.Error, options);

            writer.WriteEndObject();
        }
    }

    private sealed class GenericResultConverter<TValue> : JsonConverter<Result<TValue>>
    {
        public override Result<TValue> Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            var (isSuccess, error, value) = ReadEnvelope(ref reader, options);

            if (!isSuccess)
            {
                return Result.Failure<TValue>(error);
            }

            var payload = value is null
                ? default
                : value.Value.Deserialize<TValue>(options);

            return payload is null
                ? Result.Failure<TValue>(Error.NullValue)
                : Result.Success(payload);
        }

        public override void Write(
            Utf8JsonWriter writer,
            Result<TValue> value,
            JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);

            writer.WriteStartObject();
            writer.WriteBoolean(IsSuccessProperty, value.IsSuccess);

            writer.WritePropertyName(ErrorProperty);
            JsonSerializer.Serialize(writer, value.Error, options);

            if (value.IsSuccess)
            {
                writer.WritePropertyName(ValueProperty);
                JsonSerializer.Serialize(writer, value.Value, options);
            }

            writer.WriteEndObject();
        }
    }
}
