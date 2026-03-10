# Security Policy

## Supported Versions

We release patches for security vulnerabilities for the following versions:

| Version | Supported          |
| ------- | ------------------ |
| main    | :white_check_mark: |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Instead, please report them via email to: **security@nadosh.io** (or your preferred contact)

You should receive a response within 48 hours. If for some reason you do not, please follow up to ensure we received your original message.

Please include the following information:

- Type of issue (e.g. buffer overflow, SQL injection, XSS, etc.)
- Full paths of source file(s) related to the issue
- Location of the affected source code (tag/branch/commit or direct URL)
- Any special configuration required to reproduce the issue
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the issue, including how an attacker might exploit it

## What to Expect

- We will acknowledge your email within 48 hours
- We will provide a detailed response within 7 days
- We will keep you informed of our progress
- We will notify you when the vulnerability is fixed
- We will publicly acknowledge your responsible disclosure (unless you prefer to remain anonymous)

## Security Best Practices

When deploying Nadosh in production:

1. **Change Default Credentials**
   - Update all passwords in docker-compose.yml
   - Generate strong, unique API keys
   - Use environment variables or secrets management

2. **Network Security**
   - Run PostgreSQL and Redis on private networks
   - Use firewall rules to restrict access
   - Enable TLS/SSL for API endpoints
   - Use VPN or private networking for remote access

3. **Docker Security**
   - Use specific image tags, not `latest`
   - Scan images for vulnerabilities
   - Run containers as non-root users
   - Limit container resources

4. **Data Protection**
   - Encrypt sensitive data at rest
   - Use encrypted connections (TLS)
   - Regular backups with encryption
   - Implement proper access controls

5. **Regular Updates**
   - Keep base images updated
   - Update dependencies regularly
   - Monitor security advisories
   - Apply security patches promptly

## Known Security Considerations

### Current Implementation Notes

1. **Development Mode Defaults**: The default configuration uses weak credentials suitable only for development
2. **API Key Authentication**: Uses simple header-based authentication; implement OAuth2/JWT for production
3. **No Rate Limiting Per User**: Current rate limiting is per API key; consider per-IP limits
4. **CORS**: Default configuration allows all origins; restrict in production

## Security Hardening Checklist

Before production deployment:

- [ ] Change all default passwords and API keys
- [ ] Enable HTTPS/TLS for API
- [ ] Configure firewall rules
- [ ] Restrict database and Redis access
- [ ] Enable Docker security features
- [ ] Set up monitoring and alerting
- [ ] Implement backup and recovery procedures
- [ ] Review and update CORS policies
- [ ] Enable audit logging
- [ ] Set up secrets management (Azure Key Vault, HashiCorp Vault, etc.)

## Contact

For security-related questions: security@nadosh.io
For general questions: See README.md
