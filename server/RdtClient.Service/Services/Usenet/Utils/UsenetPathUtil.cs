namespace RdtClient.Service.Services.Usenet.Utils;

public class UsenetPathUtil
{
    public static IEnumerable<String> GetAllParentDirectories(String path)
    {
        var directoryName = Path.GetDirectoryName(path);
        return !String.IsNullOrEmpty(directoryName)
            ? GetAllParentDirectories(directoryName).Prepend(directoryName)
            : [];
    }

    public static String ReplaceExtension(String path, String newExtensions)
    {
        var directoryName = Path.GetDirectoryName(path);
        var filenameWithoutExtension = Path.GetFileNameWithoutExtension(path);
        var newFilename = $"{filenameWithoutExtension}.{newExtensions.TrimStart('.')}";
        return Path.Join(directoryName, newFilename);
    }
}
