using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Text.Json;
using VideoUploadFunctions.Models;

namespace VideoUploadFunctions.Functions;

public class EmailNotificationFunction
{
	private readonly ILogger<EmailNotificationFunction> _logger;

	public EmailNotificationFunction(ILogger<EmailNotificationFunction> logger)
	{
		_logger = logger;
	}

	[Function(nameof(EmailNotificationFunction))]
	public async Task Run(
		[ServiceBusTrigger("%VideoProcessedQueueName%", Connection = "ServiceBusConnection")]
		ServiceBusReceivedMessage message,
		ServiceBusMessageActions messageActions)
	{
		_logger.LogInformation("EmailNotificationFunction triggered for message ID: {MessageId}", message.MessageId);

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

			if (string.IsNullOrWhiteSpace(videoMessage.UserEmail))
			{
				_logger.LogWarning("No recipient email address for blob {BlobName}. Skipping email.", videoMessage.BlobName);
				await messageActions.CompleteMessageAsync(message);
				return;
			}

			var apiKey = Environment.GetEnvironmentVariable("SendGridApiKey");
			var fromEmail = Environment.GetEnvironmentVariable("NotificationFromEmail") ?? "noreply@yourdomain.com";

			if (string.IsNullOrWhiteSpace(apiKey))
			{
				_logger.LogError("SendGrid API key is not configured.");
				await messageActions.AbandonMessageAsync(message);
				return;
			}

			var client = new SendGridClient(apiKey);
			var from = new EmailAddress(fromEmail, "Video Upload Service");
			var to = new EmailAddress(videoMessage.UserEmail, videoMessage.UserName);

			var subject = $"✅ Your video \"{videoMessage.OriginalFileName}\" has been uploaded successfully!";
			var plainText = BuildPlainTextEmail(videoMessage);
			var htmlContent = BuildHtmlEmail(videoMessage);

			var msg = MailHelper.CreateSingleEmail(from, to, subject, plainText, htmlContent);
			var response = await client.SendEmailAsync(msg);

			if ((int)response.StatusCode >= 400)
			{
				_logger.LogError("SendGrid returned error status {StatusCode} for {Email}", response.StatusCode, videoMessage.UserEmail);
				await messageActions.AbandonMessageAsync(message);
				return;
			}

			_logger.LogInformation("Notification email sent to {Email} for video {BlobName}", videoMessage.UserEmail, videoMessage.BlobName);
			await messageActions.CompleteMessageAsync(message);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error sending notification email for message {MessageId}", message.MessageId);
			await messageActions.AbandonMessageAsync(message);
		}
	}

	private static string BuildPlainTextEmail(VideoMessage video)
	{
		return $"""
			Hi {video.UserName},

			Your video "{video.OriginalFileName}" has been successfully uploaded and processed.

			Details:
			- File: {video.OriginalFileName}
			- Size: {FormatFileSize(video.FileSizeBytes)}
			- Uploaded at: {video.UploadedAt:MMMM dd, yyyy HH:mm UTC}

			A thumbnail has been generated for your video.

			Thank you for using Video Upload Service!
			""";
	}

	private static string BuildHtmlEmail(VideoMessage video)
	{
		var fileSize = FormatFileSize(video.FileSizeBytes);
		var uploadedAt = video.UploadedAt.ToString("MMMM dd, yyyy HH:mm UTC");

		return $"""
			<!DOCTYPE html>
			<html>
			<head><meta charset="utf-8" /></head>
			<body style="font-family:Arial,sans-serif;background:#f5f5f5;margin:0;padding:20px;">
			  <div style="max-width:600px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.1);">
				<div style="background:#0078d4;padding:24px 32px;">
				  <h1 style="color:#fff;margin:0;font-size:22px;">✅ Video Upload Successful</h1>
				</div>
				<div style="padding:32px;">
				  <p style="font-size:16px;color:#333;">Hi <strong>{video.UserName}</strong>,</p>
				  <p style="color:#555;">Your video has been successfully uploaded, validated, and a thumbnail has been generated.</p>
				  <div style="background:#f0f7ff;border-left:4px solid #0078d4;border-radius:4px;padding:16px;margin:24px 0;">
					<table style="width:100%;border-collapse:collapse;">
					  <tr><td style="padding:6px 0;color:#666;font-size:14px;">File name</td><td style="padding:6px 0;font-weight:bold;font-size:14px;">{video.OriginalFileName}</td></tr>
					  <tr><td style="padding:6px 0;color:#666;font-size:14px;">File size</td><td style="padding:6px 0;font-size:14px;">{fileSize}</td></tr>
					  <tr><td style="padding:6px 0;color:#666;font-size:14px;">Uploaded at</td><td style="padding:6px 0;font-size:14px;">{uploadedAt}</td></tr>
					</table>
				  </div>
				  <p style="color:#888;font-size:13px;">Thank you for using Video Upload Service.</p>
				</div>
				<div style="background:#f0f0f0;padding:16px 32px;text-align:center;">
				  <p style="color:#aaa;font-size:12px;margin:0;">Video Upload Service &mdash; Automated Notification</p>
				</div>
			  </div>
			</body>
			</html>
			""";
	}

	private static string FormatFileSize(long bytes)
	{
		if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
		if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
		if (bytes >= 1_024) return $"{bytes / 1_024.0:F1} KB";
		return $"{bytes} B";
	}
}
