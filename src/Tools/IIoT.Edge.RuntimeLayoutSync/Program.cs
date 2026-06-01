using Microsoft.Extensions.DependencyInjection;

var options = CommandLineOptions.Parse(args);
var services = new ServiceCollection();
services.AddSingleton<IRuntimeLayoutSyncFileSystem, RuntimeLayoutSyncFileSystem>();
services.AddSingleton<IRuntimeLayoutSyncValidation, RuntimeLayoutSyncValidation>();
services.AddSingleton<IRuntimeLayoutSyncModulePublisher, RuntimeLayoutSyncModulePublisher>();
services.AddSingleton<IRuntimeLayoutSyncApp, RuntimeLayoutSyncApp>();

using var provider = services.BuildServiceProvider();
var app = provider.GetRequiredService<IRuntimeLayoutSyncApp>();
var layoutRoot = app.Run(options);
Console.WriteLine($"Synchronized local runtime layout: {layoutRoot}");
