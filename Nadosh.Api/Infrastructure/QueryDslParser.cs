using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Nadosh.Core.Models;

namespace Nadosh.Api.Infrastructure;

/// <summary>
/// Query DSL Parser for advanced exposure searching
/// Supports indexed exposure fields such as:
/// ip:192.168.1.10, port:22, service:ssh, severity:high, state:open, protocol:tcp, time:last_7d
/// Boolean operators: AND, OR, NOT
/// Example: "service:ssh AND (port:22 OR port:2222) AND country:US AND time:last_30d"
/// </summary>
public class QueryDslParser
{
    private static readonly MethodInfo StringToLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo StringContainsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

    public static Expression<Func<CurrentExposure, bool>> Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return exp => true;

        var tokens = Tokenize(query.Trim());
        if (tokens.Count == 0)
            throw new FormatException("Query does not contain any valid filter terms.");

        var parameter = Expression.Parameter(typeof(CurrentExposure), "exp");
        var reader = new TokenReader(tokens);
        var body = ParseOrExpression(reader, parameter);

        if (!reader.IsAtEnd)
            throw new FormatException($"Unexpected token '{reader.Current.DisplayValue}'.");

        return Expression.Lambda<Func<CurrentExposure, bool>>(body, parameter);
    }

    private static List<Token> Tokenize(string query)
    {
        var tokens = new List<Token>();

        for (var index = 0; index < query.Length;)
        {
            if (char.IsWhiteSpace(query[index]))
            {
                index++;
                continue;
            }

            if (query[index] == '(' || query[index] == ')')
            {
                tokens.Add(new Token
                {
                    Type = query[index] == '(' ? TokenType.OpenParen : TokenType.CloseParen,
                    DisplayValue = query[index].ToString()
                });
                index++;
                continue;
            }

            var start = index;
            var inQuotes = false;

            while (index < query.Length)
            {
                var current = query[index];

                if (current == '"')
                {
                    inQuotes = !inQuotes;
                    index++;
                    continue;
                }

                if (!inQuotes && (char.IsWhiteSpace(current) || current == '(' || current == ')'))
                    break;

                index++;
            }

            if (inQuotes)
                throw new FormatException("Unterminated quoted value in query.");

            var rawToken = query[start..index];
            if (string.IsNullOrWhiteSpace(rawToken))
                continue;

            if (rawToken.Equals("AND", StringComparison.OrdinalIgnoreCase)
                || rawToken.Equals("OR", StringComparison.OrdinalIgnoreCase)
                || rawToken.Equals("NOT", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add(new Token
                {
                    Type = rawToken.ToUpperInvariant() switch
                    {
                        "AND" => TokenType.And,
                        "OR" => TokenType.Or,
                        "NOT" => TokenType.Not,
                        _ => TokenType.Unknown
                    },
                    DisplayValue = rawToken
                });

                continue;
            }

            var separatorIndex = rawToken.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == rawToken.Length - 1)
                throw new FormatException($"Invalid token '{rawToken}'. Expected field:value format.");

            var field = rawToken[..separatorIndex].Trim().ToLowerInvariant();
            var value = rawToken[(separatorIndex + 1)..].Trim();

            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                value = value[1..^1];

            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException($"Field '{field}' is missing a value.");

            tokens.Add(new Token
            {
                Type = TokenType.FieldValue,
                Field = field,
                Value = value,
                DisplayValue = rawToken
            });
        }

        return tokens;
    }

    private static Expression ParseOrExpression(TokenReader reader, ParameterExpression parameter)
    {
        var left = ParseAndExpression(reader, parameter);

        while (reader.Match(TokenType.Or))
        {
            var right = ParseAndExpression(reader, parameter);
            left = Expression.OrElse(left, right);
        }

        return left;
    }

    private static Expression ParseAndExpression(TokenReader reader, ParameterExpression parameter)
    {
        var left = ParseUnaryExpression(reader, parameter);

        while (reader.Match(TokenType.And) || reader.PeekStartsExpression())
        {
            var right = ParseUnaryExpression(reader, parameter);
            left = Expression.AndAlso(left, right);
        }

        return left;
    }

    private static Expression ParseUnaryExpression(TokenReader reader, ParameterExpression parameter)
    {
        if (reader.Match(TokenType.Not))
            return Expression.Not(ParseUnaryExpression(reader, parameter));

        return ParsePrimaryExpression(reader, parameter);
    }

    private static Expression ParsePrimaryExpression(TokenReader reader, ParameterExpression parameter)
    {
        if (reader.Match(TokenType.OpenParen))
        {
            var nested = ParseOrExpression(reader, parameter);
            reader.Expect(TokenType.CloseParen, "Missing closing ')' in query.");
            return nested;
        }

        if (reader.TryReadFieldValue(out var token))
            return BuildFieldExpression(parameter, token.Field!, token.Value!);

        throw new FormatException($"Unexpected token '{reader.Current.DisplayValue}'.");
    }

    private static Expression BuildFieldExpression(ParameterExpression parameter, string field, string value)
    {
        return field switch
        {
            "port" => int.TryParse(value, out var port)
                ? Expression.Equal(Expression.Property(parameter, "Port"), Expression.Constant(port))
                : throw new FormatException($"'{value}' is not a valid port number."),

            "ip" or "target" => BuildStringEqualsExpression(parameter, nameof(CurrentExposure.TargetId), value),
            "service" or "classification" => BuildStringEqualsExpression(parameter, nameof(CurrentExposure.Classification), value),
            "severity" => BuildStringEqualsExpression(parameter, nameof(CurrentExposure.Severity), value),
            "state" => BuildStringEqualsExpression(parameter, nameof(CurrentExposure.CurrentState), value),
            "protocol" => BuildStringEqualsExpression(parameter, nameof(CurrentExposure.Protocol), value),
            "mac" or "macaddress" => BuildStringEqualsExpression(parameter, nameof(CurrentExposure.MacAddress), value),
            "macvendor" => BuildStringContainsExpression(parameter, nameof(CurrentExposure.MacVendor), value),
            "devicetype" => BuildStringEqualsExpression(parameter, nameof(CurrentExposure.DeviceType), value),
            "summary" => BuildStringContainsExpression(parameter, nameof(CurrentExposure.CachedSummary), value),
            "time" => ParseTimeFilter(parameter, value),
            _ => throw new FormatException($"Field '{field}' is not supported by the current exposure index.")
        };
    }

    private static Expression BuildStringEqualsExpression(ParameterExpression parameter, string propertyName, string value)
    {
        var property = Expression.Property(parameter, propertyName);
        var normalizedProperty = Expression.Call(property, StringToLowerMethod);
        var normalizedValue = Expression.Constant(value.ToLowerInvariant());
        var notNull = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));

        return Expression.AndAlso(notNull, Expression.Equal(normalizedProperty, normalizedValue));
    }

    private static Expression BuildStringContainsExpression(ParameterExpression parameter, string propertyName, string value)
    {
        var property = Expression.Property(parameter, propertyName);
        var normalizedProperty = Expression.Call(property, StringToLowerMethod);
        var normalizedValue = Expression.Constant(value.ToLowerInvariant());
        var notNull = Expression.NotEqual(property, Expression.Constant(null, typeof(string)));

        return Expression.AndAlso(notNull, Expression.Call(normalizedProperty, StringContainsMethod, normalizedValue));
    }

    private static Expression ParseTimeFilter(ParameterExpression parameter, string value)
    {
        var normalized = value.ToLowerInvariant();
        var now = DateTime.UtcNow;
        DateTime? threshold = null;

        if (normalized.StartsWith("last_", StringComparison.Ordinal))
        {
            var unit = normalized[^1];
            var parts = normalized["last_".Length..].TrimEnd('d', 'h', 'm');

            if (int.TryParse(parts, out var amount))
            {
                threshold = unit == 'd' ? now.AddDays(-amount)
                    : unit == 'h' ? now.AddHours(-amount)
                    : unit == 'm' ? now.AddMinutes(-amount)
                    : null;
            }
        }
        else if (normalized.StartsWith("since:", StringComparison.Ordinal)
            && DateTime.TryParse(
                value["since:".Length..],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var sinceDate))
        {
            threshold = sinceDate;
        }

        if (!threshold.HasValue)
            throw new FormatException($"Unsupported time filter '{value}'. Use last_7d, last_12h, last_30m, or since:YYYY-MM-DD.");

        return Expression.GreaterThanOrEqual(
            Expression.Property(parameter, nameof(CurrentExposure.LastSeen)),
            Expression.Constant(threshold.Value));
    }
}

public class Token
{
    public TokenType Type { get; set; }
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string DisplayValue { get; set; } = string.Empty;
}

internal sealed class TokenReader
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _index;

    public TokenReader(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    public bool IsAtEnd => _index >= _tokens.Count;

    public Token Current => IsAtEnd
        ? new() { Type = TokenType.EndOfInput, DisplayValue = "<end of query>" }
        : _tokens[_index];

    public bool Match(TokenType expected)
    {
        if (Current.Type != expected)
            return false;

        _index++;
        return true;
    }

    public void Expect(TokenType expected, string message)
    {
        if (!Match(expected))
            throw new FormatException(message);
    }

    public bool TryReadFieldValue(out Token token)
    {
        if (Current.Type == TokenType.FieldValue)
        {
            token = Current;
            _index++;
            return true;
        }

        token = new Token { Type = TokenType.Unknown, DisplayValue = "<invalid>" };
        return false;
    }

    public bool PeekStartsExpression()
        => Current.Type is TokenType.FieldValue or TokenType.OpenParen or TokenType.Not;
}

public enum TokenType
{
    Unknown,
    EndOfInput,
    FieldValue,
    And,
    Or,
    Not,
    OpenParen,
    CloseParen
}
