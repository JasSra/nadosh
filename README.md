# Nadosh - Network Discovery & Exposure Platform

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Docker](https://img.shields.io/badge/docker-required-blue.svg)

**Nadosh** is a modern, high-performance network discovery and exposure management platform built with .NET 10. It provides comprehensive network scanning, service fingerprinting, vulnerability assessment, and change tracking capabilities with a beautiful web-based dashboard.

## 🎯 Features

### Core Capabilities
- **3-Tier Network Scanning**: Discovery → Banner Grabbing → Deep Fingerprinting
- **Real-Time Exposure Tracking**: Track 4,000+ observations across your network
- **Timeline & Change Detection**: Full historical tracking with automatic change detection
- **Advanced Query DSL**: Search exposures with boolean operators (`service:ssh AND port:22`)
- **ASN/Geo Enrichment**: Automatic IP-to-location and organization mapping
- **CVE Vulnerability Detection**: Automatic enrichment with known CVEs from NVD database
- **Webhook Notifications**: Real-time alerts for network changes
- **Modern Web Dashboard**: Built with Alpine.js and Chart.js

### Technical Highlights
- ✅ **High Performance**: 200+ concurrent scans, 1,500ms timeout
- ✅ **Scalable Architecture**: Horizontal worker scaling with Redis caching
- ✅ **Production Ready**: Rate limiting, error handling, health checks
- ✅ **Docker Native**: Full containerized deployment with Docker Compose
- ✅ **PostgreSQL + Redis**: Robust data persistence and caching
- ✅ **RESTful API**: Clean API design with OpenAPI/Swagger support

## 🚀 Quick Start

### Prerequisites
- Docker Desktop (Windows/Mac/Linux)
- .NET 10 SDK (for local development)
- 4GB RAM minimum, 8GB recommended

### Installation

1. **Clone the repository**
```bash
git clone https://github.com/yourusername/nadosh.git
cd nadosh
```

2. **Start the platform**
```bash
docker compose up -d
```

3. **Initialize demo scan**
```bash
curl -X POST http://localhost:5000/v1/targets/demo-scan \
  -H "X-API-Key: dev-api-key-123"
```

4. **Access the dashboard**
```
Open http://localhost:5000 in your browser
```

## 📊 Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Nadosh Platform                           │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────┐      ┌──────────────┐   ┌──────────────┐  │
│  │   Web UI     │────▶ │   API        │   │  Workers     │  │
│  │  Alpine.js   │      │  .NET 10     │   │  Background  │  │
│  └──────────────┘      └──────┬───────┘   └──────┬───────┘  │
│                               │                   │          │
│                        ┌──────▼───────────────────▼───────┐  │
│                        │      PostgreSQL 16               │  │
│                        │      (4,000+ observations)       │  │
│                        └──────────────────────────────────┘  │
│                                                               │
│                        ┌──────────────────────────────────┐  │
│                        │      Redis 7                     │  │
│                        │      (Caching & Queuing)         │  │
│                        └──────────────────────────────────┘  │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

### Components

- **Nadosh.Api**: REST API with controllers for exposures, targets, timeline, stats
- **Nadosh.Workers**: Background workers for scanning, enrichment, change detection
- **Nadosh.Core**: Domain models and business logic
- **Nadosh.Infrastructure**: Data access, EF Core, Redis integration

## 🔧 Configuration

### Environment Variables

Key configuration options in `docker-compose.yml`:

```yaml
# Database Connection
ConnectionStrings__DefaultConnection: "Host=postgres;Database=nadosh;Username=nadosh;Password=nadosh_password"

# Redis Connection
ConnectionStrings__Redis: "redis:6379,abortConnect=false"

# Worker Configuration
WORKER_ROLE: "discovery,banner,fingerprint"
NADOSH_DISCOVERY_CONCURRENCY: "200"
NADOSH_DISCOVERY_TIMEOUT_MS: "1500"

# API Configuration
API_KEY: "dev-api-key-123"
```

### Production Settings

For production deployment, update:
1. Change API keys in environment variables
2. Update PostgreSQL credentials
3. Configure webhook URLs in `appsettings.json`
4. Replace demo geo enrichment with MaxMind GeoIP2
5. Enable HTTPS and configure certificates

## 📡 API Reference

### Core Endpoints

#### Exposures
```bash
# Search exposures with Query DSL
POST /v1/exposures/search
{
  "query": "service:ssh AND port:22 AND state:open",
  "pageSize": 50
}

# Get exposure by ID
GET /v1/exposures/{id}
```

#### Targets
```bash
# List all targets
GET /v1/targets?skip=0&take=100

# Get target details
GET /v1/targets/{ip}

# Trigger demo scan
POST /v1/targets/demo-scan
```

#### Timeline
```bash
# Get IP timeline with change detection
GET /v1/timeline/{ip}

# Get recent changes (24h)
GET /v1/timeline/changes?days=1

# Track service lifecycle
GET /v1/timeline/services/{serviceName}
```

#### Statistics
```bash
# Get platform statistics
GET /v1/stats/summary
```

#### CVE Vulnerabilities
```bash
# Get exposures with CVEs
GET /v1/cve/exposures?severity=high&minCvssScore=7.0

# Get CVE statistics
GET /v1/cve/stats

# Get CVEs for specific exposure
GET /v1/cve/{ip}/{port}

# Get CVE details by ID
GET /v1/cve/details/CVE-2024-12345

# Search for CVEs
POST /v1/cve/search
{
  "product": "openssh",
  "vendor": "openbsd",
  "version": "7.4"
}
```

### Authentication

All API requests require an API key:
```bash
curl -H "X-API-Key: dev-api-key-123" http://localhost:5000/v1/exposures
```

## 🎨 Query DSL Syntax

The platform supports advanced boolean queries:

```
# Basic field filters
service:ssh
port:22
severity:high
state:open

# Boolean operators
service:ssh AND port:22
severity:high OR severity:critical
service:http AND NOT state:closed

# Time-based filters
time:last_7d
time:last_30d
time:last_1h
time:since:2026-01-01

# Complex queries
(service:ssh OR service:telnet) AND severity:high AND time:last_7d
state:open AND (port:80 OR port:443 OR port:8080)
```

## 🛠️ Development

### Local Development Setup

```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run API locally
dotnet run --project Nadosh.Api

# Run workers locally
dotnet run --project Nadosh.Workers
```

### Database Migrations

```bash
# Create new migration
dotnet ef migrations add MigrationName --project Nadosh.Infrastructure --startup-project Nadosh.Api

# Apply migrations
dotnet ef database update --project Nadosh.Infrastructure --startup-project Nadosh.Api
```

### Running Tests

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## 📦 Docker Services

The platform uses 5 Docker services:

| Service | Port | Purpose |
|---------|------|---------|
| `api` | 5000 | REST API + Web UI |
| `postgres` | 5439 | Database |
| `redis` | 6389 | Caching & queuing |
| `deep-workers` | - | Banner + fingerprint scanning |
| `enrichment-workers` | - | Geo enrichment + change detection |

### Scaling Workers

```bash
# Scale discovery workers
docker compose up -d --scale deep-workers=5

# Scale enrichment workers
docker compose up -d --scale enrichment-workers=3
```

## 🔒 Security Considerations

### Before Production Deployment

- [ ] Change all default passwords in `docker-compose.yml`
- [ ] Generate new API keys (use strong random strings)
- [ ] Enable HTTPS/TLS for API
- [ ] Configure firewall rules (restrict PostgreSQL/Redis access)
- [ ] Review and restrict CORS policies
- [ ] Enable rate limiting (already configured)
- [ ] Set up webhook authentication
- [ ] Use secrets management (Azure Key Vault, HashiCorp Vault)
- [ ] Regular security updates for base images

### Sensitive Files (Never Commit)

The `.gitignore` excludes:
- `appsettings.Development.json`
- `.env` files
- `secrets/` directory
- `*.key`, `*.pem`, `*.pfx` files

## 📈 Performance Benchmarks

Tested on: AMD Ryzen 7 / 16GB RAM / Docker Desktop

| Metric | Performance |
|--------|-------------|
| Discovery scan rate | 200 IPs/second |
| Banner grab timeout | 1,500ms |
| Timeline API (cached) | 15ms response |
| Query DSL (complex) | 220ms response |
| Database size | 4,092 observations = ~2MB |
| Worker throughput | 500 targets/5min (enrichment) |

## 🗺️ Roadmap

### Completed ✅
- [x] 3-tier scanning architecture
- [x] Timeline API with change detection
- [x] Query DSL with boolean operators
- [x] ASN/Geo enrichment
- [x] Webhook notifications
- [x] Web dashboard UI
- [x] Integration with CVE databases

### Planned 🚧
- [ ] Machine learning for threat scoring
- [ ] SNMP discovery support
- [ ] Active vulnerability scanning
- [ ] Multi-tenant support
- [ ] Grafana dashboards
- [ ] Elasticsearch integration
- [ ] API v2 with GraphQL

## 🤝 Contributing

Contributions welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [.NET 10](https://dotnet.microsoft.com/)
- UI powered by [Alpine.js](https://alpinejs.dev/) and [Tailwind CSS](https://tailwindcss.com/)
- Charts by [Chart.js](https://www.chartjs.org/)
- Inspired by network security tools like Shodan, Censys, and Nmap

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/nadosh/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/nadosh/discussions)
- **Email**: support@nadosh.io

---

**Built with ❤️ for network security professionals**
