namespace JevGen.Jev;

/// <summary>Builds provider endpoint addresses.</summary>
internal static class JevEndpoint
{
    /// <summary>
    /// Appends a provider's relative path to a base address, keeping any path the base address
    /// already carries.
    /// </summary>
    /// <remarks>
    /// Plain <see cref="Uri"/> resolution treats the last segment of a base address without a
    /// trailing slash as a file name and replaces it, so a gateway configured as
    /// <c>https://gateway.example.com/jev</c> would silently be called at
    /// <c>https://gateway.example.com/v1/systemone</c>. Providers append their path, as the
    /// documentation says, so the base address is treated as a directory either way.
    /// </remarks>
    public static Uri Combine(Uri baseAddress, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(relativePath);

        if (!baseAddress.IsAbsoluteUri)
        {
            return new Uri(baseAddress, relativePath);
        }

        if (!baseAddress.AbsolutePath.EndsWith('/'))
        {
            var builder = new UriBuilder(baseAddress);
            builder.Path += "/";
            baseAddress = builder.Uri;
        }

        return new Uri(baseAddress, relativePath);
    }
}
