# Video Upload App - Flow and Azure Setup Guide

## 1) How this application works

This solution has two parts:

- **Razor Pages web app** (`VideoUploadTest`)
  - Accepts `.mp4` uploads from users
  - Stores videos in Azure Blob Storage
  - Publishes upload metadata to Azure Service Bus
- **Azure Functions app** (`VideoUploadFunctions`)
  - Validates uploaded video metadata/blob
  - Generates a thumbnail image
  - Sends an email notification

## 2) End-to-end flow

1. User opens **Index** page and uploads a `.mp4` + email.
2. Razor Page validates file type/size and required email.
3. Web app uploads file to Blob container `videos`.
4. Web app sends message to Service Bus queue `video-uploaded`.
5. `VideoValidationFunction` reads from `video-uploaded`:
   - checks blob exists
   - checks mp4 content type/extension
   - forwards valid message to `video-validated`
6. `ThumbnailFunction` reads from `video-validated`:
   - creates a thumbnail image
   - uploads it to `thumbnails` container
   - sends message to `video-processed`
7. `EmailNotificationFunction` reads from `video-processed`:
   - sends success email via SendGrid

## 3) Queues and containers used

### Service Bus queues

- `video-uploaded` (input from web app)
- `video-validated` (after validation)
- `video-processed` (after thumbnail generation)

### Blob containers

- `videos` (uploaded files)
- `thumbnails` (generated thumbnail images)

## 4) Required configuration

### Razor app (`VideoUploadTest/appsettings.json`)

- `AzureStorage:ConnectionString`
- `AzureStorage:VideoContainerName` (default `videos`)
- `AzureServiceBus:ConnectionString`
- `AzureServiceBus:VideoUploadedQueueName` (default `video-uploaded`)
- `VideoUpload:MaxFileSizeMB`

### Functions app (`VideoUploadFunctions/local.settings.json` or Azure App Settings)

- `AzureWebJobsStorage`
- `StorageConnection`
- `ServiceBusConnection`
- `VideoUploadedQueueName`
- `VideoValidatedQueueName`
- `VideoProcessedQueueName`
- `ThumbnailContainerName`
- `SendGridApiKey`
- `NotificationFromEmail`

## 5) Create Azure Service Bus (Portal)

1. In Azure Portal, create resource: **Service Bus Namespace**.
2. Choose pricing tier (Standard is typical for queues).
3. Open the namespace -> **Shared access policies** -> `RootManageSharedAccessKey`.
4. Copy the **Primary Connection String**.
5. In namespace -> **Queues** -> create:
   - `video-uploaded`
   - `video-validated`
   - `video-processed`

## 6) Create Azure Function App (Portal)

1. Create resource: **Function App**.
2. Publish: **Code**.
3. Runtime stack: **.NET**.
4. Version/Mode: **.NET 8 (Isolated)**.
5. Hosting plan: Consumption or Premium (per requirements).
6. After creation, open Function App -> **Configuration** -> add app settings:
   - `StorageConnection`
   - `ServiceBusConnection`
   - `VideoUploadedQueueName=video-uploaded`
   - `VideoValidatedQueueName=video-validated`
   - `VideoProcessedQueueName=video-processed`
   - `ThumbnailContainerName=thumbnails`
   - `SendGridApiKey`
   - `NotificationFromEmail`
7. Save and restart Function App.

## 7) Deployment overview

1. Deploy `VideoUploadFunctions` to the Function App.
2. Deploy `VideoUploadTest` to App Service (or run locally).
3. Configure web app settings with Blob + Service Bus connection strings.
4. Test by uploading an `.mp4` file from the Razor page.

## 8) Quick verification checklist

- Upload succeeds in web app.
- Message appears in `video-uploaded` queue.
- Message moves through `video-validated` and `video-processed`.
- Thumbnail appears in `thumbnails` container.
- Confirmation email arrives.

## 9) Notes

- If content-type is incorrect, validation can dead-letter the message.
- Missing SendGrid API key will prevent email sending.
- Ensure all services use the same Azure subscription/resource group for easier management.
