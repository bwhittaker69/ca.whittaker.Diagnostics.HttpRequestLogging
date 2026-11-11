using ca.whittaker.Diagnostics.HttpRequestLogging;
using System.Text;

static void Configure(RequestDiagnosticsOptions options)
{
    options.CaptureBody = true;
    options.BodyFieldNames.Add("name");
    options.CaptureQuery = true;
    options.QueryNames.Add("page");
}

if (args.Contains("--self-test"))
{
    var output = new CapturingProvider();
    var selfTestBuilder = WebApplication.CreateBuilder();
    selfTestBuilder.Logging.ClearProviders().AddProvider(output);
    await using var host = selfTestBuilder.Build();
    var pipeline = new ApplicationBuilder(host.Services);
    pipeline.UseRequestDiagnostics(Configure);
    string? received = null;
    pipeline.Run(async context =>
        received = await new StreamReader(context.Request.Body, leaveOpen: true).ReadToEndAsync());
    const string json = "{\"name\":\"example\",\"password\":\"synthetic-secret\",\"private\":\"synthetic-private\"}";
    var request = new DefaultHttpContext();
    request.Request.Method = "POST";
    request.Request.ContentType = "application/json";
    request.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
    await pipeline.Build()(request);
    var messages = string.Join("\n", output.Messages);
    if (received != json || messages.Contains("synthetic-secret") || messages.Contains("synthetic-private") ||
        !messages.Contains("[REDACTED]") || !messages.Contains("example"))
        throw new InvalidOperationException("Sample preservation/redaction check failed.");
    Console.WriteLine("Sample checks passed: redacted diagnostics and original downstream body.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseRouting();
app.UseRequestDiagnostics(Configure);
app.MapPost("/example", async (HttpContext context) =>
    Results.Text(await new StreamReader(context.Request.Body, leaveOpen: true).ReadToEndAsync()));
app.Run("http://localhost:5095");

sealed class CapturingProvider : ILoggerProvider
{
    public List<string> Messages { get; } = [];
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
    public void Dispose() { }

    private sealed class CaptureLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
    }
}
