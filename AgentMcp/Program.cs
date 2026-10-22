using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Voidwright.AgentMcp;

if (args.Length > 0 && args[0] == "--local-vehicle-test")
{
    await LocalVehicleTestOperator.RunAsync(args.Skip(1).ToArray());
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<SnapshotFileReader>();
builder.Services.AddSingleton<LabTrialRunner>();
builder.Services.AddSingleton<PerspectiveStore>();
builder.Services.AddSingleton<HoloViewCaptureClient>();
builder.Services.AddSingleton<AgentMcpTools>();
builder.Services.AddSingleton<VehicleObservationTools>();
builder.Services.AddSingleton<PerspectiveMcpTools>();
builder.Services.AddSingleton<IVerifiedVehiclePrincipalProvider, NoVerifiedVehiclePrincipalProvider>();
builder.Services.AddSingleton(serviceProvider => new LiveVehicleRuntime(
    serviceProvider.GetRequiredService<IVerifiedVehiclePrincipalProvider>(), _ => null));
builder.Services.AddSingleton<LiveVehicleMcpTools>();
var profile = Environment.GetEnvironmentVariable("VOIDWRIGHT_TOOL_PROFILE") ?? "full";
if (profile is not ("full" or "vehicle")) throw new InvalidOperationException("VOIDWRIGHT_TOOL_PROFILE must be full or vehicle.");
var mcpServer = builder.Services.AddMcpServer()
    .WithStdioServerTransport();
if (profile == "vehicle") mcpServer.WithTools<VehicleWorkflowTools>();
else mcpServer.WithTools<AgentMcpTools>().WithTools<VehicleObservationTools>().WithTools<PerspectiveMcpTools>();

// Interactive gameplay should expose observation only. The bounded destructive-lab
// driving tool is registered only when the operator has not explicitly requested
// a read-only MCP surface.
if (!string.Equals(Environment.GetEnvironmentVariable("VOIDWRIGHT_READ_ONLY_ONLY"), "true",
        StringComparison.OrdinalIgnoreCase))
{
    mcpServer.WithTools<LabMcpTools>();
    if (profile == "full")
    {
        mcpServer.WithTools<LegacyLabMcpTools>();
        mcpServer.WithTools<LiveVehicleMcpTools>();
    }
}

await builder.Build().RunAsync();
