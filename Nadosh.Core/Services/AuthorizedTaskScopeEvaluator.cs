using System.Text.Json;
using Nadosh.Core.Models;

namespace Nadosh.Core.Services;

public static class AuthorizedTaskScopeEvaluator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AuthorizedTaskScope Parse(string? scopeJson)
    {
        if (string.IsNullOrWhiteSpace(scopeJson))
        {
            return new AuthorizedTaskScope();
        }

        try
        {
            return JsonSerializer.Deserialize<AuthorizedTaskScope>(scopeJson, SerializerOptions) ?? new AuthorizedTaskScope();
        }
        catch
        {
            return new AuthorizedTaskScope();
        }
    }

    public static AuthorizedTaskScopeValidationResult ValidateTarget(string taskKind, AuthorizedTaskScope scope, string targetIp, IEnumerable<int>? ports = null)
    {
        if (string.IsNullOrWhiteSpace(targetIp))
        {
            return AuthorizedTaskScopeValidationResult.Deny($"{taskKind} is missing a target IP.");
        }

        if (!scope.RequireScopeMatch)
        {
            return ValidatePorts(taskKind, scope, ports);
        }

        var hasTargetScope = scope.AllowedTargets.Count > 0 || scope.AllowedCidrs.Count > 0;
        if (!hasTargetScope)
        {
            return AuthorizedTaskScopeValidationResult.Deny($"{taskKind} has no allowed targets or CIDRs in scope.");
        }

        var targetAllowed = scope.AllowedTargets.Contains(targetIp, StringComparer.OrdinalIgnoreCase)
            || scope.AllowedCidrs.Any(cidr => IsIpInCidr(targetIp, cidr));

        if (!targetAllowed)
        {
            return AuthorizedTaskScopeValidationResult.Deny($"Target {targetIp} is outside the authorized scope for {taskKind}.");
        }

        return ValidatePorts(taskKind, scope, ports);
    }

    private static AuthorizedTaskScopeValidationResult ValidatePorts(string taskKind, AuthorizedTaskScope scope, IEnumerable<int>? ports)
    {
        var requestedPorts = ports?.Distinct().ToArray() ?? Array.Empty<int>();
        if (requestedPorts.Length == 0 || scope.AllowedPorts.Count == 0)
        {
            return AuthorizedTaskScopeValidationResult.Allow();
        }

        var disallowedPorts = requestedPorts
            .Where(port => !scope.AllowedPorts.Contains(port))
            .OrderBy(port => port)
            .ToArray();

        return disallowedPorts.Length == 0
            ? AuthorizedTaskScopeValidationResult.Allow()
            : AuthorizedTaskScopeValidationResult.Deny($"Ports {string.Join(", ", disallowedPorts)} are outside the authorized scope for {taskKind}.");
    }

    private static bool IsIpInCidr(string ipAddress, string cidr)
    {
        try
        {
            var slashIndex = cidr.IndexOf('/');
            if (slashIndex < 0)
            {
                return System.Net.IPAddress.TryParse(cidr, out var cidrHost)
                    && System.Net.IPAddress.TryParse(ipAddress, out var targetHost)
                    && cidrHost.Equals(targetHost);
            }

            var networkPart = cidr[..slashIndex];
            if (!int.TryParse(cidr[(slashIndex + 1)..], out var prefixLength)
                || prefixLength < 0 || prefixLength > 32)
            {
                return false;
            }

            if (!System.Net.IPAddress.TryParse(networkPart, out var networkAddress)
                || networkAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }

            if (!System.Net.IPAddress.TryParse(ipAddress, out var targetAddress)
                || targetAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return false;
            }

            var mask = prefixLength == 0 ? 0u : ~((1u << (32 - prefixLength)) - 1u);
            var networkInt = IpToUint(networkAddress.GetAddressBytes());
            var targetInt = IpToUint(targetAddress.GetAddressBytes());
            return (networkInt & mask) == (targetInt & mask);
        }
        catch
        {
            return false;
        }
    }

    private static uint IpToUint(byte[] bytes)
        => (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
}
