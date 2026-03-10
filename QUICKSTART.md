# Nadosh Quick Reference

## 🚀 Quick Start Commands

```bash
# Start the platform
docker compose up -d

# Check status
docker ps

# View logs
docker compose logs -f api
docker compose logs -f deep-workers

# Stop everything
docker compose down
```

## 📡 API Examples

All requests require: `-H "X-API-Key: dev-api-key-123"`

### Trigger Demo Scan
```bash
curl -X POST http://localhost:5000/v1/targets/demo-scan \
  -H "X-API-Key: dev-api-key-123"
```

### Search Exposures
```bash
curl -X POST http://localhost:5000/v1/exposures/search \
  -H "X-API-Key: dev-api-key-123" \
  -H "Content-Type: application/json" \
  -d '{"query": "service:ssh AND port:22"}'
```

### Get IP Timeline
```bash
curl http://localhost:5000/v1/timeline/192.168.4.25 \
  -H "X-API-Key: dev-api-key-123"
```

### View Platform Stats
```bash
curl http://localhost:5000/v1/stats/summary \
  -H "X-API-Key: dev-api-key-123"
```

### List All Targets
```bash
curl http://localhost:5000/v1/targets?skip=0&take=50 \
  -H "X-API-Key: dev-api-key-123"
```

### Get Recent Changes
```bash
curl http://localhost:5000/v1/timeline/changes?days=1 \
  -H "X-API-Key: dev-api-key-123"
```

## 🔍 Query DSL Examples

```
# Basic
service:ssh
port:22
severity:high
state:open

# Boolean operators
service:ssh AND port:22
severity:high OR severity:critical
service:http AND NOT state:closed

# Time filters
time:last_7d
time:last_30d
time:since:2026-01-01

# Complex
(service:ssh OR service:telnet) AND severity:high AND time:last_7d
```

## 🐳 Docker Commands

```bash
# Rebuild specific service
docker compose build api
docker compose up -d api

# Scale workers
docker compose up -d --scale deep-workers=5

# View resource usage
docker stats

# Clean up
docker compose down -v  # Warning: deletes volumes!

# Restart a service
docker compose restart api
```

## 🗄️ Database Commands

```bash
# Connect to PostgreSQL
docker exec -it nadosh-postgres psql -U nadosh -d nadosh

# Useful SQL queries
SELECT COUNT(*) FROM "Targets";
SELECT COUNT(*) FROM "Observations";
SELECT COUNT(*) FROM "CurrentExposures";

# View recent observations
SELECT "TargetId", "Port", "CurrentState", "ServiceName", "ObservedAt" 
FROM "Observations" 
ORDER BY "ObservedAt" DESC 
LIMIT 20;
```

## 🔧 Development Commands

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run migrations
dotnet ef migrations add MigrationName --project Nadosh.Infrastructure --startup-project Nadosh.Api
dotnet ef database update --project Nadosh.Infrastructure --startup-project Nadosh.Api

# Run locally (without Docker)
dotnet run --project Nadosh.Api
dotnet run --project Nadosh.Workers
```

## 📊 Monitoring

```bash
# API Health Check
curl http://localhost:5000/health/ready

# Check Redis
docker exec -it nadosh-redis redis-cli PING

# Check PostgreSQL
docker exec -it nadosh-postgres pg_isready

# View all container logs
docker compose logs --tail=100 -f
```

## 🔐 Security Notes

**Default Credentials (CHANGE IN PRODUCTION!):**
- API Key: `dev-api-key-123`
- PostgreSQL: `nadosh` / `nadosh_password`
- Redis: No password (localhost only)

## 📱 Web Dashboard

Access at: **http://localhost:5000**

Features:
- View all active hosts
- Search exposures with Query DSL
- View IP timelines
- Monitor recent changes
- Real-time statistics

## 🆘 Troubleshooting

### Port already in use
```bash
# Check what's using port 5000
netstat -ano | findstr :5000

# Change API port in docker-compose.yml
ports:
  - "5001:8080"  # Use 5001 instead
```

### Database connection failed
```bash
# Restart PostgreSQL
docker compose restart postgres

# Check logs
docker compose logs postgres
```

### Workers not running
```bash
# Check worker logs
docker compose logs deep-workers
docker compose logs enrichment-workers

# Restart workers
docker compose restart deep-workers enrichment-workers
```

## 🎯 Common Workflows

### Add New Targets
```bash
# Manual
curl -X POST http://localhost:5000/v1/targets/demo-scan \
  -H "X-API-Key: dev-api-key-123"

# Via SQL
docker exec -it nadosh-postgres psql -U nadosh -d nadosh -c \
  "INSERT INTO \"Targets\" (\"Ip\", \"CidrSource\", \"Monitored\", \"NextScheduled\") 
   VALUES ('10.0.0.1', 'manual', true, NOW());"
```

### Export Data
```bash
# Export exposures to JSON
curl http://localhost:5000/v1/exposures/search \
  -H "X-API-Key: dev-api-key-123" \
  -H "Content-Type: application/json" \
  -d '{"query": "state:open", "pageSize": 1000}' > exposures.json

# Export database
docker exec nadosh-postgres pg_dump -U nadosh nadosh > backup.sql
```

### Reset Everything
```bash
# Stop and remove all containers/volumes
docker compose down -v

# Remove database
docker volume rm nadosh_postgres_data

# Start fresh
docker compose up -d
```

## 📞 Getting Help

- **Documentation**: See README.md
- **Issues**: GitHub Issues
- **Logs**: `docker compose logs -f`
- **Health**: `curl http://localhost:5000/health/ready`
