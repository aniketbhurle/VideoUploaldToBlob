using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
	.ConfigureFunctionsWorkerDefaults()
	.ConfigureServices(services =>
	{
		var serviceBusConnection = Environment.GetEnvironmentVariable("ServiceBusConnection")
			?? throw new InvalidOperationException("ServiceBusConnection is not configured.");
		var storageConnection = Environment.GetEnvironmentVariable("StorageConnection")
			?? throw new InvalidOperationException("StorageConnection is not configured.");

		services.AddSingleton(new ServiceBusClient(serviceBusConnection));
		services.AddSingleton(new BlobServiceClient(storageConnection));
	})
	.Build();

host.Run();
