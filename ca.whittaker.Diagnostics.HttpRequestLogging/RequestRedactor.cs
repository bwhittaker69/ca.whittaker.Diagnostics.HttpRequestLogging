using System.Text.Json.Nodes;

namespace ca.whittaker.Diagnostics.HttpRequestLogging;

internal sealed class RequestRedactor(RequestDiagnosticsOptions options)
{
    internal const string Redacted = "[REDACTED]";
    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "token", "secret", "apikey", "credential", "authorization",
        "cookie", "creditcard", "sessionid", "ssn", "referer", "referrer"
    ];

    private static string Normalize(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    internal bool IsSensitive(string name)
    {
        var normalized = Normalize(name);
        return normalized == "key" ||
            SensitiveFragments.Any(normalized.Contains) ||
            options.AdditionalSensitiveNames.Any(x => Normalize(x) == normalized);
    }

    internal string Clip(string value) =>
        value.Length <= options.MaxValueCharacters ? value : value[..options.MaxValueCharacters] + "[TRUNCATED]";

    internal string Value(string name, string value) =>
        IsSensitive(name) ? Redacted : Clip(value);

    internal JsonNode? Body(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var field in obj)
            {
                if (IsSensitive(field.Key))
                    result[field.Key] = Redacted;
                else if (options.BodyFieldNames.Contains(field.Key))
                    result[field.Key] = field.Value is JsonValue
                        ? field.Value.DeepClone()
                        : Body(field.Value);
            }
            return result;
        }
        if (node is JsonArray array)
        {
            var result = new JsonArray();
            foreach (var item in array)
                result.Add(item is JsonValue ? JsonValue.Create(Redacted) : Body(item));
            return result;
        }
        return null;
    }
}
