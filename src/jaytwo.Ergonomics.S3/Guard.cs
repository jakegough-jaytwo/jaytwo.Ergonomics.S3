using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace jaytwo.Ergonomics.S3;

internal static class Guard
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void NotNull<T>([NotNull] T? value, string name)
        where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(name);
        }
    }
}
