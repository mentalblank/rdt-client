using System.ComponentModel;

namespace RdtClient.Data.Enums;

public enum MagnetTrackerEnrichment
{
    [Description("None (do not modify magnet links)")]
    None,

    [Description("Best trackers")]
    TrackersBest,

    [Description("All trackers")]
    TrackersAll,

    [Description("All UDP trackers")]
    TrackersAllUdp,

    [Description("All HTTP trackers")]
    TrackersAllHttp,

    [Description("All HTTPS trackers")]
    TrackersAllHttps,

    [Description("All WebSocket (WS) trackers")]
    TrackersAllWs,

    [Description("All I2P trackers")]
    TrackersAllI2P,

    [Description("Best IP-only trackers")]
    TrackersBestIp,

    [Description("All IP-only trackers")]
    TrackersAllIp
}
