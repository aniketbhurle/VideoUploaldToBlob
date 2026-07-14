namespace VideoUploadFunctions.Models;

public class VideoMessage
{
	public string BlobName { get; set; } = string.Empty;
	public string OriginalFileName { get; set; } = string.Empty;
	public string UserEmail { get; set; } = string.Empty;
	public string UserName { get; set; } = string.Empty;
	public long FileSizeBytes { get; set; }
	public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
	public string ContainerName { get; set; } = string.Empty;
}
