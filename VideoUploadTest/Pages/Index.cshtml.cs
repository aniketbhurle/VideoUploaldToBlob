using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VideoUploadTest.Services;

namespace VideoUploadTest.Pages
{
	public class IndexModel : PageModel
	{
		private readonly ILogger<IndexModel> _logger;
		private readonly IBlobStorageService _blobStorageService;
		private readonly IServiceBusService _serviceBusService;
		private readonly IConfiguration _configuration;

		[BindProperty]
		public IFormFile? VideoFile { get; set; }

		[BindProperty]
		public string UserEmail { get; set; } = string.Empty;

		[BindProperty]
		public string UserName { get; set; } = string.Empty;

		public string? SuccessMessage { get; set; }
		public string? ErrorMessage { get; set; }

		public IndexModel(
			ILogger<IndexModel> logger,
			IBlobStorageService blobStorageService,
			IServiceBusService serviceBusService,
			IConfiguration configuration)
		{
			_logger = logger;
			_blobStorageService = blobStorageService;
			_serviceBusService = serviceBusService;
			_configuration = configuration;
		}

		public void OnGet() { }

		public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
		{
			if (VideoFile is null || VideoFile.Length == 0)
			{
				ErrorMessage = "Please select a video file to upload.";
				return Page();
			}

			if (!VideoFile.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(VideoFile.ContentType, "video/mp4", StringComparison.OrdinalIgnoreCase))
			{
				ErrorMessage = "Only .mp4 video files are supported.";
				return Page();
			}

			var maxFileSizeMB = _configuration.GetValue<int>("VideoUpload:MaxFileSizeMB", 500);
			if (VideoFile.Length > maxFileSizeMB * 1024 * 1024L)
			{
				ErrorMessage = $"File size exceeds the maximum allowed size of {maxFileSizeMB} MB.";
				return Page();
			}

			if (string.IsNullOrWhiteSpace(UserEmail))
			{
				ErrorMessage = "Please provide your email address for upload confirmation.";
				return Page();
			}

			try
			{
				var containerName = _configuration["AzureStorage:VideoContainerName"] ?? "videos";
				var blobName = await _blobStorageService.UploadVideoAsync(VideoFile, cancellationToken);

				var message = new VideoUploadMessage(
					BlobName: blobName,
					OriginalFileName: VideoFile.FileName,
					UserEmail: UserEmail,
					UserName: string.IsNullOrWhiteSpace(UserName) ? UserEmail : UserName,
					FileSizeBytes: VideoFile.Length,
					UploadedAt: DateTime.UtcNow,
					ContainerName: containerName);

				await _serviceBusService.SendVideoUploadedMessageAsync(message, cancellationToken);

				SuccessMessage = $"✅ \"{VideoFile.FileName}\" has been uploaded successfully! " +
								 $"You will receive a confirmation email at {UserEmail} once processing is complete.";

				_logger.LogInformation("Video {FileName} uploaded by {Email}.", VideoFile.FileName, UserEmail);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error uploading video {FileName}", VideoFile.FileName);
				ErrorMessage = "An error occurred while uploading your video. Please try again later.";
			}

			return Page();
		}
	}
}
