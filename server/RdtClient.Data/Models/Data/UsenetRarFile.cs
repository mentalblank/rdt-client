using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RdtClient.Data.Models.Data;

public class UsenetRarFile
{
    [Key]
    public Guid Id { get; set; } // foreign key to UsenetDavItem.Id
    
    public String RarParts { get; set; } = null!;

    [NotMapped]
    public RarPart[] RarPartList
    {
        get
        {
            if (String.IsNullOrWhiteSpace(RarParts))
            {
                return Array.Empty<RarPart>();
            }

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<RarPart[]>(RarParts) ?? Array.Empty<RarPart>();
            }
            catch
            {
                return Array.Empty<RarPart>();
            }
        }
        set => RarParts = System.Text.Json.JsonSerializer.Serialize(value);
    }

    [ForeignKey("Id")]
    [System.Text.Json.Serialization.JsonIgnore]
    public UsenetDavItem? DavItem { get; set; }

    public UsenetMultipartFile.Meta ToUsenetMultipartFileMeta()
    {
        return new UsenetMultipartFile.Meta
        {
            FileParts = RarPartList.Select(x => new UsenetMultipartFile.FilePart()
            {
                SegmentIds = x.SegmentIds,
                SegmentIdByteRange = LongRange.FromStartAndSize(0, x.PartSize),
                FilePartByteRange = LongRange.FromStartAndSize(x.Offset, x.ByteCount),
            }).ToArray()
        };
    }

    public class RarPart
    {
        public String[] SegmentIds { get; set; } = [];
        public Int64 PartSize { get; set; }
        public Int64 Offset { get; set; }
        public Int64 ByteCount { get; set; }
    }
}
