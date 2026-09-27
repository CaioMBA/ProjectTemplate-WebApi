using CrossCutting.Services;
using CrossCutting.Setup;
using Data.Sql.Setup;
using Observability.Setup;
using WebApi.Middlewares;
using WebApi.Setup;

var builder = WebApplication.CreateBuilder(args);

var environmentAccessor = new SystemEnvironmentAccessor();
var systemInfo = new SystemInfo(environmentAccessor);
var secretResolver = builder.Configuration.CreateSecretResolver();

var startup = builder.Configuration.GetStartupSettings(
    builder.Environment.EnvironmentName,
    environmentAccessor,
    secretResolver);

var appSettings = startup.Settings;
var apiOptions = startup.Api;

builder.AddObservabilitySetup(startup, environmentAccessor, systemInfo);

using var startupLoggerFactory = LoggerFactory.Create(logging =>
    logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole());

var startupLogger = startupLoggerFactory.CreateLogger("Startup");

builder.Services.AddCrossCuttingSetup(
    builder.Configuration,
    startup,
    startupLogger);

builder.Services
    .AddControllersSetup()
    .AddVersioningSetup()
    .AddSwaggerSetup(apiOptions)
    .AddScalarSetup(apiOptions)
    .AddGraphQlSetup(apiOptions, appSettings)
    .AddCorsSetup(apiOptions)
    .AddRateLimitSetup(apiOptions)
    .AddProblemDetailsSetup()
    .AddForwardedHeadersSetup(apiOptions);

var app = builder.Build();

await app.Services.UseDataSqlSetupAsync(appSettings).ConfigureAwait(false);

if (!string.IsNullOrWhiteSpace(appSettings.PathBase))
{
    app.UsePathBase(appSettings.PathBase);
}

if (apiOptions.ForwardedHeaders.Enabled)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();

app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseRouting();

app.UseCorsSetupPipeline(apiOptions);

if (apiOptions.RateLimit.Enabled)
{
    app.UseRateLimiter();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseObservabilitySetupPipeline();

app.UseCrossCuttingSetupPipeline();

app.UseSwaggerSetupPipeline(apiOptions);
app.UseScalarSetupPipeline(apiOptions);

app.MapControllers();

app.UseGraphQlSetupPipeline(apiOptions);

await app.RunAsync().ConfigureAwait(false);

public partial class Program
{
    protected Program()
    {
    }
}
