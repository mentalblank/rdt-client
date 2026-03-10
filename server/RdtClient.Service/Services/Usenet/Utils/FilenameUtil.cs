using System.Text.RegularExpressions;

namespace RdtClient.Service.Services.Usenet.Utils;

public partial class FilenameUtil
{
    [GeneratedRegex(@"(?<rm>[\s-]*(?:(?<br>{{)|password=)(?<pw>\w+)(?(br)}}))\.nzb$", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();

    private static readonly HashSet<String> VideoExtensions =
    [
        ".webm", ".m4v", ".3gp", ".nsv", ".ty", ".strm", ".rm", ".rmvb", ".m3u", ".ifo", ".mov", ".qt", ".divx",
        ".xvid", ".bivx", ".nrg", ".pva", ".wmv", ".asf", ".asx", ".ogm", ".ogv", ".m2v", ".avi", ".bin", ".dat",
        ".dvr-ms", ".mpg", ".mpeg", ".mp4", ".avc", ".vp3", ".svq3", ".nuv", ".viv", ".dv", ".fli", ".flv", ".wpl",
        ".img", ".iso", ".vob", ".mkv", ".mk3d", ".ts", ".wtv", ".m2ts"
    ];

    public static Boolean IsImportantFileType(String filename)
    {
        return IsVideoFile(filename)
               || IsRarFile(filename)
               || Is7zFile(filename)
               || IsMultipartMkv(filename);
    }

    public static Boolean IsVideoFile(String filename)
    {
        var ext = Path.GetExtension(filename).ToLower();
        return VideoExtensions.Contains(ext);
    }

    public static Boolean IsRarFile(String? filename)
    {
        if (String.IsNullOrEmpty(filename)) return false;
        return filename.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
               || Regex.IsMatch(filename, @"\.r(\d+)$", RegexOptions.IgnoreCase);
    }

    public static Boolean Is7zFile(String? filename)
    {
        if (String.IsNullOrEmpty(filename)) return false;
        return Regex.IsMatch(filename, @"\.7z(\.(\d+))?$", RegexOptions.IgnoreCase);
    }

    public static Boolean IsMultipartMkv(String? filename)
    {
        if (String.IsNullOrEmpty(filename)) return false;
        return Regex.IsMatch(filename, @"\.mkv\.(\d+)?$", RegexOptions.IgnoreCase);
    }

    public static String GetJobName(String filename)
    {
        var passMatch = PasswordRegex().Match(filename);
        return Path.GetFileNameWithoutExtension(
            passMatch.Success ?
            filename.Replace(passMatch.Groups["rm"].Value, "") :
            filename
        );
    }

    public static String? GetNzbPassword(String filename)
    {
        var passMatch = PasswordRegex().Match(filename);
        return passMatch.Success ? passMatch.Groups["pw"].Value : null;
    }
}
