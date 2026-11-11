# ca.whittaker.Diagnostics.HttpRequestLogging

Explicit ASP.NET Core request diagnostics with opt-in capture and redaction. Intended for bounded troubleshooting where an application owner can identify safe fields. Defaults log only the HTTP method and a body-capture status. No response capture, persistence, retention, telemetry transport, or universal personal-data detection is provided.

## Requirements and installation

Targets .NET 9 and .NET 10. Requires the corresponding ASP.NET Core shared framework (`Microsoft.AspNetCore.App`) and a host logging provider. No runtime NuGet dependencies are added.

```powershell
dotnet add package ca.whittaker.Diagnostics.HttpRequestLogging --version 1.0.0
```

## Minimal quick start

In an ASP.NET Core application:

```csharp
using ca.whittaker.Diagnostics.HttpRequestLogging;

var app = WebApplication.Create(args);
app.UseRequestDiagnostics();
app.MapGet("/", () => "Example");
app.Run();
```

Ensure your host enables Information logging for `ca.whittaker.Diagnostics.HttpRequestLogging.RequestDiagnosticsMiddleware`. Event 1001, RequestDiagnostics, contains a structured `RequestDiagnostics` field with serialized JSON. Debug and Release use identical capture rules and host logging filters.

## Explicit capture configuration

```csharp
app.UseRequestDiagnostics(options =>
{
    options.CaptureHeaders = true;
    options.HeaderNames.Add("X-Correlation-Id");
    options.CaptureQuery = true;
    options.QueryNames.Add("page");

    // Only enable after reviewing the application's data and access policy.
    options.CaptureBody = true;
    options.BodyFieldNames.Add("name");
    options.MaxBodyBytes = 4096;
});
```

For synthetic JSON `{"name":"example","password":"synthetic-secret","private":"omit"}`, this configuration captures `name`, replaces the recognized sensitive `password` value with `[REDACTED]`, and omits the unapproved `private` property. Unknown fields are not automatically safe.

Nested object/array properties and their children must each be allowed by name. Scalar array items are redacted. Header/query capture also requires explicit name lists. Recognized sensitive names are always redacted even when allowed. Matching ignores case and punctuation; recognized fragments include password, token, secret, API key, credential, authorization, cookie, credit card and session ID. Add application-specific sensitive names through `AdditionalSensitiveNames`.

## Options and defaults

| Option | Default / meaning |
|---|---|
| Enabled | true when registered; false bypasses capture and logging |
| CaptureMethod | true |
| CapturePath | false; enabling exposes raw route values |
| CaptureHeaders / CaptureQuery / CaptureBody | false |
| HeaderNames / QueryNames / BodyFieldNames | empty, case-insensitive explicit allowlists |
| AdditionalSensitiveNames | empty; supplements mandatory sensitive-name rules |
| MaxBodyBytes | 16384 input bytes; allowed range 1 to 1048576 |
| MaxValueCharacters | 256 for method/path/header/query values; range 1 to 4096 |
| LogLevel | Information; None bypasses capture |

Configuration is copied during registration/construction. Changing the original object afterward does not update running middleware. First values only are captured for multi-value headers/query fields. Long metadata values end with `[TRUNCATED]`. Options are validated; invalid limits or log levels throw `ArgumentOutOfRangeException`. Required null constructor/extension arguments throw `ArgumentNullException`.

## Body rules and preservation

Body capture requires both `CaptureBody=true` and a nonempty body-field allowlist. Only UTF-8 `application/json` and `application/*+json` are supported. Compressed, form, multipart, text, scalar-root, malformed, excessively nested and oversized bodies are omitted. Parser/read exception text and partial raw bodies are never logged.

A known oversized Content-Length is rejected without reading. For unknown lengths, at most MaxBodyBytes + 1 bytes are inspected before downstream execution. The extra byte detects overflow. JSON is parsed before capture, with a maximum depth of 32.

Seekable bodies are returned to their original position. Non-seekable bodies use ASP.NET Core buffering so downstream code can replay the captured prefix and read the rest. The middleware does not dispose the request stream or impose a downstream body-size limit. Framework buffering can spill later downstream reads to temporary storage; ASP.NET Core owns that buffering lifetime. See [buffering guidance](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httprequestrewindextensions.enablebuffering).

Body status values describe omission without source data: disabled, not-configured, empty, too-large, unsupported-content-type, unsupported-encoding, unsupported-root, invalid-json, unreadable, restore-failed, or captured. If a broken/disposed stream prevents rewind, capture is omitted with restore-failed; normal downstream preservation cannot be guaranteed for that failed stream.

## Registration order and failure behaviour

Register before the endpoints you want to inspect. A typical order is exception handling, routing, diagnostics, authorization, and endpoints. Keep authentication, environment gating and provider configuration in the host. Place diagnostics after static-file middleware if static-file requests should be excluded. This package does not authorize requests.

When disabled or filtered out by the logger, no body inspection occurs. Ordinary diagnostics/provider failures omit diagnostics and still invoke the next handler once. Request cancellation propagates. Downstream exceptions propagate unchanged; this middleware does not turn an application failure into success.

## Security limitations

Redaction is name-based, not a guarantee against all secrets or personal information. An allowed innocuous field may contain a secret. Raw paths, property names, configured safe fields and exception output elsewhere in the application may still reveal data. Review schema/allowlists and keep capture disabled when confidentiality cannot be assured. There is no unrestricted raw-body logging option.

Logging consumes CPU/memory and may buffer request data. Do not enable broad capture without evaluating traffic, permissions, retention and the host's logging provider. Use only synthetic payloads in examples and support reports.

## Sample, troubleshooting and migration

The [runnable sample](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging/tree/main/samples/HttpDiagnostics.Sample) supports `--self-test` without opening a network listener. It proves redacted log output and unchanged downstream body content using the packed package.

If there is no output, check Enabled, LogLevel and host filters. If body capture is omitted, check bodyStatus, explicit field names, media type/charset/encoding and byte limit. For nested values, allow both container and child names. Do not log raw request data merely to diagnose a capture failure.

Replace the older `UseWhittakerHttpRequestLogging(bool)` call with `UseRequestDiagnostics(options => options.Enabled = ...)`. Keep a previously disabled registration disabled during migration. The public root namespace is `ca.whittaker.Diagnostics.HttpRequestLogging`. See [configuration and migration](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging/blob/main/docs/configuration.md).

## Public API and project links

Public types: `RequestDiagnosticsOptions`, `RequestDiagnosticsMiddleware` (constructor and `InvokeAsync(HttpContext)`), and `RequestDiagnosticsExtensions.UseRequestDiagnostics(IApplicationBuilder, Action<RequestDiagnosticsOptions>?)`. The application owns its next delegate and ILogger destination.

- [Source](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging)
- [NuGet versions](https://www.nuget.org/packages/ca.whittaker.Diagnostics.HttpRequestLogging)
- [Releases](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging/releases)
- [Contributing](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging/blob/main/CONTRIBUTING.md)
- [Support](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging/blob/main/SUPPORT.md)
- [Security reporting](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging/blob/main/SECURITY.md)
- [GPL version 3 licence](https://github.com/bwhittaker69/ca.whittaker.Diagnostics.HttpRequestLogging/blob/main/LICENSE.txt)

Copyright (c) 2026 Brett Whittaker.
