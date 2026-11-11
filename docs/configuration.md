# Configuration and migration

Keep application authentication, production enablement, logging provider permissions and retention in the application. The library snapshots its configuration once. There is no dynamic global switch.

Start with method-only diagnostics. Choose header and query allowlists before enabling those captures. Their first values are captured and length-limited. Add confidential application field names to AdditionalSensitiveNames; built-in credential-name protection cannot be disabled.

For JSON, allow each container and child property needed for troubleshooting. Unknown properties are omitted. Recognized sensitive properties are redacted even when allowed. Scalar array entries are redacted. Custom application field names are not automatically recognized as sensitive, and permitted text may itself contain credentials.

Do not enable path capture on routes containing reset tokens or personal identifiers. Body capture defaults off, does not capture raw strings, and never logs parser exception text. Unsupported encodings/media types and over-limit data are omitted, not truncated and logged.

The original request body remains host-owned. Normal reads are rewound to the original position, or a buffered wrapper replays a non-seekable prefix. ASP.NET Core disposes request buffering. Downstream code should leave the body stream open for its owner. A failed underlying stream or failed rewind is explicitly outside the normal preservation guarantee.

Migration from the old helper requires the new package namespace and UseRequestDiagnostics extension. Preserve an old false setting with options.Enabled=false. Do not enable body capture simply because the old implementation read it.
