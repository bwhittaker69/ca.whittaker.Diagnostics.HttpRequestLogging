using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Text;
using System.Text.Json;

namespace ca.whittaker.Diagnostics.HttpRequestLogging.UnitTests;

public class RequestDiagnosticsMiddlewareTests
{
    private const string Json = "{\"name\":\"safe\",\"password\":\"synthetic-password\",\"ignored\":\"synthetic-private\"}";

    [Test]
    public async Task Defaults_DoNotReadOrExposeSensitiveRequestData()
    {
        var stream = new NonSeekableStream(Json);
        var context = Context(stream);
        context.Request.Path = "/synthetic-private-path";
        context.Request.Headers.Authorization = "Bearer synthetic-token";
        context.Request.QueryString = new QueryString("?password=synthetic-query");
        var log = new RecordingLogger();
        var nextCalls = 0;
        var middleware = new RequestDiagnosticsMiddleware(_ => { nextCalls++; return Task.CompletedTask; }, log, new());

        await middleware.InvokeAsync(context);

        Assert.That(nextCalls, Is.EqualTo(1));
        Assert.That(stream.BytesRead, Is.Zero);
        using var output = Snapshot(log);
        Assert.That(output.RootElement.GetProperty("method").GetString(), Is.EqualTo("POST"));
        Assert.That(output.RootElement.TryGetProperty("path", out _), Is.False);
        Assert.That(log.Messages.Single(), Does.Not.Contain("synthetic"));
        Assert.That(output.RootElement.GetProperty("bodyStatus").GetString(), Is.EqualTo("disabled"));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task DisabledMiddlewareOrLogLevel_DoesNotReadBody(bool enabled, bool logEnabled)
    {
        var stream = new NonSeekableStream(Json);
        var context = Context(stream);
        var logger = new RecordingLogger { Enabled = logEnabled };
        var options = BodyOptions();
        options.Enabled = enabled;
        var nextCalls = 0;
        var middleware = new RequestDiagnosticsMiddleware(_ => { nextCalls++; return Task.CompletedTask; }, logger, options);

        await middleware.InvokeAsync(context);

        Assert.That(stream.BytesRead, Is.Zero);
        Assert.That(logger.Messages, Is.Empty);
        Assert.That(nextCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task HeaderAndQuerySecrets_AreRedactedRegardlessOfAllowance()
    {
        var context = Context(new MemoryStream());
        context.Request.Headers.Authorization = "Bearer synthetic-token";
        context.Request.Headers.Cookie = "synthetic-cookie";
        context.Request.Headers["X-API-Key"] = "synthetic-key";
        context.Request.Headers["X-Trace"] = "safe";
        context.Request.QueryString = new("?access_token=synthetic-query&PaSs_Word=synthetic-password&page=2&unknown=synthetic-private");
        var options = new RequestDiagnosticsOptions { CaptureHeaders = true, CaptureQuery = true };
        options.HeaderNames.UnionWith(["Authorization", "Cookie", "X-API-Key", "X-Trace"]);
        options.QueryNames.UnionWith(["access_token", "PaSs_Word", "page"]);
        var logger = new RecordingLogger();
        var middleware = new RequestDiagnosticsMiddleware(_ => Task.CompletedTask, logger, options);

        await middleware.InvokeAsync(context);

        using var output = Snapshot(logger);
        var headers = output.RootElement.GetProperty("headers");
        Assert.That(headers.GetProperty("Authorization").GetString(), Is.EqualTo("[REDACTED]"));
        Assert.That(headers.GetProperty("Cookie").GetString(), Is.EqualTo("[REDACTED]"));
        Assert.That(headers.GetProperty("X-API-Key").GetString(), Is.EqualTo("[REDACTED]"));
        Assert.That(headers.GetProperty("X-Trace").GetString(), Is.EqualTo("safe"));
        Assert.That(output.RootElement.GetProperty("query").GetProperty("page").GetString(), Is.EqualTo("2"));
        Assert.That(logger.Messages.Single(), Does.Not.Contain("synthetic"));
    }

    [Test]
    public async Task JsonCapture_RedactsNestedSecretsAndOmitsUnapprovedFields()
    {
        var json = "{\"user\":{\"name\":\"safe\",\"client_Secret\":\"synthetic-secret\",\"private\":\"synthetic-private\"}," +
                   "\"items\":[{\"name\":\"safe\",\"refreshToken\":\"synthetic-token\"},\"synthetic-array\"]," +
                   "\"password\":{\"nested\":\"synthetic-nested\"}}";
        var context = Context(new NonSeekableStream(json));
        var options = BodyOptions();
        options.BodyFieldNames.UnionWith(["user", "items"]);
        var logger = new RecordingLogger();
        string? downstream = null;
        var middleware = new RequestDiagnosticsMiddleware(async ctx =>
        {
            downstream = await new StreamReader(ctx.Request.Body, leaveOpen: true).ReadToEndAsync();
        }, logger, options);

        await middleware.InvokeAsync(context);

        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("body").GetProperty("user").GetProperty("name").GetString(), Is.EqualTo("safe"));
        Assert.That(logger.Messages.Single(), Does.Contain("[REDACTED]").And.Not.Contain("synthetic"));
        Assert.That(downstream, Is.EqualTo(json));
        Assert.That(context.Request.Body.CanRead, Is.True);
    }

    [Test]
    public async Task NoBodyAllowlist_DoesNotReadEvenWhenCaptureIsEnabled()
    {
        var stream = new NonSeekableStream(Json);
        var logger = new RecordingLogger();
        var middleware = new RequestDiagnosticsMiddleware(_ => Task.CompletedTask, logger,
            new RequestDiagnosticsOptions { CaptureBody = true });

        await middleware.InvokeAsync(Context(stream));

        Assert.That(stream.BytesRead, Is.Zero);
        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("bodyStatus").GetString(), Is.EqualTo("not-configured"));
    }

    [TestCase("application/json", "captured")]
    [TestCase("application/problem+json; charset=utf-8", "captured")]
    [TestCase("application/json; charset=utf-16", "unsupported-encoding")]
    [TestCase("text/plain", "unsupported-content-type")]
    [TestCase("application/x-www-form-urlencoded", "unsupported-content-type")]
    [TestCase("multipart/form-data; boundary=test", "unsupported-content-type")]
    public async Task ContentTypeControlsCapture(string contentType, string expected)
    {
        var stream = new NonSeekableStream(Json);
        var context = Context(stream);
        context.Request.ContentType = contentType;
        var logger = new RecordingLogger();
        var middleware = new RequestDiagnosticsMiddleware(_ => Task.CompletedTask, logger, BodyOptions());

        await middleware.InvokeAsync(context);

        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("bodyStatus").GetString(), Is.EqualTo(expected));
        if (expected != "captured") Assert.That(stream.BytesRead, Is.Zero);
    }

    [Test]
    public async Task CompressedBody_IsNotRead()
    {
        var stream = new NonSeekableStream(Json);
        var context = Context(stream);
        context.Request.Headers.ContentEncoding = "gzip";
        var logger = new RecordingLogger();

        await new RequestDiagnosticsMiddleware(_ => Task.CompletedTask, logger, BodyOptions()).InvokeAsync(context);

        Assert.That(stream.BytesRead, Is.Zero);
        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("bodyStatus").GetString(), Is.EqualTo("unsupported-encoding"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task OversizedBody_IsOmittedAndPreserved(bool knownLength)
    {
        var json = "{\"name\":\"" + new string('x', 80) + "\"}";
        var stream = new NonSeekableStream(json);
        var context = Context(stream);
        context.Request.ContentLength = knownLength ? Encoding.UTF8.GetByteCount(json) : null;
        var options = BodyOptions();
        options.MaxBodyBytes = 20;
        var logger = new RecordingLogger();
        var prefixRead = -1;
        string? downstream = null;
        var middleware = new RequestDiagnosticsMiddleware(async ctx =>
        {
            prefixRead = stream.BytesRead;
            downstream = await new StreamReader(ctx.Request.Body, leaveOpen: true).ReadToEndAsync();
        }, logger, options);

        await middleware.InvokeAsync(context);

        Assert.That(prefixRead, Is.EqualTo(knownLength ? 0 : 21));
        Assert.That(downstream, Is.EqualTo(json));
        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("bodyStatus").GetString(), Is.EqualTo("too-large"));
        Assert.That(output.RootElement.TryGetProperty("body", out _), Is.False);
    }

    [TestCase("{\"name\":\"synthetic-secret")]
    [TestCase("\"synthetic-secret\"")]
    public async Task InvalidOrScalarJson_NeverLogsRawBody(string json)
    {
        var context = Context(new NonSeekableStream(json));
        var logger = new RecordingLogger();
        string? downstream = null;

        await new RequestDiagnosticsMiddleware(async ctx =>
            downstream = await new StreamReader(ctx.Request.Body, leaveOpen: true).ReadToEndAsync(),
            logger, BodyOptions()).InvokeAsync(context);

        Assert.That(logger.Messages.Single(), Does.Not.Contain("synthetic-secret"));
        Assert.That(downstream, Is.EqualTo(json));
        using var output = Snapshot(logger);
        Assert.That(output.RootElement.TryGetProperty("body", out _), Is.False);
    }

    [Test]
    public async Task ExistingPosition_IsRestoredAndOriginalStreamStaysOpen()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("skip" + Json));
        stream.Position = 4;
        var context = Context(stream);
        var position = -1L;

        await new RequestDiagnosticsMiddleware(ctx => { position = ctx.Request.Body.Position; return Task.CompletedTask; },
            new RecordingLogger(), BodyOptions()).InvokeAsync(context);

        Assert.That(position, Is.EqualTo(4));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public async Task ReaderFailure_IsOmittedAndNextStillReceivesBody()
    {
        var stream = new NonSeekableStream(Json) { FailFirstRead = true };
        var logger = new RecordingLogger();
        string? downstream = null;

        await new RequestDiagnosticsMiddleware(async ctx =>
            downstream = await new StreamReader(ctx.Request.Body, leaveOpen: true).ReadToEndAsync(),
            logger, BodyOptions()).InvokeAsync(Context(stream));

        Assert.That(downstream, Is.EqualTo(Json));
        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("bodyStatus").GetString(), Is.EqualTo("unreadable"));
        Assert.That(logger.Messages.Single(), Does.Not.Contain("synthetic-reader-failure"));
    }

    [Test]
    public async Task LoggerFailure_DoesNotPreventDownstreamExecution()
    {
        var nextCalls = 0;
        var logger = new RecordingLogger { ThrowOnWrite = true };

        await new RequestDiagnosticsMiddleware(_ => { nextCalls++; return Task.CompletedTask; },
            logger, new()).InvokeAsync(Context(new MemoryStream()));

        Assert.That(nextCalls, Is.EqualTo(1));
    }

    [Test]
    public void DownstreamFailure_PropagatesUnchanged()
    {
        var failure = new InvalidOperationException("synthetic downstream failure");
        var middleware = new RequestDiagnosticsMiddleware(_ => throw failure, new RecordingLogger(), new());

        var actual = Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(Context(new MemoryStream())));

        Assert.That(actual, Is.SameAs(failure));
    }

    [Test]
    public void Cancellation_PropagatesAndRewinds()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
        var context = Context(stream);
        context.RequestAborted = new CancellationToken(canceled: true);
        var nextCalls = 0;
        var middleware = new RequestDiagnosticsMiddleware(_ => { nextCalls++; return Task.CompletedTask; },
            new RecordingLogger(), BodyOptions());

        Assert.CatchAsync<OperationCanceledException>(() => middleware.InvokeAsync(context));

        Assert.That(stream.Position, Is.Zero);
        Assert.That(nextCalls, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1048577)]
    public void InvalidBodyLimit_IsRejected(int limit)
    {
        var options = new RequestDiagnosticsOptions { MaxBodyBytes = limit };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RequestDiagnosticsMiddleware(_ => Task.CompletedTask, new RecordingLogger(), options));
    }

    [Test]
    public async Task OptionsAreCopiedAndAdditionalSensitiveNamesAreHonoured()
    {
        var options = BodyOptions();
        options.BodyFieldNames.Add("name");
        options.AdditionalSensitiveNames.Add("name");
        var logger = new RecordingLogger();
        var middleware = new RequestDiagnosticsMiddleware(_ => Task.CompletedTask, logger, options);
        options.Enabled = false;
        options.AdditionalSensitiveNames.Clear();

        await middleware.InvokeAsync(Context(new MemoryStream(Encoding.UTF8.GetBytes(Json))));

        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("body").GetProperty("name").GetString(), Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public async Task MetadataValuesAreBoundedAndUnapprovedHeadersAreAbsent()
    {
        var context = Context(new MemoryStream());
        context.Request.Headers["X-Trace"] = "abcdefgh";
        context.Request.Headers["X-Unapproved"] = "synthetic-private";
        var options = new RequestDiagnosticsOptions { CaptureHeaders = true, MaxValueCharacters = 4 };
        options.HeaderNames.Add("X-Trace");
        var logger = new RecordingLogger();

        await new RequestDiagnosticsMiddleware(_ => Task.CompletedTask, logger, options).InvokeAsync(context);

        using var output = Snapshot(logger);
        Assert.That(output.RootElement.GetProperty("headers").GetProperty("X-Trace").GetString(), Is.EqualTo("abcd[TRUNCATED]"));
        Assert.That(logger.Messages.Single(), Does.Not.Contain("synthetic-private"));
    }

    [Test]
    public async Task ExtensionRegistersRunnableMiddlewareWithDefaults()
    {
        var logger = new RecordingLogger();
        var services = new ServiceCollection().AddSingleton<ILogger<RequestDiagnosticsMiddleware>>(logger).BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseRequestDiagnostics();
        app.Run(ctx => { ctx.Response.StatusCode = 202; return Task.CompletedTask; });
        var context = Context(new MemoryStream());

        await app.Build()(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(202));
        Assert.That(logger.Messages, Has.Count.EqualTo(1));
        await services.DisposeAsync();
    }

    private static RequestDiagnosticsOptions BodyOptions()
    {
        var options = new RequestDiagnosticsOptions { CaptureBody = true };
        options.BodyFieldNames.Add("name");
        return options;
    }

    private static DefaultHttpContext Context(Stream stream)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = stream;
        return context;
    }

    private static JsonDocument Snapshot(RecordingLogger logger) =>
        JsonDocument.Parse(logger.Messages.Single()["Request diagnostics: ".Length..]);

    private sealed class RecordingLogger : ILogger<RequestDiagnosticsMiddleware>
    {
        public bool Enabled { get; set; } = true;
        public bool ThrowOnWrite { get; set; }
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("synthetic logger failure");
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NonSeekableStream(string text) : Stream
    {
        private readonly MemoryStream _inner = new(Encoding.UTF8.GetBytes(text));
        public int BytesRead { get; private set; }
        public bool FailFirstRead { get; set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (FailFirstRead) { FailFirstRead = false; throw new IOException("synthetic-reader-failure"); }
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailFirstRead) { FailFirstRead = false; throw new IOException("synthetic-reader-failure"); }
            var read = _inner.Read(buffer.Span);
            BytesRead += read;
            return ValueTask.FromResult(read);
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
