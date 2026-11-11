using Microsoft.AspNetCore.Builder;

namespace ca.whittaker.Diagnostics.HttpRequestLogging;

/// <summary>Registers request diagnostics through an explicit pipeline call.</summary>
public static class RequestDiagnosticsExtensions
{
    /// <summary>Adds middleware with method-only capture unless the caller explicitly opts into more fields.</summary>
    /// <param name="builder">Application pipeline.</param>
    /// <param name="configure">Optional configuration callback, run once at registration.</param>
    /// <returns>The application builder for further middleware registration.</returns>
    /// <exception cref="ArgumentNullException">The builder is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Configured limits or log level are invalid.</exception>
    /// <remarks>Register before the endpoint to inspect. Keep host authorization, environment and provider decisions in the application.</remarks>
    public static IApplicationBuilder UseRequestDiagnostics(
        this IApplicationBuilder builder, Action<RequestDiagnosticsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new RequestDiagnosticsOptions();
        configure?.Invoke(options);
        return builder.UseMiddleware<RequestDiagnosticsMiddleware>(options.Snapshot());
    }
}
