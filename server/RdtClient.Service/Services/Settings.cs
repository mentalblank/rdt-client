using RdtClient.Data.Data;
using RdtClient.Data.Enums;
using RdtClient.Data.Models.Internal;
using Serilog.Core;
using Serilog.Events;

namespace RdtClient.Service.Services;

public class Settings(SettingData settingData)
{
    public static readonly LoggingLevelSwitch LoggingLevelSwitch = new(LogEventLevel.Debug);

    public static DbSettings Get => SettingData.Get;

    public static String GetDownloadPath(Provider? provider)
    {
        var path = provider switch
        {
            Provider.RealDebrid => Get.DownloadClient.DownloadPathRealDebrid,
            Provider.AllDebrid => Get.DownloadClient.DownloadPathAllDebrid,
            Provider.Premiumize => Get.DownloadClient.DownloadPathPremiumize,
            Provider.DebridLink => Get.DownloadClient.DownloadPathDebridLink,
            Provider.TorBox => Get.DownloadClient.DownloadPathTorBox,
            _ => null
        };

        if (String.IsNullOrWhiteSpace(path))
        {
            path = Get.DownloadClient.DownloadPath;

            if (provider != null)
            {
                path = Path.Combine(path, provider.ToString()!);
            }
        }

        return path;
    }

    public static String GetMappedPath(Provider? provider)
    {
        var path = provider switch
        {
            Provider.RealDebrid => Get.DownloadClient.MappedPathRealDebrid,
            Provider.AllDebrid => Get.DownloadClient.MappedPathAllDebrid,
            Provider.Premiumize => Get.DownloadClient.MappedPathPremiumize,
            Provider.DebridLink => Get.DownloadClient.MappedPathDebridLink,
            Provider.TorBox => Get.DownloadClient.MappedPathTorBox,
            _ => null
        };

        if (String.IsNullOrWhiteSpace(path))
        {
            path = Get.DownloadClient.MappedPath;
        }

        return path;
    }

    public static String AppDefaultSavePath
    {
        get
        {
            var downloadPath = Get.DownloadClient.MappedPath;

            downloadPath = downloadPath.TrimEnd('\\')
                                       .TrimEnd('/');

            downloadPath += Path.DirectorySeparatorChar;

            return downloadPath;
        }
    }

    public static String GetAppDefaultSavePath(Provider? provider)
    {
        var downloadPath = GetMappedPath(provider);

        downloadPath = downloadPath.TrimEnd('\\')
                                   .TrimEnd('/');

        downloadPath += Path.DirectorySeparatorChar;

        return downloadPath;
    }

    public async Task Update(IList<SettingProperty> settings)
    {
        await settingData.Update(settings);
    }

    public async Task Update(String settingId, Object? value)
    {
        await settingData.Update(settingId, value);
    }

    public async Task Seed()
    {
        await settingData.Seed();
    }

    public async Task ResetCache()
    {
        await settingData.ResetCache();

        LoggingLevelSwitch.MinimumLevel = Get.General.LogLevel switch
        {
            LogLevel.Verbose => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            _ => LogEventLevel.Warning
        };
    }
}
