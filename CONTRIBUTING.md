# CONTRIBUTING.md

## Contributing to Nadosh

Thank you for your interest in contributing to Nadosh! This document provides guidelines and instructions for contributing.

## Getting Started

1. **Fork the Repository**
   - Fork the repo on GitHub
   - Clone your fork locally

2. **Set Up Development Environment**
   ```bash
   # Install .NET 10 SDK
   # Install Docker Desktop
   
   # Clone and setup
   git clone https://github.com/YOUR_USERNAME/nadosh.git
   cd nadosh
   
   # Restore dependencies
   dotnet restore
   
   # Start services
   docker compose up -d postgres redis
   
   # Run migrations
   dotnet ef database update --project Nadosh.Infrastructure --startup-project Nadosh.Api
   ```

3. **Create a Branch**
   ```bash
   git checkout -b feature/your-feature-name
   ```

## Development Guidelines

### Code Style
- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation comments for public APIs
- Keep methods focused and small
- Use async/await for I/O operations

### Commit Messages
Follow the Conventional Commits specification:

```
feat: add timeline filtering by date range
fix: resolve race condition in worker queue
docs: update API documentation
refactor: simplify query DSL parser
test: add unit tests for enrichment worker
```

### Pull Request Process

1. **Update Documentation**
   - Update README.md if adding features
   - Add/update API documentation
   - Include code comments

2. **Test Your Changes**
   ```bash
   dotnet test
   dotnet build
   ```

3. **Submit PR**
   - Reference any related issues
   - Describe what changed and why
   - Include screenshots for UI changes

4. **Code Review**
   - Address review feedback
   - Keep commits clean and atomic

## Project Structure

```
nadosh/
├── Nadosh.Api/              # REST API + Web UI
│   ├── Controllers/         # API endpoints
│   ├── Infrastructure/      # API middleware
│   └── wwwroot/            # Static web assets
├── Nadosh.Workers/          # Background workers
│   └── Workers/            # Worker implementations
├── Nadosh.Core/            # Domain models
│   └── Models/             # Entity definitions
└── Nadosh.Infrastructure/  # Data access
    ├── Data/               # DbContext
    └── Migrations/         # EF Core migrations
```

## Testing

### Unit Tests
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Integration Tests
```bash
# Start test environment
docker compose -f docker-compose.test.yml up -d

# Run integration tests
dotnet test --filter Category=Integration
```

## Adding Features

### New API Endpoint
1. Add controller in `Nadosh.Api/Controllers/`
2. Add route with `[ApiKeyAuth]` attribute
3. Document with XML comments
4. Add to README API reference

### New Worker
1. Create in `Nadosh.Workers/Workers/`
2. Inherit from `BackgroundService`
3. Add configuration to `appsettings.json`
4. Register in `Program.cs`

### Database Changes
1. Update model in `Nadosh.Core/Models/`
2. Create migration: `dotnet ef migrations add MigrationName`
3. Test migration: `dotnet ef database update`
4. Document schema changes

## Questions?

- Open a [GitHub Discussion](https://github.com/yourusername/nadosh/discussions)
- File an [Issue](https://github.com/yourusername/nadosh/issues) for bugs

Thank you for contributing! 🎉
