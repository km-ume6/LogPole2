using LP2DTP.Common.Services;
using LP2SVR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var serviceName = builder.Configuration["ServiceName"];

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = string.IsNullOrWhiteSpace(serviceName)
        ? "LP2SVR"
        : serviceName;
});

builder.Services.AddSingleton<AppSettingsService>();
builder.Services.AddSingleton<VisaItemService>();
builder.Services.AddSingleton<ModbusItemService>();
builder.Services.AddSingleton<PollingWorkerManager>();
builder.Services.AddHostedService<PollingHostedService>();

var host = builder.Build();
await host.RunAsync();
