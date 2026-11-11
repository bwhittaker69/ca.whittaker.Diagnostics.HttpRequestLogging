using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ca.whittaker.Diagnostics.HttpRequestLogging;

/// <summary>Emits explicitly selected, redacted request diagnostics before invoking the next middleware.</summary>
/// <remarks>No response capture is performed. Cancellation and downstream exceptions propagate.</remarks>
public sealed class RequestDiagnosticsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestDiagnosticsMiddleware> _logger;
    private readonly RequestDiagnosticsOptions _options;
    private readonly RequestRedactor _redactor;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Creates middleware with an immutable copy of the supplied configuration.</summary>
    /// <param name="next">Next request handler.</param>
    /// <param name="logger">Host-owned logging destination. Its level filters also gate body reads.</param>
    /// <param name="options">Capture configuration, copied and validated.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit or log level is invalid.</exception>
    public RequestDiagnosticsMiddleware(RequestDelegate next,
        ILogger<RequestDiagnosticsMiddleware> logger, RequestDiagnosticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _logger = logger;
        _options = options.Snapshot();
        _redactor = new RequestRedactor(_options);
    }

    // #############################################
    // REQUEST DIAGNOSTICS PROCESS
    // #############################################

    /// <summary>Captures permitted diagnostics, then runs the next request handler exactly once.</summary>
    /// <param name="context">Current HTTP context.</param>
    /// <returns>A task completing when the downstream handler completes.</returns>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    /// <exception cref="OperationCanceledException">Request body capture is cancelled.</exception>
    /// <remarks>Ordinary diagnostics/provider failures do not suppress downstream execution.
    /// Exceptions thrown by the next handler propagate unchanged. No raw body or capture exception message is logged.</remarks>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // -----------------------------------------
        // STEP 1: CHECK CAPTURE ENABLEMENT
        // -----------------------------------------
        if (_options.Enabled && _options.LogLevel != LogLevel.None)
        {
            try
            {
                if (_logger.IsEnabled(_options.LogLevel))
                {
                    // -----------------------------------------
                    // STEP 2: CAPTURE ONLY SELECTED METADATA
                    // -----------------------------------------
                    var headers = _options.CaptureHeaders ? CaptureHeaders(context.Request) : null;
                    var query = _options.CaptureQuery ? CaptureQuery(context.Request) : null;

                    // -----------------------------------------
                    // STEP 3: READ, REDACT AND REWIND A BOUNDED BODY
                    // -----------------------------------------
                    var body = await RequestBodyCapture.ReadAsync(
                        context.Request, _options, _redactor, context.RequestAborted);

                    // -----------------------------------------
                    // STEP 4: EMIT SAFE STRUCTURED DIAGNOSTICS
                    // -----------------------------------------
                    var snapshot = JsonSerializer.Serialize(new
                    {
                        method = _options.CaptureMethod ? _redactor.Clip(context.Request.Method) : null,
                        path = _options.CapturePath ? _redactor.Clip(context.Request.Path.Value ?? "") : null,
                        headers, query,
                        bodyStatus = body.Status,
                        body = body.Value
                    }, JsonOptions);
                    _logger.Log(_options.LogLevel, new EventId(1001, "RequestDiagnostics"),
                        "Request diagnostics: {RequestDiagnostics}", snapshot);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Fail closed: omit diagnostics. Do not expose source data or recursively call a failed logger.
            }
        }

        // -----------------------------------------
        // STEP 5: CONTINUE THE APPLICATION PIPELINE
        // -----------------------------------------
        await _next(context);
    }

    private Dictionary<string, string> CaptureHeaders(HttpRequest request)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _options.HeaderNames)
            if (request.Headers.TryGetValue(name, out var values))
                result[name] = _redactor.Value(name, values.Count > 0 ? values[0] ?? "" : "");
        return result;
    }

    private Dictionary<string, string> CaptureQuery(HttpRequest request)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _options.QueryNames)
            if (request.Query.TryGetValue(name, out var values))
                result[name] = _redactor.Value(name, values.Count > 0 ? values[0] ?? "" : "");
        return result;
    }
}
