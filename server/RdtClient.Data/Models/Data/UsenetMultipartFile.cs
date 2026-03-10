using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RdtClient.Data.Models.Data;

public class UsenetMultipartFile
{
    [Key]
    public Guid Id { get; set; } // foreign key to UsenetDavItem.Id
    
    public String Metadata { get; set; } = null!; // JSON blob

    [NotMapped]
    public Meta? MetadataObject
    {
        get
        {
            if (String.IsNullOrWhiteSpace(Metadata))
            {
                return null;
            }

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<Meta>(Metadata);
            }
            catch
            {
                return null;
            }
        }
        set => Metadata = System.Text.Json.JsonSerializer.Serialize(value);
    }

    [ForeignKey("Id")]
    [System.Text.Json.Serialization.JsonIgnore]
    public UsenetDavItem? DavItem { get; set; }

    public class Meta
    {
        public AesParams? AesParams { get; set; }
        public FilePart[] FileParts { get; set; } = [];
    }

    public class FilePart
    {
        public String[] SegmentIds { get; set; } = [];
        public LongRange SegmentIdByteRange { get; set; } = null!;
        public LongRange FilePartByteRange { get; set; } = null!;
    }
}
