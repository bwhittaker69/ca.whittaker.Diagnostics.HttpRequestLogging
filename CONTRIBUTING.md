# Contributing

Use the .NET 10 SDK and .NET 9/10 ASP.NET Core runtimes. Tests use NUnit and Arrange-Act-Assert.

```powershell
dotnet build ca.whittaker.Diagnostics.HttpRequestLogging.sln -c Release
dotnet test ca.whittaker.Diagnostics.HttpRequestLogging.sln -c Release
dotnet pack ca.whittaker.Diagnostics.HttpRequestLogging.sln -c Release -o artifacts
dotnet restore samples/HttpDiagnostics.Sample --source "$PWD/artifacts"
dotnet run --project samples/HttpDiagnostics.Sample -c Release --no-restore -- --self-test
```

The sample references a packed package, not the source project. Update its pinned version, README installation command and changelog when releasing. The publishing workflow reads package identity/version from the project; it does not choose or increment a version.

Open a focused issue or pull request with synthetic reproductions, tests and documentation. Never attach real request credentials or private application data. Contributions must be compatible with GPL-3.0-only.
