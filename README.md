# oculusit.sync

A .NET 10 Worker Service that synchronises ConnectWise companies, projects, and time entries to the Keka HR platform. The service runs as an AWS ECS Fargate task and persists sync state in DynamoDB.

> Syncs PSA / CoreHR data: ConnectWise → Keka (and Keka → ConnectWise) for the OculusIT customer.

---

## Prerequisites

| Tool | Purpose |
|------|---------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Build & run locally |
| [Docker](https://docs.docker.com/get-docker/) | Build container image |
| [AWS CLI v2](https://docs.aws.amazon.com/cli/latest/userguide/install-cliv2.html) | ECR authentication & ECS deployment |

Ensure your AWS CLI is configured with credentials that have permissions for ECR (`ecr:GetAuthorizationToken`, `ecr:BatchCheckLayerAvailability`, `ecr:PutImage`, etc.) and ECS (`ecs:UpdateService`).

---

## Configuration

Application settings are injected at runtime via **AWS SSM Parameter Store** (see `infra/task-definition.json`).  
For local development, use `appsettings.Development.json` or [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets).

---

## Docker Build to ECR Push

All commands below must be run from the **solution root directory** (the folder containing `oculusit.sync/`, `oculusit.sync.core/`, etc.) because the `Dockerfile` uses relative `../` paths for the multi-project COPY steps.

```
AWS Account ID : 272403792718
Region         : ap-south-1
ECR Repository : oculusit-integration-service
```

---

### Step 1 — Authenticate Docker to ECR

```sh
aws ecr get-login-password --region ap-south-1 \
  | docker login --username AWS --password-stdin \
    272403792718.dkr.ecr.ap-south-1.amazonaws.com
```

---

### Step 2 — Build the Docker image

```sh
docker build \
  -f oculusit.sync/Dockerfile \
  -t oculusit-integration-service:latest \
  .
```

> The `.` context is the solution root. The `Dockerfile` uses `../` relative paths to copy each `.csproj` file, which requires the build context to start one level above `oculusit.sync/`.

---

### Step 3 — Tag the image for ECR

```sh
docker tag \
  oculusit-integration-service:latest \
  272403792718.dkr.ecr.ap-south-1.amazonaws.com/oculusit-integration-service:latest
```

---

### Step 4 — Push the image to ECR

```sh
docker push \
  272403792718.dkr.ecr.ap-south-1.amazonaws.com/oculusit-integration-service:latest
```

---

### Step 5 — (Optional) Force a new ECS deployment

After the image is pushed, force ECS to pull the new image and restart the task:

```sh
aws ecs update-service \
  --cluster <your-cluster-name> \
  --service <your-service-name> \
  --force-new-deployment \
  --region ap-south-1
```

Replace `<your-cluster-name>` and `<your-service-name>` with your actual ECS cluster and service names.

---

### All-in-one script (PowerShell)

```powershell
$ACCOUNT_ID = "272403792718"
$REGION     = "ap-south-1"
$REPO       = "oculusit-integration-service"
$TAG        = "latest"
$ECR_URI    = "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com/$REPO`:$TAG"

# 1. Authenticate
aws ecr get-login-password --region $REGION |
  docker login --username AWS --password-stdin "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com"

# 2. Build
docker build -f oculusit.sync/Dockerfile -t "$REPO`:$TAG" .

# 3. Tag
docker tag "$REPO`:$TAG" $ECR_URI

# 4. Push
docker push $ECR_URI

Write-Host "Image pushed: $ECR_URI"
```

---

## Project Structure

```
oculusit.sync/               # Worker Service entry point (BackgroundService)
oculusit.sync.core/          # Domain models, interfaces, DynamoDB state service
oculusit.sync.orchestration/ # Company & project sync orchestration logic
oculusit.sync.connectwise/   # ConnectWise API client
oculusit.sync.keka/          # Keka HR API client
infra/
  task-definition.json       # ECS Fargate task definition
```

---

## Infrastructure

| Resource | Value |
|----------|-------|
| ECS Task family | `oculusit-integration-task` |
| Container name | `oculusit-integration-service` |
| Execution & Task role | `arn:aws:iam::272403792718:role/OculusitIntegrationTaskRole` |
| CloudWatch log group | `/ecs/oculusit-integration` |
| DynamoDB table | Resolved from SSM `oculusit/sync/DynamoDB/TableName` |
