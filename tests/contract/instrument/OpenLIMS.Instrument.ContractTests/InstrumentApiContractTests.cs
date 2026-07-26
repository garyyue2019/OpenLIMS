using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenLIMS.Contracts.Instrument;
using OpenLIMS.Modules.Instrument;
using Xunit;

namespace OpenLIMS.Instrument.ContractTests;

[Trait("Profile", "instrument")]
public sealed class InstrumentApiContractTests
{
    private const string FileId = "00000000000000000000000000000090";
    private const string ExceptionId = "00000000000000000000000000000091";
    private static readonly string ValidHash = new('a', 64);

    [Fact]
    public async Task Five_instrument_operations_expose_versioned_contracts()
    {
        using var factory = new InstrumentApiFactory();
        using var client = factory.CreateClient();
        using var registered = await client.PostAsJsonAsync(
            InstrumentContract.RegisterFilePath, Registration(), TestContext.Current.CancellationToken);
        using var rows = await client.PostAsJsonAsync(
            $"/api/v1/instrument-files/{FileId}/rows",
            new SubmitInstrumentRowsRequest(1, InstrumentContract.RuleSetVersion, [Row(1)]),
            TestContext.Current.CancellationToken);
        using var resolved = await client.PostAsJsonAsync(
            $"/api/v1/instrument-files/{FileId}/exceptions/{ExceptionId}/resolution",
            new ResolveImportExceptionRequest(
                2, InstrumentContract.RuleSetVersion, InstrumentResolutionKinds.RejectRow, "operator rejected the row"),
            TestContext.Current.CancellationToken);
        using var read = await client.GetAsync(
            $"/api/v1/instrument-files/{FileId}", TestContext.Current.CancellationToken);
        using var status = await client.GetAsync(
            $"/api/v1/instrument-files/{FileId}/import-status?expectedFileVersion=2&ruleSetVersion={Uri.EscapeDataString(InstrumentContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rows.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resolved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var gate = await status.Content.ReadFromJsonAsync<InstrumentImportStatusResult>(TestContext.Current.CancellationToken);
        Assert.NotNull(gate);
        Assert.Equal(InstrumentStatusDecisions.Allowed, gate.Decision);
        Assert.Equal(InstrumentContract.RuleSetVersion, gate.RuleSetVersion);
    }

    [Theory]
    [InlineData(InstrumentErrorCodes.NotAuthorized, HttpStatusCode.Forbidden)]
    [InlineData(InstrumentErrorCodes.ObjectNotAccessible, HttpStatusCode.NotFound)]
    [InlineData(InstrumentErrorCodes.DuplicateFile, HttpStatusCode.Conflict)]
    [InlineData(InstrumentErrorCodes.ExceptionAlreadyResolved, HttpStatusCode.Conflict)]
    [InlineData(InstrumentErrorCodes.ExpectedVersionConflict, HttpStatusCode.Conflict)]
    [InlineData(InstrumentErrorCodes.ValidationFailed, HttpStatusCode.BadRequest)]
    [InlineData(InstrumentErrorCodes.PersistenceUnavailable, HttpStatusCode.ServiceUnavailable)]
    public async Task Instrument_errors_map_to_stable_problem_contracts(string errorCode, HttpStatusCode status)
    {
        using var factory = new InstrumentApiFactory(errorCode);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            InstrumentContract.RegisterFilePath, Registration(), TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains(errorCode, content, StringComparison.Ordinal);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }

    [Fact]
    public async Task Malformed_status_query_is_rejected()
    {
        using var factory = new InstrumentApiFactory();
        using var client = factory.CreateClient();
        using var missingBoth = await client.GetAsync(
            $"/api/v1/instrument-files/{FileId}/import-status", TestContext.Current.CancellationToken);
        using var missingVersion = await client.GetAsync(
            $"/api/v1/instrument-files/{FileId}/import-status?ruleSetVersion={Uri.EscapeDataString(InstrumentContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);
        using var zeroVersion = await client.GetAsync(
            $"/api/v1/instrument-files/{FileId}/import-status?expectedFileVersion=0&ruleSetVersion={Uri.EscapeDataString(InstrumentContract.RuleSetVersion)}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingBoth.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zeroVersion.StatusCode);
    }

    [Fact]
    public async Task Openapi_declares_all_instrument_operations()
    {
        using var factory = new InstrumentApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(InstrumentContract.RegisterFilePath, content, StringComparison.Ordinal);
        foreach (var operation in new[]
        {
            "registerInstrumentFile", "submitInstrumentRows", "resolveInstrumentImportException",
            "getInstrumentFile", "getInstrumentImportStatus"
        })
        {
            Assert.Contains(operation, content, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// PRD §22-15: instrument integration acceptance compares the approved
    /// validation dataset field by field — raw value, parsed value, unit,
    /// qualifier, sample/batch mapping and exception handling must match at
    /// a 100% rate.
    /// </summary>
    [Fact]
    public async Task Approved_validation_dataset_matches_field_by_field_at_full_rate()
    {
        using var factory = new InstrumentApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            $"/api/v1/instrument-files/{FileId}/rows",
            new SubmitInstrumentRowsRequest(1, InstrumentContract.RuleSetVersion, ValidationDataset),
            TestContext.Current.CancellationToken);
        var file = await response.Content.ReadFromJsonAsync<InstrumentFileResult>(TestContext.Current.CancellationToken);

        Assert.NotNull(file);
        var expectedRows = ValidationDataset
            .Where(row => ExpectedExceptionRows.All(entry => entry.RowNumber != row.RowNumber))
            .ToList();
        Assert.Equal(expectedRows.Count, file.Rows.Count);
        Assert.Equal(ExpectedExceptionRows.Count, file.Exceptions.Count);

        var comparisons = 0;
        foreach (var expected in expectedRows)
        {
            var actual = Assert.Single(file.Rows, row => row.RowNumber == expected.RowNumber);
            Assert.Equal(expected.RawValue, actual.RawValue);
            Assert.Equal(expected.ParsedValue, actual.ParsedValue);
            Assert.Equal(expected.Unit, actual.Unit);
            Assert.Equal(expected.Qualifier, actual.Qualifier);
            Assert.Equal(expected.SampleNumber, actual.SampleNumber);
            Assert.Equal(expected.BatchPosition, actual.BatchPosition);
            Assert.Equal(expected.Parameter, actual.Parameter);
            comparisons += 7;
        }

        foreach (var (rowNumber, reasonCode) in ExpectedExceptionRows)
        {
            var actual = Assert.Single(file.Exceptions, entry => entry.RowNumber == rowNumber);
            var source = Assert.Single(ValidationDataset, row => row.RowNumber == rowNumber);
            Assert.Equal(reasonCode, actual.ReasonCode);
            Assert.Equal(InstrumentExceptionStates.Pending, actual.State);
            Assert.Contains(source.RawValue, actual.RawContent, StringComparison.Ordinal);
            comparisons += 3;
        }

        Assert.Equal((expectedRows.Count * 7) + (ExpectedExceptionRows.Count * 3), comparisons);
    }

    private static readonly IReadOnlyList<InstrumentRowInput> ValidationDataset =
    [
        new(1, "SPEC-1", "POS-1", "TENSILE-STRENGTH", "NEWTON", null, "83.4", "83.4"),
        new(2, "SPEC-2", "POS-2", "TENSILE-STRENGTH", "NEWTON", "LESS-THAN", "<0.50", "0.50"),
        new(3, "SPEC-3", "POS-3", "SMALL-PARTS", "MILLIMETRE", "GREATER-THAN", ">31.7", "31.7"),
        new(4, "unknown sample", "POS-4", "TENSILE-STRENGTH", "NEWTON", null, "77.1", "77.1"),
        new(5, "SPEC-5", "POS-5", "TENSILE-STRENGTH", "newton force", null, "62.0", "62.0")
    ];

    private static readonly IReadOnlyList<(int RowNumber, string ReasonCode)> ExpectedExceptionRows =
    [
        (4, InstrumentExceptionReasons.UnknownSample),
        (5, InstrumentExceptionReasons.IllegalUnit)
    ];

    private static RegisterInstrumentFileRequest Registration() => new(
        InstrumentContract.RuleSetVersion,
        new InstrumentObjectContext("LEGAL-A", "LAB-A"),
        new InstrumentVersionedReference("EXPORT-7", 1),
        ValidHash,
        InstrumentSourceSystems.Instrument,
        new InstrumentVersionedReference("TENSILE-1", 2),
        "PARSER-1.4",
        5);

    private static InstrumentRowInput Row(int rowNumber) => new(
        rowNumber, $"SPEC-{rowNumber}", $"POS-{rowNumber}", "TENSILE-STRENGTH", "NEWTON", null, "83.4", "83.4");
}

internal sealed class InstrumentApiFactory(string? errorCode = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Platform:OrganizationGroupId", "test-group");
        builder.UseSetting("Platform:PostgresConnectionString", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test;Timeout=1");
        builder.UseSetting("Platform:OidcAuthority", "https://issuer.invalid/");
        builder.UseSetting("Platform:OidcAudience", "openlims-api");
        builder.UseSetting("Platform:ObjectStorageEndpoint", "http://127.0.0.1:1");
        builder.UseSetting("Platform:ObjectStorageBucket", "test");
        builder.UseSetting("Platform:ObjectStorageAccessKey", "test-access");
        builder.UseSetting("Platform:ObjectStorageSecretKey", "test-secret");
        builder.UseSetting("Platform:PostgresCommandTimeoutSeconds", "1");
        builder.UseSetting("Platform:OidcMetadataTimeoutSeconds", "1");
        builder.UseSetting("Platform:ObjectStorageProbeTimeoutSeconds", "1");
        builder.UseSetting("Platform:DependencyProbeTimeoutSeconds", "2");
        builder.UseSetting("Platform:AllowInsecureDevelopmentObjectStorage", "true");
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = InstrumentTestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = InstrumentTestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, InstrumentTestAuthenticationHandler>(
                    InstrumentTestAuthenticationHandler.SchemeName, _ => { });
            services.RemoveAll<IInstrumentImportService>();
            services.RemoveAll<IInstrumentImportPort>();
            services.AddSingleton<IInstrumentImportService>(new StubInstrumentImportService(errorCode));
            services.AddSingleton<IInstrumentImportPort>(new StubInstrumentImportPort(errorCode));
        });
    }
}

/// <summary>
/// Contract-level stub: it applies the real classification rules so the HTTP
/// surface and the validation-dataset comparison exercise production semantics
/// without a database.
/// </summary>
internal sealed class StubInstrumentImportService(string? errorCode) : IInstrumentImportService
{
    private const string FileId = "00000000000000000000000000000090";
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

    public Task<InstrumentFileResult> RegisterFileAsync(
        RegisterInstrumentFileRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(File(request, [], []));
    }

    public Task<InstrumentFileResult> SubmitRowsAsync(
        string fileRegistrationId, SubmitInstrumentRowsRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        var (valid, exceptions) = InstrumentRules.ClassifyRows(request, new HashSet<int>(), new HashSet<int>());
        var rows = valid.Select((row, index) => new InstrumentParsedRowResult(
            $"row-{index + 1}", fileRegistrationId, row.RowNumber, row.SampleNumber, row.BatchPosition,
            row.Parameter, row.Unit, row.Qualifier, row.RawValue, row.ParsedValue, "PARSER-1.4",
            "contract-actor", Now)).ToList();
        var queued = exceptions.Select((entry, index) => new InstrumentImportExceptionResult(
            $"exception-{index + 1}", fileRegistrationId, entry.Row.RowNumber, entry.ReasonCode,
            $"{entry.Row.SampleNumber}|{entry.Row.BatchPosition}|{entry.Row.Parameter}|{entry.Row.Unit}|{entry.Row.Qualifier}|{entry.Row.RawValue}|{entry.Row.ParsedValue}",
            InstrumentExceptionStates.Pending, null)).ToList();
        return Task.FromResult(File(null, rows, queued));
    }

    public Task<InstrumentFileResult> ResolveExceptionAsync(
        string fileRegistrationId, string exceptionId, ResolveImportExceptionRequest request, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        InstrumentRules.ValidateResolution(request);
        return Task.FromResult(File(null, [], [new InstrumentImportExceptionResult(
            exceptionId, fileRegistrationId, 4, InstrumentExceptionReasons.UnknownSample, "raw",
            InstrumentExceptionStates.Resolved,
            new InstrumentExceptionResolutionResult(
                "resolution-1", exceptionId, request.Kind, request.CorrectedMapping,
                request.Reason, "contract-actor", Now))]));
    }

    public Task<InstrumentFileResult> GetAsync(
        string fileRegistrationId, string correlationId, CancellationToken cancellationToken = default)
    {
        Throw(cancellationToken);
        return Task.FromResult(File(null, [], []));
    }

    private static InstrumentFileResult File(
        RegisterInstrumentFileRequest? request,
        IReadOnlyList<InstrumentParsedRowResult> rows,
        IReadOnlyList<InstrumentImportExceptionResult> exceptions) => new(
        FileId,
        1 + rows.Count + exceptions.Count,
        exceptions.Any(entry => entry.State == InstrumentExceptionStates.Pending)
            ? InstrumentFileStates.Blocked
            : InstrumentFileStates.Ingested,
        InstrumentContract.RuleSetVersion,
        request?.ObjectScope ?? new InstrumentObjectContext("LEGAL-A", "LAB-A"),
        request?.ExternalRef ?? new InstrumentVersionedReference("EXPORT-7", 1),
        request?.Sha256 ?? new string('a', 64),
        request?.SourceSystem ?? InstrumentSourceSystems.Instrument,
        request?.InstrumentRef ?? new InstrumentVersionedReference("TENSILE-1", 2),
        request?.ParserVersion ?? "PARSER-1.4",
        request?.DeclaredRowCount ?? 5,
        rows,
        exceptions,
        "contract-actor",
        Now);

    private void Throw(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new InstrumentDomainException(errorCode);
    }
}

internal sealed class StubInstrumentImportPort(string? errorCode) : IInstrumentImportPort
{
    public ValueTask<InstrumentImportStatusResult> EvaluateAsync(
        InstrumentImportStatusRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errorCode is not null) throw new InstrumentDomainException(errorCode);
        return ValueTask.FromResult(new InstrumentImportStatusResult(
            InstrumentStatusDecisions.Allowed, [], request.FileRegistrationId,
            request.ExpectedFileVersion, 5, 0, InstrumentContract.RuleSetVersion));
    }
}

internal sealed class InstrumentTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OpenLIMS.Instrument.ContractTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims = [new("sub", "contract-actor"), new("organization_group", "test-group")];
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
