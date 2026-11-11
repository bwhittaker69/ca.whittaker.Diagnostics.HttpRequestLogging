using Microsoft.Extensions.Logging;

namespace ca.whittaker.Diagnostics.HttpRequestLogging;

/// <summary>Configures explicitly selected request diagnostics. Only method capture is enabled by default.</summary>
/// <remarks>Configuration is copied at registration/construction. Changing the original options later has no effect.</remarks>
public sealed class RequestDiagnosticsOptions
{
    /// <summary>Gets or sets whether diagnostics run. Defaults to true when this middleware is registered.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets whether the HTTP method is captured. Defaults to true.</summary>
    public bool CaptureMethod { get; set; } = true;
    /// <summary>Gets or sets whether the raw path is captured. Defaults to false; paths may contain sensitive values.</summary>
    public bool CapturePath { get; set; }
    /// <summary>Gets or sets whether allowlisted headers are captured. Defaults to false.</summary>
    public bool CaptureHeaders { get; set; }
    /// <summary>Gets or sets whether allowlisted query fields are captured. Defaults to false.</summary>
    public bool CaptureQuery { get; set; }
    /// <summary>Gets or sets whether bounded JSON body capture is enabled. Defaults to false.</summary>
    public bool CaptureBody { get; set; }
    /// <summary>Gets or sets the input body limit in bytes. Defaults to 16384; allowed range is 1 through 1048576.</summary>
    public int MaxBodyBytes { get; set; } = 16384;
    /// <summary>Gets or sets the method/path/header/query value character limit. Defaults to 256; range 1 through 4096.</summary>
    public int MaxValueCharacters { get; set; } = 256;
    /// <summary>Gets or sets the logging level. Defaults to Information; None skips all capture.</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    /// <summary>Gets explicitly allowed header names. Initially empty; recognized sensitive names are always redacted.</summary>
    public ISet<string> HeaderNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets explicitly allowed query names. Initially empty; recognized sensitive names are always redacted.</summary>
    public ISet<string> QueryNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets allowed JSON property names, initially empty. Nested containers and their children both need allowance.</summary>
    /// <remarks>Sensitive names are redacted even if allowed. Other unapproved properties are omitted. Scalar array items are redacted.</remarks>
    public ISet<string> BodyFieldNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets extra sensitive names to redact, initially empty. Matching ignores case and punctuation.</summary>
    public ISet<string> AdditionalSensitiveNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal RequestDiagnosticsOptions Snapshot()
    {
        if (MaxBodyBytes is < 1 or > 1048576)
            throw new ArgumentOutOfRangeException(nameof(MaxBodyBytes));
        if (MaxValueCharacters is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(MaxValueCharacters));
        if (!Enum.IsDefined(LogLevel))
            throw new ArgumentOutOfRangeException(nameof(LogLevel));
        var copy = new RequestDiagnosticsOptions
        {
            Enabled = Enabled, CaptureMethod = CaptureMethod, CapturePath = CapturePath,
            CaptureHeaders = CaptureHeaders, CaptureQuery = CaptureQuery, CaptureBody = CaptureBody,
            MaxBodyBytes = MaxBodyBytes, MaxValueCharacters = MaxValueCharacters, LogLevel = LogLevel
        };
        copy.HeaderNames.UnionWith(HeaderNames);
        copy.QueryNames.UnionWith(QueryNames);
        copy.BodyFieldNames.UnionWith(BodyFieldNames);
        copy.AdditionalSensitiveNames.UnionWith(AdditionalSensitiveNames);
        return copy;
    }
}
