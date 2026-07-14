using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using VideoUploadFunctions.Models;

namespace VideoUploadFunctions.Functions;

public class VideoValidationFunction
{
	private readonly ILogger<VideoValidationFunction> _logger;
	private readonly ServiceBusClient _serviceBusClient;
	private readonly BlobServiceClient _blobServiceClient;

	public VideoValidationFunction(
		ILogger<VideoValidationFunction> logger,
		ServiceBusClient serviceBusClient,
		BlobServiceClient blobServiceClient)
	{
		_logger = logger;
		_serviceBusClient = serviceBusClient;
		_blobServiceClient = blobServiceClient;
	}

	[Function(nameof(VideoValidationFunction))]
	public async Task Run(
		[ServiceBusTrigger("%VideoUploadedQueueName%", Connection = "ServiceBusConnection")]
		ServiceBusReceivedMessage message,
		ServiceBusMessageActions messageActions)
	{
		_logger.LogInformation("VideoValidationFunction triggered for message ID: {MessageId}", message.MessageId);

		VideoMessage? videoMessage = null;

		try
		{
			videoMessage = JsonSerializer.Deserialize<VideoMessage>(message.Body.ToString());

			if (videoMessage is null)
			{
				_logger.LogError("Failed to deserialize video message.");
				await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "DeserializationFailed");
				return;
			}

			_logger.LogInformation("Validating video: {BlobName}", videoMessage.BlobName);

			// Validate that the blob exists and is an .mp4 file
			var containerClient = _blobServiceClient.GetBlobContainerClient(videoMessage.ContainerName);
			var blobClient = containerClient.GetBlobClient(videoMessage.BlobName);

			if (!await blobClient.ExistsAsync())
			{
				_logger.LogWarning("Blob {BlobName} does not exist in container {Container}.", videoMessage.BlobName, videoMessage.ContainerName);
				await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "BlobNotFound");
				return;
			}

			var properties = await blobClient.GetPropertiesAsync();
			var contentType = properties.Value.ContentType;

			if (!string.Equals(contentType, "video/mp4", StringComparison.OrdinalIgnoreCase) &&
				!videoMessage.OriginalFileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
			{
				_logger.LogWarning("File {BlobName} is not a valid MP4 video. ContentType: {ContentType}", videoMessage.BlobName, contentType);
				await blobClient.DeleteIfExistsAsync();
				await messageActions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidFileType");
				return;
			}

			_logger.LogInformation("Video {BlobName} passed validation. Sending to video-validated queue.", videoMessage.BlobName);

			// Forward to the validated queue so thumbnail & email functions can pick it up
			var validatedQueueName = Environment.GetEnvironmentVariable("VideoValidatedQueueName") ?? "video-validated";
			var sender = _serviceBusClient.CreateSender(validatedQueueName);

			var outgoingMessage = new ServiceBusMessage(JsonSerializer.Serialize(videoMessage))
			{
				ContentType = "application/json",
				MessageId = Guid.NewGuid().ToString(),
				Subject = "VideoValidated"
			};

			await sender.SendMessageAsync(outgoingMessage);

			await messageActions.CompleteMessageAsync(message);
			_logger.LogInformation("Video {BlobName} validation complete.", videoMessage.BlobName);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error validating video message {MessageId}", message.MessageId);
			await messageActions.AbandonMessageAsync(message);
		}
	}
}
