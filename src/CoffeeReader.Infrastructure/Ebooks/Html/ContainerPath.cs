namespace CoffeeReader.Infrastructure.Ebooks.Html;

/// <summary>
/// Paths inside a book container (zip entries, resource names). Always '/'-separated and relative
/// to the container root; never touch the real file system.
/// </summary>
public static class ContainerPath
{
    /// <summary>
    /// Resolves an href found in <paramref name="baseDirectory"/> into a container path, dropping any fragment.
    /// </summary>
    public static string Resolve(string baseDirectory, string href)
    {
        var withoutFragment = SplitFragment(href).Path;
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(withoutFragment);
        }
        catch (UriFormatException)
        {
            decoded = withoutFragment;
        }

        decoded = decoded.Replace('\\', '/');
        var combined = decoded.StartsWith('/') || baseDirectory.Length == 0 ? decoded : $"{baseDirectory}/{decoded}";
        var stack = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(segment);
        }

        return string.Join('/', stack);
    }

    public static (string Path, string? Fragment) SplitFragment(string href)
    {
        var hash = href.IndexOf('#');
        if (hash < 0)
        {
            return (href, null);
        }

        var fragment = href[(hash + 1)..];
        return (href[..hash], fragment.Length == 0 ? null : Uri.UnescapeDataString(fragment));
    }

    public static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }
}
