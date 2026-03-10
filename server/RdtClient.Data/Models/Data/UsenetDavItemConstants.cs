namespace RdtClient.Data.Models.Data;

public static class UsenetDavItemConstants
{
    public static readonly UsenetDavItem Root = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000000"),
        ParentId = null,
        Name = "Usenet",
        FileSize = null,
        Type = UsenetDavItem.UsenetItemType.Directory,
        Path = "/",
        CreatedAt = DateTime.UnixEpoch
    };

    public static readonly UsenetDavItem NzbFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        ParentId = Root.Id,
        Name = "nzbs",
        FileSize = null,
        Type = UsenetDavItem.UsenetItemType.Directory,
        Path = "/nzbs",
        CreatedAt = DateTime.UnixEpoch
    };

    public static readonly UsenetDavItem ContentFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        ParentId = Root.Id,
        Name = "content",
        FileSize = null,
        Type = UsenetDavItem.UsenetItemType.Directory,
        Path = "/content",
        CreatedAt = DateTime.UnixEpoch
    };

    public static readonly UsenetDavItem IdsFolder = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
        ParentId = Root.Id,
        Name = ".ids",
        FileSize = null,
        Type = UsenetDavItem.UsenetItemType.IdsRoot,
        Path = "/.ids",
        CreatedAt = DateTime.UnixEpoch
    };
}
