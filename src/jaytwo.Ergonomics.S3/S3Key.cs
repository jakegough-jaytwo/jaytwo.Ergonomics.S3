using System;

namespace jaytwo.Ergonomics.S3;

/// <summary>
/// Prefix rules used by <see cref="S3Client"/>. Trailing slashes are normalized;
/// object keys are otherwise left as the caller wrote them.
/// </summary>
public static class S3Key
{
    public static string NormalizePrefix(string? keyPrefix)
    {
        if (string.IsNullOrEmpty(keyPrefix))
        {
            return string.Empty;
        }

        return keyPrefix.TrimEnd('/') + "/";
    }

    public static string Combine(string? prefix, string? key)
    {
        var normalized = NormalizePrefix(prefix);
        if (string.IsNullOrEmpty(key))
        {
            return normalized;
        }

        if (normalized.Length == 0)
        {
            return key;
        }

        return normalized + key;
    }

    public static string StripPrefix(string fullKey, string? prefix)
    {
        Guard.NotNull(fullKey, nameof(fullKey));

        var normalized = NormalizePrefix(prefix);
        if (normalized.Length == 0)
        {
            return fullKey;
        }

        if (!fullKey.StartsWith(normalized, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("S3 key escaped configured KeyPrefix.");
        }

        return fullKey.Substring(normalized.Length);
    }
}
