using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;

namespace RdtClient.Service.Services.Usenet.Queue.Aggregators;

public abstract class UsenetBaseAggregator
{
    protected abstract DataContext DataContext { get; }
    protected abstract UsenetDavItem MountDirectory { get; }

    private static readonly Char[] DirectorySeparators =
    [
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar
    ];

    /// <summary>
    /// Ensures that all parent-directories for the given `relativePath` exist.
    /// </summary>
    /// <param name="relativePath">The path at which to place a file, relative to the `MountDirectory`.</param>
    /// <returns>The parentDirectory UsenetDavItem</returns>
    protected UsenetDavItem EnsureParentDirectory(String relativePath)
    {
        var pathSegments = relativePath
            .Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        var parentDirectory = MountDirectory;
        var pathKey = "";
        for (var i = 0; i < pathSegments.Length - 1; i++)
        {
            pathKey = Path.Join(pathKey, pathSegments[i]);
            parentDirectory = EnsureDirectory(parentDirectory, pathSegments[i], pathKey);
        }

        return parentDirectory;
    }

    private readonly Dictionary<String, UsenetDavItem> _directoryCache = [];

    protected UsenetDavItem EnsureDirectory(UsenetDavItem parentDirectory, String directoryName, String pathKey)
    {
        if (_directoryCache.TryGetValue(pathKey, out var cachedDirectory)) return cachedDirectory;

        var directory = new UsenetDavItem
        {
            Id = Guid.NewGuid(),
            ParentId = parentDirectory.Id,
            Name = directoryName,
            FileSize = null,
            Type = UsenetDavItem.UsenetItemType.Directory,
            Path = Path.Join(parentDirectory.Path, directoryName),
            CreatedAt = DateTime.Now
        };
        
        _directoryCache.Add(pathKey, directory);
        DataContext.UsenetDavItems.Add(directory);
        return directory;
    }
}
