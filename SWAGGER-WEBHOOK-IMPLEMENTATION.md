# Swagger & Webhook Polling Implementation

**Status**: ✅ Complete  
**Date**: March 13, 2026

## Summary

Exposed OpenAPI/Swagger JSON endpoint and created webhook delivery polling API to enable external monitoring of webhook notifications.

## What Was Implemented

### 1. Swagger JSON Endpoint (Global)
**File**: [Nadosh.Api/Program.cs](Nadosh.Api/Program.cs)

- Moved `MapOpenApi()` outside the development-only block
- Swagger JSON now available at `/openapi/v1.json` in **all environments** (dev, staging, production)
- Already had `AddOpenApi()` registered, just needed endpoint mapping

**Access**:
```bash
curl http://localhost:5000/openapi/v1.json
```

### 2. Webhook Delivery Tracking Model
**File**: [Nadosh.Core/Models/WebhookDelivery.cs](Nadosh.Core/Models/WebhookDelivery.cs)

Created entity to track webhook delivery history:
- `Id` (Guid) - Unique delivery identifier
- `EventType` (string) - Event category (e.g., "ChangeNotification", "CveAlert")
- `Url` (string) - Target webhook endpoint
- `Payload` (string) - JSON payload sent
- `HttpStatusCode` (int?) - Response status from webhook
- `Success` (bool) - Delivery outcome
- `ErrorMessage` (string?) - Failure details if applicable
- `SentAt` (DateTime) - Timestamp of delivery attempt

**DbContext**: [Nadosh.Infrastructure/Data/NadoshDbContext.cs](Nadosh.Infrastructure/Data/NadoshDbContext.cs)
- Added `DbSet<WebhookDelivery>` property
- Configured entity with indexes on `SentAt`, `EventType`, `Success`
- Generated migration: `AddWebhookDeliveries`

### 3. Webhook Delivery Persistence
**File**: [Nadosh.Workers/Workers/ChangeDetectorWorker.cs](Nadosh.Workers/Workers/ChangeDetectorWorker.cs)

Updated change notification worker to persist every webhook delivery:
- Creates `WebhookDelivery` record before sending
- Captures HTTP status code and success/failure state
- Logs error messages for failed deliveries
- Persists all records to database via `db.SaveChangesAsync()`

**Flow**:
1. Detect changes in recent observations
2. Build notification payload
3. For each configured webhook URL:
   - Create delivery record
   - POST JSON payload
   - Update record with response status
   - Save to database
4. All deliveries queryable via polling API

### 4. Webhook Polling API
**File**: [Nadosh.Api/Controllers/WebhooksController.cs](Nadosh.Api/Controllers/WebhooksController.cs)

Created REST API for webhook delivery polling:

#### **GET /api/webhooks/deliveries**
Query webhook delivery history with filtering and pagination:
- `?eventType=ChangeNotification` - Filter by event type
- `?success=true` - Filter by success/failure
- `?since=2026-03-13T00:00:00Z` - Filter by timestamp
- `?limit=100&offset=0` - Pagination (max 1000 per page)

**Response**:
```json
{
  "total": 42,
  "limit": 100,
  "offset": 0,
  "deliveries": [
    {
      "id": "guid",
      "eventType": "ChangeNotification",
      "url": "https://example.com/webhook",
      "success": true,
      "httpStatusCode": 200,
      "errorMessage": null,
      "sentAt": "2026-03-13T12:34:56Z",
      "payloadPreview": "{\"timestamp\":\"2026-03-13...\" (truncated)"
    }
  ]
}
```

#### **GET /api/webhooks/deliveries/{id}**
Get full details of a specific delivery (includes complete payload):

**Response**:
```json
{
  "id": "guid",
  "eventType": "ChangeNotification",
  "url": "https://example.com/webhook",
  "payload": "{...full JSON payload...}",
  "success": true,
  "httpStatusCode": 200,
  "errorMessage": null,
  "sentAt": "2026-03-13T12:34:56Z"
}
```

#### **GET /api/webhooks/stats**
Get delivery statistics with optional time filtering:
- `?since=2026-03-13T00:00:00Z` - Stats since timestamp

**Response**:
```json
{
  "overall": {
    "totalDeliveries": 100,
    "successCount": 95,
    "failureCount": 5,
    "successRate": 95.0
  },
  "byEventType": [
    {
      "eventType": "ChangeNotification",
      "count": 80,
      "successCount": 77,
      "failureCount": 3
    },
    {
      "eventType": "CveAlert",
      "count": 20,
      "successCount": 18,
      "failureCount": 2
    }
  ]
}
```

## Usage Examples

### Configure Webhooks
**File**: `appsettings.json`
```json
{
  "Webhooks": {
    "ChangeNotifications": [
      "https://your-service.com/webhook",
      "https://backup.com/notify"
    ]
  }
}
```

### Poll Recent Deliveries
```bash
# Get last 24 hours of deliveries
curl -H "X-API-Key: your-key" \
  "http://localhost:5000/api/webhooks/deliveries?since=2026-03-12T12:00:00Z"

# Get failed deliveries only
curl -H "X-API-Key: your-key" \
  "http://localhost:5000/api/webhooks/deliveries?success=false"

# Get delivery stats
curl -H "X-API-Key: your-key" \
  "http://localhost:5000/api/webhooks/stats"
```

### Retrieve Full Payload
```bash
# Get complete payload for specific delivery
curl -H "X-API-Key: your-key" \
  "http://localhost:5000/api/webhooks/deliveries/{guid}"
```

## Migration & Deployment

### Database Migration
```bash
# Migration already generated
dotnet ef migrations add AddWebhookDeliveries

# Apply migration
dotnet ef database update --project Nadosh.Infrastructure \
  --startup-project Nadosh.Workers
```

### Build Status
- ✅ **Workers**: Build successful
- ✅ **Infrastructure**: Build successful  
- ⚠️ **API**: Pre-existing StatsController errors (unrelated to webhook work)

The API errors are in [StatsController.cs](Nadosh.Api/Controllers/StatsController.cs) referencing `ThreatScores`, `MitreAttackMappings`, `CveFindings` DbSets that don't exist yet. This is **not related** to the webhook polling controller.

## Benefits

1. **External Monitoring**: Systems can poll webhook delivery status instead of relying on push-only
2. **Audit Trail**: Complete history of all webhook attempts with success/failure tracking
3. **Debugging**: Full payload retrieval for failed deliveries
4. **Analytics**: Delivery statistics by event type and time range
5. **OpenAPI Discovery**: Swagger JSON enables automated client generation

## Next Steps

- Fix pre-existing StatsController errors (requires threat-intel DbSets)
- Add webhook delivery retry mechanism for failed attempts
- Implement webhook signature verification for security
- Add delivery retention policy (auto-purge old records)
- Consider webhook subscription management API (register/unregister URLs dynamically)
