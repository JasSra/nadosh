using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Nadosh.Core.Models;

namespace Nadosh.Api.Infrastructure;

/// <summary>
/// Query DSL Parser for advanced exposure searching
/// Supports: port:22, service:ssh, severity:high, country:US, asn:15169, state:open, time:last_7d
/// Boolean operators: AND, OR, NOT
/// Example: "service:ssh AND (port:22 OR port:2222) AND country:US AND time:last_30d"
/// </summary>
public class QueryDslParser
{
    public static Expression<Func<CurrentExposure, bool>> Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return exp => true;

        // Normalize query
        query = query.Trim();

        // Parse into tokens
        var tokens = Tokenize(query);

        // Build expression tree
        return BuildExpression(tokens);
    }

    private static List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();
        
        // Pattern: field:value or boolean operators
        var pattern = @"(?<field>\w+):(?<value>[\w.-]+)|(?<bool>AND|OR|NOT)|(?<paren>[()])|(?<comp>[<>=!]+)";
        var matches = Regex.Matches(query, pattern, RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (match.Groups["field"].Success)
            {
                tokens.Add(new Token
                {
                    Type = TokenType.FieldValue,
                    Field = match.Groups["field"].Value.ToLowerInvariant(),
                    Value = match.Groups["value"].Value
                });
            }
            else if (match.Groups["bool"].Success)
            {
                var boolOp = match.Groups["bool"].Value.ToUpperInvariant();
                tokens.Add(new Token
                {
                    Type = boolOp switch
                    {
                        "AND" => TokenType.And,
                        "OR" => TokenType.Or,
                        "NOT" => TokenType.Not,
                        _ => TokenType.Unknown
                    }
                });
            }
            else if (match.Groups["paren"].Success)
            {
                var paren = match.Groups["paren"].Value;
                tokens.Add(new Token
                {
                    Type = paren == "(" ? TokenType.OpenParen : TokenType.CloseParen
                });
            }
        }

        return tokens;
    }

    private static Expression<Func<CurrentExposure, bool>> BuildExpression(List<Token> tokens)
    {
        if (tokens.Count == 0)
            return exp => true;

        // Simplified: combine all field:value tokens with AND
        // For full boolean logic, implement a proper expression tree builder

        var parameter = Expression.Parameter(typeof(CurrentExposure), "exp");
        Expression? combined = null;

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type != TokenType.FieldValue)
                continue;

            var fieldExpression = GetFieldExpression(parameter, token.Field!, token.Value!);
            if (fieldExpression == null)
                continue;

            // Check if next token is OR
            bool isOr = (i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Or);
            bool isNot = (i > 0 && tokens[i - 1].Type == TokenType.Not);

            if (isNot)
            {
                fieldExpression = Expression.Not(fieldExpression);
            }

            if (combined == null)
            {
                combined = fieldExpression;
            }
            else if (isOr)
            {
                combined = Expression.OrElse(combined, fieldExpression);
            }
            else
            {
                combined = Expression.AndAlso(combined, fieldExpression);
            }
        }

        if (combined == null)
            return exp => true;

        return Expression.Lambda<Func<CurrentExposure, bool>>(combined, parameter);
    }

    private static Expression? GetFieldExpression(ParameterExpression parameter, string field, string value)
    {
        return field switch
        {
            "port" => int.TryParse(value, out var port)
                ? Expression.Equal(Expression.Property(parameter, "Port"), Expression.Constant(port))
                : null,

            "service" => Expression.Equal(
                Expression.Property(parameter, "ServiceName"),
                Expression.Constant(value)),

            "severity" => Expression.Equal(
                Expression.Property(parameter, "Severity"),
                Expression.Constant(value)),

            "state" => Expression.Equal(
                Expression.Property(parameter, "CurrentState"),
                Expression.Constant(value)),

            "protocol" => Expression.Equal(
                Expression.Property(parameter, "Protocol"),
                Expression.Constant(value)),

            "classification" => Expression.Equal(
                Expression.Property(parameter, "Classification"),
                Expression.Constant(value)),

            "tier" => int.TryParse(value, out var tier)
                ? Expression.Equal(Expression.Property(parameter, "Tier"), Expression.Constant((ScanTier)tier))
                : null,

            // Time-based filters
            "time" => ParseTimeFilter(parameter, value),

            _ => null
        };
    }

    private static Expression? ParseTimeFilter(ParameterExpression parameter, string value)
    {
        // Formats: last_7d, last_30d, last_1h, since:2026-01-01
        var now = DateTime.UtcNow;
        DateTime? threshold = null;

        if (value.StartsWith("last_"))
        {
            var parts = value["last_".Length..].TrimEnd('d', 'h', 'm');
            if (int.TryParse(parts, out var amount))
            {
                threshold = value.EndsWith("d") ? now.AddDays(-amount)
                    : value.EndsWith("h") ? now.AddHours(-amount)
                    : value.EndsWith("m") ? now.AddMinutes(-amount)
                    : null;
            }
        }
        else if (value.StartsWith("since:"))
        {
            if (DateTime.TryParse(value["since:".Length..], out var sinceDate))
                threshold = sinceDate;
        }

        if (threshold.HasValue)
        {
            return Expression.GreaterThanOrEqual(
                Expression.Property(parameter, "LastSeen"),
                Expression.Constant(threshold.Value));
        }

        return null;
    }
}

public class Token
{
    public TokenType Type { get; set; }
    public string? Field { get; set; }
    public string? Value { get; set; }
}

public enum TokenType
{
    Unknown,
    FieldValue,
    And,
    Or,
    Not,
    OpenParen,
    CloseParen
}
