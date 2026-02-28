namespace RdtClient.Service.Services.Usenet.Models;

public record LongRange(Int64 StartInclusive, Int64 EndExclusive)
{
    public Int64 Count => EndExclusive - StartInclusive;

    public Boolean Contains(Int64 value) =>
        value >= StartInclusive && value < EndExclusive;

    public Boolean Contains(LongRange range) =>
        range.StartInclusive >= StartInclusive && range.EndExclusive <= EndExclusive;

    public Boolean IsContainedWithin(LongRange range) =>
        range.Contains(this);

    public static LongRange FromStartAndSize(Int64 startInclusive, Int64 size) =>
        new(startInclusive, startInclusive + size);
}
