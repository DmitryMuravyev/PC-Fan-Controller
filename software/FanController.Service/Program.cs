////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// Program.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtoBuf.Grpc.Server;
using Serilog;
using Serilog.Templates;
using FanController.Contracts;
using FanController.Service;
using FanController.Service.Core;


// Log files:
//   Linux   : /var/log/fancontroller/fancontroller-YYYYMMDD.log
//   Windows : C:\ProgramData\FanController\Logs\fancontroller-YYYYMMDD.log
//   macOS   : /Library/Logs/FanController/fancontroller-YYYYMMDD.log
//   Fallback: <application directory>/logs (e.g. when not started as a service)

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    var loggingSection = context.Configuration.GetSection("Service:Logging");

    var rollingInterval = loggingSection.GetValue("RollingInterval", RollingInterval.Day);
    var retainedFileCountLimit = loggingSection.GetValue("RetainedFileCountLimit", 30);

    var fileTemplate = new ExpressionTemplate(
        "{@t:yyyy-MM-dd HH:mm:ss.fff} [{@l:u3}] " +
        "{SourceContext}" +
        "{#if SN is not null} [{SN}]{#end}" +
        ": {@m}{#if @x is not null}\n{@x}{#end}\n");

    // Minimum levels and overrides come from appsettings.json ("Serilog" section)
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.File(
            formatter: fileTemplate,
            path: GetLogPath(),
            rollingInterval: rollingInterval,
            retainedFileCountLimit: retainedFileCountLimit);

#if DEBUG
    var debugTemplate = new ExpressionTemplate(
        "[{@l:u3}] {SourceContext}" +
        "{#if SN is not null} [{SN}]{#end}" +
        ": {@m}{#if @x is not null}\n{@x}{#end}\n");

    loggerConfig.WriteTo.Debug(formatter: debugTemplate);
#endif
});

// The endpoints are defined explicitly below
// (no default http://localhost:5000 / https://localhost:5001)
builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, string.Empty);

// Windows Service / Linux systemd integration (no-op when started from a console)
builder.Services.AddWindowsService(options => options.ServiceName = "FanController");
builder.Services.AddSystemd();
builder.Services.AddCodeFirstGrpc();
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(15));

builder.WebHost.ConfigureKestrel((context, options) =>
{
    var serviceSection = context.Configuration.GetSection("Service");

    // HTTP/2 connection-level options
    options.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
    options.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    // TCP endpoint: local and remote connections, without TLS
    int port = serviceSection.GetValue("GrpcPort", 51515);
    options.ListenAnyIP(port, o => o.Protocols = HttpProtocols.Http2);
});

// Single instance: the concrete type and both interface views resolve to it
builder.Services.AddSingleton<FanControllerManager>();
builder.Services.AddSingleton<IManagedDeviceService>(
    sp => sp.GetRequiredService<FanControllerManager>());
builder.Services.AddSingleton<IDeviceService>(
    sp => sp.GetRequiredService<FanControllerManager>());

// The host creates the manager at startup (devices are opened without waiting for
// the first gRPC call) and stops it on shutdown
// (hosted services are stopped in the reverse order of registration).
builder.Services.AddHostedService(sp => sp.GetRequiredService<FanControllerManager>());
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.MapGrpcService<FanControllerManager>();
app.Run();


////////////////////////////////////////////////////////////////////////////////////////
// Helpers

// Platform log directory, or the application directory if it can't be created
static string GetLogPath()
{
    const string FileName = "fancontroller-.log";

    string? logDir = null;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        logDir = "/var/log/fancontroller";
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FanController",
            "Logs");
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        logDir = "/Library/Logs/FanController";

    if (logDir is not null)
    {
        try
        {
            Directory.CreateDirectory(logDir);
            return Path.Combine(logDir, FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No permission (e.g. not started as a service): use the fallback
        }
    }

    return Path.Combine(AppContext.BaseDirectory, "logs", FileName);
}

// END-OF-FILE
