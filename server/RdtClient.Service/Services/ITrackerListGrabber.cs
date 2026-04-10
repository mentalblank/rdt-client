namespace RdtClient.Service.Services;

public interface ITrackerListGrabber
{
    Task<String[]> GetEnrichmentTrackers();
    Task<String[]> GetBannedTrackers();
}
