using NobreakMonitor.Service.Ipc;
using NobreakMonitor.Service.Shutdown;
using NobreakMonitor.Service.Storage;
using NobreakMonitor.Service.Ups;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = "NobreakMonitor");

builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<ISystemShutdown, WindowsShutdown>();
builder.Services.AddSingleton<UpsMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpsMonitor>());
builder.Services.AddHostedService<PipeServer>();

var host = builder.Build();
host.Run();
