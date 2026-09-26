using System.Collections.Generic;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

public static class HeadersCollectionExtensions
{
    public static IDictionary<string, string> ToDictionary(this HeadersCollection headers)
    {
        Guard.NotNull(headers, nameof(headers));

        var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var key in headers.Keys)
        {
            result[key] = headers[key];
        }

        return result;
    }
}
