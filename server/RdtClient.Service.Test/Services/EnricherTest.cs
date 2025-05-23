using Microsoft.Extensions.Logging;
using Moq;
using RdtClient.Service.Services;
using MonoTorrent.BEncoding;

namespace RdtClient.Service.Test.Services;

public class EnricherTest : IDisposable
{
    private readonly MockRepository _mockRepository;
    private readonly Mock<ILogger<Enricher>> _loggerMock;
    private readonly Mock<ITrackerListGrabber> _trackerListGrabberMock;

    public EnricherTest()
    {
        _mockRepository = new MockRepository(MockBehavior.Strict);
        _loggerMock = _mockRepository.Create<ILogger<Enricher>>(MockBehavior.Loose);
        _trackerListGrabberMock = _mockRepository.Create<ITrackerListGrabber>();
    }

    public void Dispose()
    {
        _mockRepository.VerifyAll();
    }

    private const String TestMagnetLink =
        "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=TestFile&tr=http%3A%2F%2Ftracker1.com%2Fannounce&tr=http%3A%2F%2Ftracker2.com%2Fannounce";

    [Fact]
    public async Task EnrichMagnetLink_AddsNoTrackers_WhenNoTrackersFromTrackerGrabber()
    {
        // Arrange
        SetupTrackerListGrabber([]);

        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enriched = await enricher.EnrichMagnetLink(TestMagnetLink);

        // Assert
        Assert.Equal(TestMagnetLink, enriched);
    }

    [Fact]
    public async Task EnrichMagnetLink_AddsTrackers_WhenTrackersFromTrackerGrabber()
    {
        // Arrange
        SetupTrackerListGrabber(["http://new-tracker.com/announce"]);

        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enriched = await enricher.EnrichMagnetLink(TestMagnetLink);

        // Assert
        Assert.Equal(TestMagnetLink + $"&tr={Uri.EscapeDataString("http://new-tracker.com/announce")}", enriched);
    }

    [Fact]
    public async Task EnrichMagnetLink_DoesNotAddDuplicateTrackers_WhenTrackersFromTrackerGrabberAlreadyPresent()
    {
        // Arrange
        SetupTrackerListGrabber(["http://new-tracker.com/announce", "http://tracker1.com/announce"]);

        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enriched = await enricher.EnrichMagnetLink(TestMagnetLink);

        // Assert
        Assert.Equal(TestMagnetLink + $"&tr={Uri.EscapeDataString("http://new-tracker.com/announce")}", enriched);
    }

    [Fact]
    public async Task EnrichMagnetLink_Throws_WhenTrackerGrabberThrows()
    {
        // Arrange
        _trackerListGrabberMock
            .Setup(t => t.GetTrackers())
            .ThrowsAsync(new InvalidOperationException("Unable to fetch tracker list for enrichment."));

        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => enricher.EnrichMagnetLink(TestMagnetLink));
    }

    [Fact]
    public async Task EnrichTorrentBytes_AddsTrackers_WhenTrackersFromTrackerGrabber()
    {
        // Arrange
        var originalTracker = "http://tracker1.com/announce";
        var newTracker = "http://new-tracker.com/announce";

        var torrentDict = new BEncodedDictionary
        {
            ["announce"] = new BEncodedString(originalTracker),
            ["announce-list"] = new BEncodedList
            {
                new BEncodedList
                {
                    new BEncodedString(originalTracker)
                }
            }
        };

        var originalTorrentBytes = torrentDict.Encode();

        SetupTrackerListGrabber([newTracker]);
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enrichedBytes = await enricher.EnrichTorrentBytes(originalTorrentBytes);
        var enrichedDict = BEncodedValue.Decode<BEncodedDictionary>(enrichedBytes);

        // Assert
        Assert.True(enrichedDict.ContainsKey("announce"));
        Assert.True(enrichedDict.ContainsKey("announce-list"));

        var announceList = (BEncodedList)enrichedDict["announce-list"];
        var flattened = announceList.Cast<BEncodedList>().SelectMany(l => l.Cast<BEncodedString>().Select(s => s.Text)).ToList();

        Assert.Contains(originalTracker, flattened);
        Assert.Contains(newTracker, flattened);

        var announce = ((BEncodedString)enrichedDict["announce"]).Text;
        Assert.Equal(flattened.First(), announce);
    }

    private void SetupTrackerListGrabber(String[] trackerList)
    {
        _trackerListGrabberMock
            .Setup(t => t.GetTrackers())
            .ReturnsAsync(trackerList)
            .Verifiable();
    }

    [Fact]
    public async Task EnrichTorrentBytes_DoesNotAddDuplicateTrackers()
    {
        // Arrange
        var originalTracker = "http://tracker1.com/announce";
        var duplicateTracker = "http://tracker1.com/announce";

        var torrentDict = new BEncodedDictionary
        {
            ["announce"] = new BEncodedString(originalTracker),
            ["announce-list"] = new BEncodedList
            {
                new BEncodedList
                {
                    new BEncodedString(originalTracker)
                }
            }
        };

        var originalTorrentBytes = torrentDict.Encode();

        SetupTrackerListGrabber([duplicateTracker]);
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enrichedBytes = await enricher.EnrichTorrentBytes(originalTorrentBytes);
        var enrichedDict = BEncodedValue.Decode<BEncodedDictionary>(enrichedBytes);

        // Assert
        var announceList = (BEncodedList)enrichedDict["announce-list"];
        var flattened = announceList.Cast<BEncodedList>().SelectMany(l => l.Cast<BEncodedString>().Select(s => s.Text)).ToList();

        Assert.Single(flattened);
        Assert.Contains(originalTracker, flattened);
    }

    [Fact]
    public async Task EnrichTorrentBytes_AddsTrackers_WhenNoAnnounceListPresent()
    {
        // Arrange
        var originalTracker = "http://tracker1.com/announce";
        var newTracker = "http://new-tracker.com/announce";

        var torrentDict = new BEncodedDictionary
        {
            ["announce"] = new BEncodedString(originalTracker)

            // No "announce-list"
        };

        var originalTorrentBytes = torrentDict.Encode();

        SetupTrackerListGrabber([newTracker]);
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enrichedBytes = await enricher.EnrichTorrentBytes(originalTorrentBytes);
        var enrichedDict = BEncodedValue.Decode<BEncodedDictionary>(enrichedBytes);

        // Assert
        Assert.True(enrichedDict.ContainsKey("announce-list"));
        var announceList = (BEncodedList)enrichedDict["announce-list"];
        var flattened = announceList.Cast<BEncodedList>().SelectMany(l => l.Cast<BEncodedString>().Select(s => s.Text)).ToList();

        Assert.Contains(originalTracker, flattened);
        Assert.Contains(newTracker, flattened);
    }

    [Fact]
    public async Task EnrichTorrentBytes_Throws_WhenTrackerGrabberThrows()
    {
        // Arrange
        _trackerListGrabberMock
            .Setup(t => t.GetTrackers())
            .ThrowsAsync(new InvalidOperationException("Unable to fetch tracker list for enrichment."));

        var torrentDict = new BEncodedDictionary
        {
            ["announce"] = new BEncodedString("http://tracker1.com/announce"),
            ["announce-list"] = new BEncodedList
            {
                new BEncodedList
                {
                    new BEncodedString("http://tracker1.com/announce")
                }
            }
        };

        var originalTorrentBytes = torrentDict.Encode();

        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => enricher.EnrichTorrentBytes(originalTorrentBytes));
    }

    [Fact]
    public async Task EnrichTorrentBytes_ThrowsArgumentException_OnNullOrEmptyInput()
    {
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        await Assert.ThrowsAsync<ArgumentException>(() => enricher.EnrichTorrentBytes(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => enricher.EnrichTorrentBytes(Array.Empty<Byte>()));
    }

    [Fact]
    public async Task EnrichTorrentBytes_ThrowsInvalidOperationException_OnNonTorrentBytes()
    {
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        var notTorrent = new Byte[]
        {
            1, 2, 3, 4, 5
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => enricher.EnrichTorrentBytes(notTorrent));
    }

    [Fact]
    public async Task EnrichMagnetLink_WithExistingTrackersAndOtherParameters()
    {
        // Arrange
        var magnetLink = "magnet:?xt=urn:btih:HASH&dn=MyFile&tr=udp%3A%2F%2Fexisting";

        var newTrackers = new[]
        {
            "udp://new1", "udp://existing"
        }; // "udp://existing" is a duplicate

        SetupTrackerListGrabber(newTrackers);
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var result = await enricher.EnrichMagnetLink(magnetLink);

        // Assert
        var queryParams = System.Web.HttpUtility.ParseQueryString(new Uri(result).Query);
        var trValues = queryParams.GetValues("tr")?.ToList() ?? new List<String>();

        Assert.Equal("urn:btih:HASH", queryParams["xt"]);
        Assert.Equal("MyFile", queryParams["dn"]);
        Assert.Contains("udp://existing", trValues);
        Assert.Contains("udp://new1", trValues);
        Assert.Equal(2, trValues.Count); // Should be "udp://existing" and "udp://new1"
    }

    [Fact]
    public async Task EnrichMagnetLink_BasicMagnetWithHashOnly()
    {
        // Arrange
        var magnetLink = "magnet:?xt=urn:btih:HASH";

        var newTrackers = new[]
        {
            "udp://tracker1"
        };

        SetupTrackerListGrabber(newTrackers);
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var result = await enricher.EnrichMagnetLink(magnetLink);

        // Assert
        Assert.Equal($"{magnetLink}&tr={Uri.EscapeDataString("udp://tracker1")}", result);
    }

    [Fact]
    public async Task EnrichMagnetLink_MagnetEndsWithQuestionMarkOnly()
    {
        // Arrange
        var magnetLink = "magnet:?";

        var newTrackers = new[]
        {
            "udp://tracker1"
        };

        SetupTrackerListGrabber(newTrackers);
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var result = await enricher.EnrichMagnetLink(magnetLink);

        // Assert
        Assert.Equal($"{magnetLink}tr={Uri.EscapeDataString("udp://tracker1")}", result);
    }

    [Fact]
    public async Task EnrichMagnetLink_GetTrackersReturnsEmptyArray_Unchanged()
    {
        // Arrange
        var magnetLink = "magnet:?xt=urn:btih:HASH&tr=udp%3A%2F%2Fexisting";
        SetupTrackerListGrabber(Array.Empty<String>()); // No new trackers
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var result = await enricher.EnrichMagnetLink(magnetLink);

        // Assert
        Assert.Equal(magnetLink, result);
    }

    // New tests for EnrichTorrentBytes
    [Fact]
    public async Task EnrichTorrentBytes_NoExistingAnnounceOrAnnounceList()
    {
        // Arrange
        var torrentDict = new BEncodedDictionary
        {
            ["info"] = new BEncodedDictionary()
        }; // Minimal valid torrent

        var originalBytes = torrentDict.Encode();

        var newTrackers = new[]
        {
            "udp://tracker1", "udp://tracker2"
        };

        SetupTrackerListGrabber(newTrackers);
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enrichedBytes = await enricher.EnrichTorrentBytes(originalBytes);
        var enrichedDict = BEncodedValue.Decode<BEncodedDictionary>(enrichedBytes);

        // Assert
        Assert.Equal(newTrackers[0], ((BEncodedString)enrichedDict["announce"]).Text);
        var announceList = (BEncodedList)enrichedDict["announce-list"];
        Assert.Equal(2, announceList.Count);
        Assert.Equal(newTrackers[0], ((BEncodedString)((BEncodedList)announceList[0])[0]).Text);
        Assert.Equal(newTrackers[1], ((BEncodedString)((BEncodedList)announceList[1])[0]).Text);
    }

    [Fact]
    public async Task EnrichTorrentBytes_WithExistingAnnounceAndAnnounceList()
    {
        // Arrange
        var existingTrackers = new List<String>
        {
            "http://tier1.com",
            "http://existing.com/announce"
        };

        var torrentDict = new BEncodedDictionary
        {
            ["announce"] = new BEncodedString(existingTrackers[1]),
            ["announce-list"] = new BEncodedList
            {
                new BEncodedList
                {
                    new BEncodedString(existingTrackers[0])
                },
                new BEncodedList
                {
                    new BEncodedString(existingTrackers[1])
                }
            },
            ["info"] = new BEncodedDictionary()
        };

        var originalBytes = torrentDict.Encode();

        var newTrackersFromGrabber = new[]
        {
            "udp://new1", "http://existing.com/announce"
        }; // "http://existing.com/announce" is duplicate

        SetupTrackerListGrabber(newTrackersFromGrabber);

        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enrichedBytes = await enricher.EnrichTorrentBytes(originalBytes);
        var enrichedDict = BEncodedValue.Decode<BEncodedDictionary>(enrichedBytes);

        // Assert
        var finalAnnounceList = ((BEncodedList)enrichedDict["announce-list"])
                                .Cast<BEncodedList>()
                                .SelectMany(tier => tier.Cast<BEncodedString>().Select(t => t.Text))
                                .ToList();

        Assert.Contains("http://tier1.com", finalAnnounceList);
        Assert.Contains("http://existing.com/announce", finalAnnounceList);
        Assert.Contains("udp://new1", finalAnnounceList);
        Assert.Equal(3, finalAnnounceList.Count); // existingTrackers[0], existingTrackers[1], newTrackersFromGrabber[0]
        Assert.Contains(((BEncodedString)enrichedDict["announce"]).Text, finalAnnounceList); // announce should be one of the final list

        // Verify logged count for added trackers (NewTrackersCount = 1, for "udp://new1")
        // This requires logger verification setup. For now, we trust the logic if counts are right.
        // Example: _loggerMock.Verify(log => log.LogInformation(It.Is<String>(s => s.Contains("Added 1 new trackers"))));
        // Actual verification of LogInformation needs specific setup for structured logging.
    }

    [Fact]
    public async Task EnrichTorrentBytes_AllTrackersListEmpty_RemovesAnnounce()
    {
        // Arrange
        var torrentDict = new BEncodedDictionary
        {
            ["info"] = new BEncodedDictionary()
        }; // No initial trackers

        var originalBytes = torrentDict.Encode();
        SetupTrackerListGrabber(Array.Empty<String>()); // No new trackers
        var enricher = new Enricher(_loggerMock.Object, _trackerListGrabberMock.Object);

        // Act
        var enrichedBytes = await enricher.EnrichTorrentBytes(originalBytes);
        var enrichedDict = BEncodedValue.Decode<BEncodedDictionary>(enrichedBytes);

        // Assert
        Assert.False(enrichedDict.ContainsKey("announce"));

        // announce-list might be present but empty, or also removed, depending on MonoTorrent's behavior.
        // The key is that 'announce' is gone.
        // Current code sets announce-list to an empty BEncodedList.
        Assert.True(enrichedDict.ContainsKey("announce-list"));
        Assert.Empty((BEncodedList)enrichedDict["announce-list"]);
    }
}