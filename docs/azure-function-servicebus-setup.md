# Azure Functions + Service Bus Setup and Deployment Guide

This guide shows how to:
1. Create an Azure Function App (for this repository's `VideoUploadFunctions` project)
2. Create Azure Service Bus resources (namespace + queues)
3. Deploy function code to Azure
4. Configure app settings so the pipeline works end-to-end

## Prerequisites

- Azure subscription
- Azure CLI installed and logged in:
  - `az login`
- .NET 8 SDK installed
- Azure Functions Core Tools v4 installed (optional, for local publish)
- This solution cloned locally

---

## 1) Create Resource Group

```powershell
$LOCATION = "eastus"
$RG = "rg-video-upload-demo"

az group create --name $RG --location $LOCATION
```

---

## 2) Create Storage Account (required by Function App)

```powershell
$STORAGE = "stvideouploaddemo001"  # must be globally unique, lowercase

az storage account create \
  --name $STORAGE \
  --resource-group $RG \
  --location $LOCATION \
  --sku Standard_LRS \
  --kind StorageV2
```

---

## 3) Create Service Bus Namespace + Queues

### 3.1 Create namespace

```powershell
$SBNS = "sb-video-upload-demo"  # must be globally unique

az servicebus namespace create \
  --name $SBNS \
  --resource-group $RG \
  --location $LOCATION \
  --sku Standard
```

### 3.2 Create queues used by this solution

```powershell
az servicebus queue create --resource-group $RG --namespace-name $SBNS --name video-uploaded
az servicebus queue create --resource-group $RG --namespace-name $SBNS --name video-validated
az servicebus queue create --resource-group $RG --namespace-name $SBNS --name video-processed
```

### 3.3 Get Service Bus connection string

```powershell
$SBCONN = az servicebus namespace authorization-rule keys list \
  --resource-group $RG \
  --namespace-name $SBNS \
  --name RootManageSharedAccessKey \
  --query primaryConnectionString -o tsv
```

---

## 4) Create Function App (Linux, .NET 8 isolated)

```powershell
$PLAN = "plan-video-upload-demo"
$FUNCAPP = "func-video-upload-demo-001"  # must be globally unique

az functionapp plan create \
  --name $PLAN \
  --resource-group $RG \
  --location $LOCATION \
  --sku EP1 \
  --is-linux

az functionapp create \
  --name $FUNCAPP \
  --resource-group $RG \
  --plan $PLAN \
  --storage-account $STORAGE \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4
```

> Note: Consumption/Flex plans can also be used. The command above uses Premium (`EP1`) for predictable warm starts.

---

## 5) Configure Function App Settings

Set required values used by `VideoUploadFunctions`:

```powershell
$VIDEO_CONTAINER = "videos"
$THUMB_CONTAINER = "thumbnails"

az functionapp config appsettings set \
  --name $FUNCAPP \
  --resource-group $RG \
  --settings \
  "ServiceBusConnection=$SBCONN" \
  "VideoUploadedQueueName=video-uploaded" \
  "VideoValidatedQueueName=video-validated" \
  "VideoProcessedQueueName=video-processed" \
  "AzureWebJobsStorage=$(az storage account show-connection-string --name $STORAGE --resource-group $RG --query connectionString -o tsv)" \
  "ThumbnailContainerName=$THUMB_CONTAINER"
```

If email notification is enabled, also set:

```powershell
az functionapp config appsettings set \
  --name $FUNCAPP \
  --resource-group $RG \
  --settings \
  "SendGridApiKey=<your-sendgrid-api-key>" \
  "NotificationFromEmail=noreply@yourdomain.com"
```

---

## 6) Deploy Function Code

From repository root:

```powershell
cd .\VideoUploadFunctions
dotnet publish -c Release
func azure functionapp publish $FUNCAPP --dotnet-isolated
```

Alternative (ZIP deploy):

```powershell
cd .\VideoUploadFunctions
Compress-Archive -Path .\* -DestinationPath .\deploy.zip -Force
az functionapp deployment source config-zip \
  --resource-group $RG \
  --name $FUNCAPP \
  --src .\deploy.zip
```

---

## 7) Configure Razor Pages App (`VideoUploadTest`) to use same resources

In the web app configuration (appsettings or Azure App Service settings), use:

- `AzureStorage:ConnectionString` = storage connection string
- `AzureStorage:VideoContainerName` = `videos`
- `AzureServiceBus:ConnectionString` = service bus connection string
- `AzureServiceBus:VideoUploadedQueueName` = `video-uploaded`

This ensures upload events are sent to the queue consumed by `VideoValidationFunction`.

---

## 8) Quick Validation Checklist

1. Upload `.mp4` from Razor page
2. Confirm message appears in `video-uploaded`
3. Confirm `VideoValidationFunction` processes it and sends to `video-validated`
4. Confirm `ThumbnailFunction` writes thumbnail to `thumbnails` and sends to `video-processed`
5. Confirm `EmailNotificationFunction` sends notification email

---

## Useful Commands

### Stream function logs

```powershell
az functionapp log tail --name $FUNCAPP --resource-group $RG
```

### List function app settings

```powershell
az functionapp config appsettings list --name $FUNCAPP --resource-group $RG -o table
```

### Check queues

```powershell
az servicebus queue list --resource-group $RG --namespace-name $SBNS -o table
```
