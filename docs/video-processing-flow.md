# Video Processing Flow

This document describes how a video moves through the Razor Pages app and Azure Functions pipeline.

## End-to-end Flow Diagram

```mermaid
flowchart TD
	A["Razor Pages app<br/>Index.cshtml.cs OnPostAsync"] -->|Upload .mp4| B["Azure Blob Storage<br/>videos container"]
	A -->|Send message| Q1["Service Bus Queue<br/>video-uploaded (VideoUploadedQueueName)"]

	Q1 --> F1["Azure Function<br/>VideoValidationFunction"]
	F1 -->|Valid video| Q2["Service Bus Queue<br/>video-validated (VideoValidatedQueueName)"]
	F1 -->|Invalid/deserialization/blob missing| DL1["Dead-letter / Abandon"]

	Q2 --> F2["Azure Function<br/>ThumbnailFunction"]
	F2 -->|Create thumbnail| C["Blob Storage<br/>thumbnails container"]
	F2 -->|Send processed event| Q3["Service Bus Queue<br/>video-processed (VideoProcessedQueueName)"]
	F2 -->|Error| DL2["Abandon / retry"]

	Q3 --> F3["Azure Function<br/>EmailNotificationFunction"]
	F3 -->|Send confirmation| D["SendGrid Email to user"]
	F3 -->|Error| DL3["Abandon / retry"]
```

## Function Chain

`VideoValidationFunction` → `ThumbnailFunction` → `EmailNotificationFunction`

(Connected through `video-validated` and `video-processed` Service Bus queues.)
