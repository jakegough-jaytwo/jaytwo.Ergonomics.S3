using System.Collections.Generic;
using Amazon.S3.Model;

namespace jaytwo.Ergonomics.S3;

public static class MetadataCollectionExtensions
{
    public static IDictionary<string, string> ToDictionary(this MetadataCollection metadata)
    {
        Guard.NotNull(metadata, nameof(metadata));

        var result = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var key in metadata.Keys)
        {
            result[key] = metadata[key];
        }

        return result;
    }
}
