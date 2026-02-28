using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace RdtClient.Data.Models.Data;

public class UsenetFile
{
    [Key]
    public Guid UsenetFileId { get; set; }

    public Guid UsenetJobId { get; set; }

    [ForeignKey("UsenetJobId")]
    [InverseProperty("Files")]
    [System.Text.Json.Serialization.JsonIgnore]
    public UsenetJob UsenetJob { get; set; } = null!;

    public String Path { get; set; } = null!;

    public Int64 Size { get; set; }

    public String SegmentIds { get; set; } = null!;

    [NotMapped]
    public String[] SegmentIdList
    {
        get
        {
            if (String.IsNullOrWhiteSpace(SegmentIds))
            {
                return Array.Empty<String>();
            }

            try
            {
                return JsonSerializer.Deserialize<String[]>(SegmentIds) ?? Array.Empty<String>();
            }
            catch
            {
                return Array.Empty<String>();
            }
        }
        set => SegmentIds = JsonSerializer.Serialize(value);
    }
}
