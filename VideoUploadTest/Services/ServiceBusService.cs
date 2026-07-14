using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace VideoUploadTest.Services;

public interface IServiceBusService
{
	Task SendVideoUploadedMessageAsync(VideoUploadMessage message, CancellationToken cancellationToken = default);
}

public record VideoUploadMessage(
	string BlobName,
	string OriginalFileName,
	string UserEmail,
	string UserName,
	long FileSizeBytes,
	DateTime UploadedAt,
	string ContainerName);

public class ServiceBusService : IServiceBusService, IAsyncDisposable
{
	private readonly ServiceBusClient _client;
	private readonly ServiceBusSender _sender;
	private readonly ILogger<ServiceBusService> _logger;

	public ServiceBusService(IConfiguration configuration, ILogger<ServiceBusService> logger)
	{
		_logger = logger;

		var connectionString = configuration["AzureServiceBus:ConnectionString"]
			?? throw new InvalidOperationException("AzureServiceBus:ConnectionString is not configured.");
		var queueName = configuration["AzureServiceBus:VideoUploadedQueueName"] ?? "video-uploaded";

		_client = new ServiceBusClient(connectionString);
		_sender = _client.CreateSender(queueName);
	}

	public async Task SendVideoUploadedMessageAsync(VideoUploadMessage message, CancellationToken cancellationToken = default)
	{
		_logger.LogInformation("Sending Service Bus message for blob {BlobName}", message.BlobName);

		var json = JsonSerializer.Serialize(message);
		var sbMessage = new ServiceBusMessage(json)
		{
			ContentType = "application/json",
			MessageId = Guid.NewGuid().ToString(),
			Subject = "VideoUploaded"
		};

		await _sender.SendMessageAsync(sbMessage, cancellationToken);
		_logger.LogInformation("Service Bus message sent for blob {BlobName}", message.BlobName);
	}

	public async ValueTask DisposeAsync()
	{
		await _sender.DisposeAsync();
		await _client.DisposeAsync();
	}
}
