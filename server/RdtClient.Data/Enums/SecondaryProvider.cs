using System.ComponentModel;

namespace RdtClient.Data.Enums;

public enum SecondaryProvider
{
    [Description("RealDebrid")]
    RealDebrid,

    [Description("AllDebrid")]
    AllDebrid,

    [Description("Premiumize")]
    Premiumize,

    [Description("TorBox")]
    TorBox,
    
    [Description("NoProvider")]
    NoProvider
}