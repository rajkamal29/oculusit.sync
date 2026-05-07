# OculusIT Integration Service — ECS Fargate Architecture

> **Project:** OculusitIntegrationService  
> **Region:** ap-south-1  
> **Account:** 272403792718  
> **Last Updated:** 2025

---

## Table of Contents

1. [Overview](#overview)
2. [Core Concepts](#core-concepts)
3. [End-to-End Flow](#end-to-end-flow)
4. [Task Startup Sequence](#task-startup-sequence)
5. [IAM Roles Explained](#iam-roles-explained)
6. [Task Definition Revisions](#task-definition-revisions)
7. [SSM Parameter Strategy](#ssm-parameter-strategy)
8. [Key Architectural Summary](#key-architectural-summary)

---

## Overview

The OculusIT Integration Service is a **.NET 10 Worker Service** deployed as a
containerised batch job on **AWS ECS Fargate**. It synchronises company data from
**ConnectWise** into **Keka PSA** and persists sync state in **DynamoDB**.
The job is triggered automatically **every hour** via **EventBridge Scheduler**.

```
ConnectWise API  ──►  Worker Service (ECS Fargate)  ──►  Keka PSA API
                              │
                              ▼
                        DynamoDB (sync state)
```

---

## Core Concepts

### Task Definition — *Blueprint / Template*

| Field | Value |
|---|---|
| Family | `oculusit-integration-task` |
| Container | `oculusit-integration-service` |
| Image | ECR → `oculusit-integration-service:latest` |
| CPU | 256 units (0.25 vCPU) |
| Memory | 512 MB |
| Secrets source | SSM Parameter Store |
| Log destination | CloudWatch `/ecs/oculusit-integration` |

> Think of the Task Definition as a **Docker Compose file stored in AWS** —
> it describes what to run, how much resource to use, and what secrets to inject.

### ECS Cluster — *Execution Environment*

| Field | Value |
|---|---|
| Name | `oculusit-integration-cluster` |
| Type | Fargate (serverless — no EC2 to manage) |

> Think of the Cluster as the **factory floor** that receives a run instruction
> and allocates serverless compute to execute the blueprint.

---

## End-to-End Flow

```
┌─────────────────────┐
│  EventBridge        │
│  Scheduler          │  Triggers every 1 hour  →  rate(1 hour)
│  oculusit-sync-     │
│  hourly             │
└──────────┬──────────┘
           │  ecs:RunTask  (via Scheduler IAM Role)
           │  Cluster:         oculusit-integration-cluster
           │  Task Definition: oculusit-integration-task (latest)
           ▼
┌─────────────────────┐
│  ECS CLUSTER        │  1. Receives RunTask instruction
│                     │  2. Allocates Fargate serverless compute
│                     │  3. Starts task lifecycle
└──────────┬──────────┘
           │
           ▼
┌──────────────────────────────────────────────────────┐
│  TASK STARTUP  (Execution Role)                      │
│                                                      │
│  A. Pull image from ECR                              │
│  B. Fetch SSM secrets → inject as env vars           │
│  C. Start .NET Worker container                      │
└──────────┬───────────────────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────────────────┐
│  CONTAINER RUNNING  (Task Role)                      │
│                                                      │
│  Worker.ExecuteAsync()                               │
│    │                                                 │
│    ├─► DynamoDB.GetAsync()                           │
│    │     Full run or incremental?                    │
│    │                                                 │
│    ├─► ConnectWise API                               │
│    │     Fetch companies (full or since LastUpdated) │
│    │                                                 │
│    ├─► Keka API                                      │
│    │     Create / Update clients                     │
│    │                                                 │
│    └─► DynamoDB.SaveAsync() / AppendAsync()          │
│          Persist sync state + LastUpdatedAt          │
└──────────┬───────────────────────────────────────────┘
           │
           ▼
┌─────────────────────┐
│  TASK STOPS         │  Exit code 0 = Success
│  (Normal)           │  lifetime.StopApplication() called
└──────────┬──────────┘
           │
           ▼
┌─────────────────────┐
│  CloudWatch Logs    │  /ecs/oculusit-integration
│                     │  All Serilog output captured here
└─────────────────────┘
```

---

## Task Startup Sequence

```
Second 0    ECS Agent starts on Fargate compute
            │
Second 1    Execution Role → ecr:GetAuthorizationToken
            Execution Role → ecr:BatchGetImage
            Docker image pulled from ECR
            │
Second 5    Execution Role → ssm:GetParameters (all 17 params)
            Execution Role → kms:Decrypt (SecureString values)
            Env vars injected into container:
              ConnectWise__BaseUrl    = "https://na.myconnectwise.net"
              ConnectWise__PrivateKey = "**decrypted**"
              Keka__ClientSecret      = "**decrypted**"
              DynamoDB__TableName     = "oculusit-sync-state"
              ...
            │
Second 8    Container starts
            .NET host reads env vars via IConfiguration
            ConnectWise__BaseUrl → ConnectWise:BaseUrl
              → ConnectWiseConfiguration.BaseUrl
            │
Second 10   Worker.ExecuteAsync() begins
```

---

## IAM Roles Explained

Both `executionRoleArn` and `taskRoleArn` currently point to the same role:
**`OculusitIntegrationTaskRole`**

### Execution Role — AWS Infrastructure Layer

> Active **before** the container starts. Used by the ECS Agent, not the app.

| Permission | Purpose |
|---|---|
| `ecr:GetAuthorizationToken` | Authenticate to ECR |
| `ecr:BatchGetImage` | Pull container image |
| `ssm:GetParameters` | Fetch SSM parameter values |
| `kms:Decrypt` | Decrypt SecureString parameters |
| `logs:CreateLogStream` | Set up CloudWatch log stream |
| `logs:PutLogEvents` | Write startup logs |

### Task Role — Application Layer

> Active **while** the container is running. Used by the .NET app.

| Permission | Purpose |
|---|---|
| `dynamodb:GetItem` | Read existing sync state |
| `dynamodb:PutItem` | Write full sync state (first run) |
| `dynamodb:UpdateItem` | Append incremental sync entries |

### Future Recommendation — Split into Two Roles

```
OculusitIntegrationExecutionRole  →  ECR + SSM + Logs  (startup only)
OculusitIntegrationTaskRole       →  DynamoDB only     (runtime only)
```

This follows the **least-privilege principle** — each role has only the
permissions it needs for its specific lifecycle phase.

---

## Task Definition Revisions

Every `register-task-definition` command creates a **new immutable revision**.
Previous revisions remain available for instant rollback.

```
oculusit-integration-task:1   DEREGISTERED  (initial)
oculusit-integration-task:2   ACTIVE        (current)
oculusit-integration-task:3   ACTIVE        (next deploy)
```

### Scheduler always picks the latest

By pointing the EventBridge Scheduler to `oculusit-integration-task`
(without a revision suffix), AWS automatically uses the latest active revision.

### Rollback command

```powershell
# Roll back to a previous revision instantly
aws ecs update-service `
  --cluster oculusit-integration-cluster `
  --service YOUR_SERVICE `
  --task-definition oculusit-integration-task:1 `
  --region ap-south-1
```

---

## SSM Parameter Strategy

All parameters stored under prefix: `/oculusit/sync/`

### How SSM → Env Var → .NET Config binding works

```
SSM Path                                Env Var (injected by ECS)     .NET Config Key
/oculusit/sync/ConnectWise/BaseUrl  →   ConnectWise__BaseUrl       →  ConnectWise:BaseUrl
/oculusit/sync/ConnectWise/PrivKey  →   ConnectWise__PrivateKey    →  ConnectWise:PrivateKey
/oculusit/sync/Keka/ClientSecret    →   Keka__ClientSecret         →  Keka:ClientSecret
/oculusit/sync/DynamoDB/TableName   →   DynamoDB__TableName        →  DynamoDB:TableName
```

> `__` (double underscore) is the .NET environment variable section separator.
> It maps to `:` in IConfiguration, which binds to the configuration class properties.

### Parameter List

| SSM Parameter | Type | Category |
|---|---|---|
| `ConnectWise/BaseUrl` | String | Required |
| `ConnectWise/CompanyId` | String | Required |
| `ConnectWise/PublicKey` | SecureString | Required |
| `ConnectWise/PrivateKey` | SecureString | Required |
| `ConnectWise/ClientId` | SecureString | Required |
| `ConnectWise/ApiVersion` | String | Required |
| `ConnectWise/PageSize` | String | Tunable |
| `Keka/TenantName` | String | Required |
| `Keka/ApiBaseUrl` | String | Required |
| `Keka/IdentityUrl` | String | Required |
| `Keka/ClientId` | SecureString | Required |
| `Keka/ClientSecret` | SecureString | Required |
| `Keka/ApiKey` | SecureString | Required |
| `Keka/GrantType` | String | Tunable |
| `Keka/Scope` | String | Tunable |
| `Keka/TokenExpiryBufferSeconds` | String | Tunable |
| `DynamoDB/TableName` | String | Required |

---

## Key Architectural Summary

| Concept | Role | Think of it as |
|---|---|---|
| **Task Definition** | WHAT to run | Blueprint / template (versioned) |
| **ECS Cluster** | WHERE to run | Serverless factory floor |
| **EventBridge Scheduler** | WHEN to run | Cron trigger (every 1 hour) |
| **Execution Role** | HOW it starts | Startup permissions (ECR + SSM) |
| **Task Role** | WHAT it can do | Runtime permissions (DynamoDB) |
| **SSM Parameter Store** | Config source | Secrets vault (injected at startup) |
| **DynamoDB** | State store | Tracks what was synced and when |
| **CloudWatch Logs** | Observability | All application logs |

---

## Deployment Steps (Manual)

| Step | Command | Frequency |
|---|---|---|
| 1. Create SSM params | `aws ssm put-parameter` | One-time |
| 2. Create DynamoDB table | `aws dynamodb create-table` | One-time |
| 3. Create CloudWatch group | `aws logs create-log-group` | One-time |
| 4. Attach IAM policy | `aws iam put-role-policy` | One-time |
| 5. Build & push image | `docker build` + `docker push` | Every deploy |
| 6. Register task definition | `aws ecs register-task-definition` | Every deploy |
| 7. Run manual test | `aws ecs run-task` | On demand |
| 8. Watch logs | `aws logs tail` | On demand |
