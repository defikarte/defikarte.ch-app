using DefikarteBackend.Helpers;
using DefikarteBackend.Interfaces;
using DefikarteBackend.Model;
using DefikarteBackend.OsmOverpassApi;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DefikarteBackend.Functions
{
    public class SimpleCacheTimer
    {
        private readonly ILogger<SimpleCacheTimer> _logger;
        private readonly ICacheRepository<OsmNode> _cacheRepository;
        private readonly OverpassClient _overpassClient;
        private readonly IUpdateGeoJsonCacheService _updateGeoJsonCacheService;

        public SimpleCacheTimer(
            ILogger<SimpleCacheTimer> logger,
            ICacheRepository<OsmNode> cacheRepository,
            OverpassClient overpassClient,
            IUpdateGeoJsonCacheService updateGeoJsonCacheService)
        {
            _logger = logger;
            _cacheRepository = cacheRepository;
            _overpassClient = overpassClient;
            _updateGeoJsonCacheService = updateGeoJsonCacheService;
        }

        [Function(nameof(SimpleCacheTimer))]
        public async Task RunAsync([TimerTrigger("0 */15 * * * *", RunOnStartup = true)] TimerInfo myTimer)
        {
            try
            {
                await _updateGeoJsonCacheService.CleanupOldItemsInLocalCacheAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception occurred while tried to CleanupOldItemsInLocalCacheAsync. Continue update cache in {nameof(SimpleCacheTimer)}.");
            }

            try
            {
                var response = await _overpassClient.GetAllDefibrillatorsInSwitzerland();
                var cacheV1Task = _cacheRepository.TryUpdateCacheAsync(response);
                var cacheV2Task = _updateGeoJsonCacheService.TryUpdateAndCombineCacheAsync(GeoJsonConverter.Convert2GeoJson(response));

                var results = await Task.WhenAll(cacheV1Task, cacheV2Task);
                _logger.LogInformation($"Updated cache successful:{results.All(x => x)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error running {nameof(SimpleCacheTimer)}");
            }
        }


    }
}