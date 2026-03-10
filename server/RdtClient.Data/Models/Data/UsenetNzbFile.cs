using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RdtClient.Data.Models.Data;

public class UsenetNzbFile
{
    [Key]
    public Guid Id { get; set; } // foreign key to UsenetDavItem.Id
    
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
                return System.Text.Json.JsonSerializer.Deserialize<String[]>(SegmentIds) ?? Array.Empty<String>();
            }
            catch
            {
                return Array.Empty<String>();
            }
        }
        set => SegmentIds = System.Text.Json.JsonSerializer.Serialize(value);
    }

    [ForeignKey("Id")]
    [System.Text.Json.Serialization.JsonIgnore]
    public UsenetDavItem? DavItem { get; set; }
}
