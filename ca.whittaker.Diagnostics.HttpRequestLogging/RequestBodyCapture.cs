using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ca.whittaker.Diagnostics.HttpRequestLogging;

internal sealed record BodyCapture(string Status, JsonNode? Value = null);

internal static class RequestBodyCapture
{
    internal static async Task<BodyCapture> ReadAsync(
        HttpRequest request, RequestDiagnosticsOptions options, RequestRedactor redactor,
        CancellationToken cancellationToken)
    {
        if (!options.CaptureBody) return new("disabled");
        if (options.BodyFieldNames.Count == 0) return new("not-configured");
        if (!request.Body.CanRead) return new("unreadable");
        if (request.ContentLength == 0) return new("empty");
        if (request.ContentLength > options.MaxBodyBytes) return new("too-large");
        if (request.Headers.ContentEncoding.Count > 0 &&
            !string.Equals(request.Headers.ContentEncoding.ToString(), "identity", StringComparison.OrdinalIgnoreCase))
            return new("unsupported-encoding");
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type))
            return new("unsupported-content-type");
        var mediaType = type.MediaType.Value ?? "";
        if (!mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) &&
            !(mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) &&
              mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            return new("unsupported-content-type");
        if (type.Charset.HasValue &&
            !type.Charset.Value!.Equals("utf-8", StringComparison.OrdinalIgnoreCase) &&
            !type.Charset.Value.Equals("utf8", StringComparison.OrdinalIgnoreCase))
            return new("unsupported-encoding");

        var originalPosition = request.Body.CanSeek ? request.Body.Position : 0;
        BodyCapture result;
        var restored = true;
        try
        {
            // Buffer only the bounded prefix for diagnostics. Do not impose a limit on downstream reads.
            request.EnableBuffering(bufferThreshold: options.MaxBodyBytes + 1);
            var bytes = new byte[options.MaxBodyBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await request.Body.ReadAsync(bytes.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }
            if (count > options.MaxBodyBytes)
                result = new("too-large");
            else if (count == 0)
                result = new("empty");
            else
            {
                var json = JsonNode.Parse(bytes.AsSpan(0, count),
                    documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
                result = json is JsonObject or JsonArray
                    ? new("captured", redactor.Body(json))
                    : new("unsupported-root");
            }
        }
        catch (JsonException)
        {
            result = new("invalid-json"); // Never log parser messages containing source data.
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
        {
            result = new("unreadable");
        }
        finally
        {
            try
            {
                if (request.Body.CanSeek) request.Body.Position = originalPosition;
                else restored = false;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
            {
                restored = false;
            }
        }
        return restored ? result : new("restore-failed");
    }
}
