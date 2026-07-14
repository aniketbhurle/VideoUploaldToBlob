using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text.Json;
using VideoUploadFunctions.Models;

namespace VideoUploadFunctions.Functions;

public class ThumbnailFunction
{
	private readonly ILogger<ThumbnailFunction> _logger;
	private readonly BlobServiceClient _blobServiceClient;
	private readonly ServiceBusClient _serviceBusClient;

	public ThumbnailFunction(
		ILogger<ThumbnailFunction> logger,
		BlobServiceClient blobServiceClient,
		ServiceBusClient serviceBusClient)
	{
		_logger = logger;
		_blobServiceClient = blobServiceClient;
		_serviceBusClient = serviceBusClient;
	}

	[Function(nameof(ThumbnailFunction))]
	public async Task Run(
		[ServiceBusTrigger("%VideoValidatedQueueName%", Connection = "ServiceBusConnection")]
		ServiceBusReceivedMessage message,
		ServiceBusMessageActions messageActions)
	{
		_logger.LogInformation("ThumbnailFunction triggered for message ID: {MessageId}", message.MessageId);

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

			_logger.LogInformation("Generating thumbnail for: {BlobName}", videoMessage.BlobName);

			var thumbnailContainerName = Environment.GetEnvironmentVariable("ThumbnailContainerName") ?? "thumbnails";
			var thumbnailContainer = _blobServiceClient.GetBlobContainerClient(thumbnailContainerName);
			await thumbnailContainer.CreateIfNotExistsAsync(PublicAccessType.None);

			var thumbnailBlobName = Path.GetFileNameWithoutExtension(videoMessage.BlobName) + "_thumbnail.png";
			var thumbnailBlobClient = thumbnailContainer.GetBlobClient(thumbnailBlobName);

			using var thumbnailStream = GenerateTitleCardThumbnail(videoMessage);
				await thumbnailBlobClient.UploadAsync(
					thumbnailStream,
					new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" } });

			_logger.LogInformation("Thumbnail saved to {Container}/{BlobName}", thumbnailContainerName, thumbnailBlobName);

			var processedQueueName = Environment.GetEnvironmentVariable("VideoProcessedQueueName") ?? "video-processed";
			var processedSender = _serviceBusClient.CreateSender(processedQueueName);

			var processedMessage = new ServiceBusMessage(JsonSerializer.Serialize(videoMessage))
			{
				ContentType = "application/json",
				MessageId = Guid.NewGuid().ToString(),
				Subject = "VideoProcessed"
			};

			await processedSender.SendMessageAsync(processedMessage);
			await processedSender.DisposeAsync();

			await messageActions.CompleteMessageAsync(message);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error generating thumbnail for message {MessageId}", message.MessageId);
			await messageActions.AbandonMessageAsync(message);
		}
	}

	private static Stream GenerateTitleCardThumbnail(VideoMessage videoMessage)
	{
		const int width = 1280;
		const int height = 720;

		FontFamily fontFamily;
		if (!SystemFonts.TryGet("Arial", out fontFamily) &&
			!SystemFonts.TryGet("DejaVu Sans", out fontFamily) &&
			!SystemFonts.TryGet("Liberation Sans", out fontFamily))
		{
			fontFamily = SystemFonts.Families.First();
		}

		var titleFont = fontFamily.CreateFont(42, FontStyle.Bold);
		var subtitleFont = fontFamily.CreateFont(22, FontStyle.Regular);
		var metaFont = fontFamily.CreateFont(18, FontStyle.Regular);

		using var image = new Image<Rgba32>(width, height);

		image.Mutate(ctx =>
		{
			// Dark background
			ctx.Fill(Color.FromRgb(20, 20, 40));

			// Top accent bar
			ctx.Fill(Color.FromRgb(0, 120, 215), new RectangleF(0, 0, width, 8));

			// Semi-transparent center panel
			ctx.Fill(Color.FromRgba(0, 60, 130, 80), new RectangleF(80, 220, width - 160, 280));

			// Bottom accent bar
			ctx.Fill(Color.FromRgb(0, 120, 215), new RectangleF(0, height - 8, width, 8));

			var fileName = Path.GetFileNameWithoutExtension(videoMessage.OriginalFileName);
			var displayName = fileName.Length > 40 ? fileName[..40] + "…" : fileName;
			var uploadedAt = videoMessage.UploadedAt.ToString("MMMM dd, yyyy HH:mm UTC");
			var fileSize = FormatFileSize(videoMessage.FileSizeBytes);

			var titleOptions = new RichTextOptions(titleFont)
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Origin = new System.Numerics.Vector2(width / 2f, 310)
			};

			var subtitleOptions = new RichTextOptions(subtitleFont)
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Origin = new System.Numerics.Vector2(width / 2f, 390)
			};

			var metaOptions = new RichTextOptions(metaFont)
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Origin = new System.Numerics.Vector2(width / 2f, 430)
			};

			ctx.DrawText(titleOptions, displayName, Color.White);
			ctx.DrawText(subtitleOptions, uploadedAt, Color.FromRgb(180, 180, 200));
			ctx.DrawText(metaOptions, fileSize, Color.FromRgb(140, 140, 160));
		});

		var ms = new MemoryStream();
		image.Save(ms, new PngEncoder());
		ms.Position = 0;
		return ms;
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
		if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
		if (bytes >= 1_024) return $"{bytes / 1_024.0:F1} KB";
		return $"{bytes} B";
	}
}
