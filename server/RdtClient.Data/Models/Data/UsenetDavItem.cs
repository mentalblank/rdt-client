using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace RdtClient.Data.Models.Data;

public class UsenetDavItem
{
    [Key]
    public Guid Id { get; init; }
    
    public DateTime CreatedAt { get; init; }
    
    public Guid? ParentId { get; init; }
    
    [MaxLength(255)]
    public String Name { get; set; } = null!;
    
    public Int64? FileSize { get; set; }
    
    public UsenetItemType Type { get; init; }
    
    public String Path { get; set; } = null!;
    
    public DateTimeOffset? ReleaseDate { get; set; }
    
    public DateTimeOffset? LastHealthCheck { get; set; }
    
    public DateTimeOffset? NextHealthCheck { get; set; }

    public enum UsenetItemType
    {
        Directory = 1,
        SymlinkRoot = 2,
        NzbFile = 3,
        RarFile = 4,
        IdsRoot = 5,
        MultipartFile = 6,
    }

    [JsonIgnore]
    [ForeignKey("ParentId")]
    public UsenetDavItem? Parent { get; set; }

    [JsonIgnore]
    public ICollection<UsenetDavItem> Children { get; set; } = new List<UsenetDavItem>();
}
