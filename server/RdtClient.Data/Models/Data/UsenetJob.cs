using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RdtClient.Data.Enums;

namespace RdtClient.Data.Models.Data;

public class UsenetJob
{
    [Key]
    public Guid UsenetJobId { get; set; }

    public String Hash { get; set; } = null!;

    public String? Category { get; set; }

    public String JobName { get; set; } = null!;

    public String NzbFileName { get; set; } = null!;

    [System.Text.Json.Serialization.JsonIgnore]
    public String NzbContents { get; set; } = null!;

    public Int64 TotalSize { get; set; }

    public DateTimeOffset Added { get; set; }

    public DateTimeOffset? Completed { get; set; }

    public Int32 Priority { get; set; }

    public String? Error { get; set; }

    public TorrentStatus Status { get; set; }

    [NotMapped]
    public Int32 FileCount => Files.Count;

    [InverseProperty("UsenetJob")]
    public IList<UsenetFile> Files { get; set; } = [];
}
