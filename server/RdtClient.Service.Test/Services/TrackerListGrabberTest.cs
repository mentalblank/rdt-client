using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using RdtClient.Service.Services;

namespace RdtClient.Service.Test.Services
{
    public class TrackerListGrabberTest
    {
        // Helper subclass to inject test settings
        private class TestableTrackerListGrabber : TrackerListGrabber
        {
            private readonly Func<String> _getUrl;
            private readonly Func<Int32> _getExpiration;

            public TestableTrackerListGrabber(IHttpClientFactory httpClientFactory,
                                              IMemoryCache memoryCache,
                                              ILogger<TrackerListGrabber> logger,
                                              Func<String> getUrl,
                                              Func<Int32> getExpiration) : base(httpClientFactory, memoryCache, logger)
            {
                _getUrl = getUrl;
                _getExpiration = getExpiration;
            }

            protected String GetTrackerEnrichmentList()
            {
                return _getUrl();
            }

            protected Int32 GetTrackerEnrichmentCacheExpiration()
            {
                return _getExpiration();
            }
        }

        private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
        private readonly Mock<IMemoryCache> _mockMemoryCache;
        private readonly Mock<ILogger<TrackerListGrabber>> _mockLogger;
        private readonly TrackerListGrabber _trackerListGrabber;

        // For a real scenario, an ISettingsProvider interface injected into TrackerListGrabber would be better.
        private String _trackerUrl = "";
        private Int32 _cacheExpiration = 60;

        public TrackerListGrabberTest()
        {
            var mockHttpClientFactory = new Mock<IHttpClientFactory>();
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
            _mockMemoryCache = new Mock<IMemoryCache>();
            _mockLogger = new Mock<ILogger<TrackerListGrabber>>();

            // Setup HttpClientFactory to return an HttpClient using the mocked HttpMessageHandler
            var httpClient = new HttpClient(_mockHttpMessageHandler.Object);
            mockHttpClientFactory.Setup(_ => _.CreateClient(It.IsAny<String>())).Returns(httpClient);

            // Patch TrackerListGrabber to use our test settings
            _trackerListGrabber =
                new TestableTrackerListGrabber(mockHttpClientFactory.Object, _mockMemoryCache.Object, _mockLogger.Object, () => _trackerUrl, () => _cacheExpiration);
        }

        private void SetupTrackerUrl(String url)
        {
            _trackerUrl = url;
        }

        private void SetupCacheExpiration(Int32 minutes)
        {
            _cacheExpiration = minutes;
        }

        [Fact]
        public async Task GetTrackers_TrackerUrlListNullOrWhitespace_ReturnsEmptyArray()
        {
            // Arrange
            SetupTrackerUrl(String.Empty);

            // Act
            var result = await _trackerListGrabber.GetTrackers();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);

            _mockHttpMessageHandler.Protected()
                                   .Verify("SendAsync",
                                           Times.Never(),
                                           ItExpr.IsAny<HttpRequestMessage>(),
                                           ItExpr.IsAny<CancellationToken>());
        }
    }
}