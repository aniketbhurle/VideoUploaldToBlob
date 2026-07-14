using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace VideoUploadTest.Services;

public interface IBlobStorageService
{
	Task<string> UploadVideoAsync(IFormFile file, CancellationToken cancellationToken = default);
}

public class BlobStorageService : IBlobStorageService
{
	private readonly BlobContainerClient _containerClient;
	private readonly ILogger<BlobStorageService> _logger;

	public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
	{
		_logger = logger;

		var connectionString = configuration["AzureStorage:ConnectionString"]
			?? throw new InvalidOperationException("AzureStorage:ConnectionString is not configured.");
		var containerName = configuration["AzureStorage:VideoContainerName"] ?? "videos";

		var blobServiceClient = new BlobServiceClient(connectionString);
		_containerClient = blobServiceClient.GetBlobContainerClient(containerName);
	}

	public async Task<string> UploadVideoAsync(IFormFile file, CancellationToken cancellationToken = default)
	{
		await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

		var blobName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
		var blobClient = _containerClient.GetBlobClient(blobName);

		_logger.LogInformation("Uploading video {FileName} as blob {BlobName}", file.FileName, blobName);

		var options = new BlobUploadOptions
		{
			HttpHeaders = new BlobHttpHeaders
			{
				ContentType = file.ContentType
			}
		};

		await using var stream = file.OpenReadStream();
		await blobClient.UploadAsync(stream, options, cancellationToken);

		_logger.LogInformation("Video {BlobName} uploaded successfully.", blobName);

		return blobName;
	}
}
