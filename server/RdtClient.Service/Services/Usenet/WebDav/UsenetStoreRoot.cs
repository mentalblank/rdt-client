using System.Runtime.CompilerServices;
using NWebDav.Server.Stores;
using RdtClient.Data.Data;
using RdtClient.Data.Models.Data;
using RdtClient.Service.Services.Usenet.WebDav.Base;

namespace RdtClient.Service.Services.Usenet.WebDav;

public class UsenetStoreRoot(DataContext dataContext, INntpClient usenetClient) : UsenetStoreCollection(UsenetDavItemConstants.Root, dataContext, usenetClient)
{
    public override String Name => "Usenet";
    public override String UniqueKey => "UsenetRoot";
    public override DateTime CreatedAt => DateTime.UnixEpoch;
}
