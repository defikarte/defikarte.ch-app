using DefikarteBackend.Interfaces;
using DefikarteBackend.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace DefikarteBackend.OsmOverpassApi
{
    public class OverpassClient
    {
        private readonly HttpClient _overpassHttpClient;
        private readonly ILogger<OverpassClient> _logger;

        public OverpassClient(IServiceConfiguration config, ILogger<OverpassClient> logger)
        {
            var overpassUrl = config.OverpassApiUrl;
            _overpassHttpClient = new HttpClient
            {
                BaseAddress = new Uri(overpassUrl, UriKind.Absolute),
            };
            _logger = logger;
        }

        public async Task<IList<OsmNode>> GetAllDefibrillatorsInSwitzerland()
        {
            using var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                Content = new StringContent(
                    "[out:json][timeout:25]; " +
                    "(area[\"ISO3166-1\" = \"CH\"][admin_level = 2]; area[\"ISO3166-1\" = \"LI\"][admin_level = 2];)->.searchArea;" +
                    "(node[\"emergency\" = \"defibrillator\"](area.searchArea);" +
                    "way[\"emergency\" = \"defibrillator\"](area.searchArea);" +
                    "relation[\"emergency\" = \"defibrillator\"](area.searchArea);" +
                    ");" +
                    "out body;>;out skel qt;"
                    ),
            };

            try
            {
                using var response = await _overpassHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead);
                response.EnsureSuccessStatusCode();
                var responseContent = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseContent);
                json.TryGetValue("elements", out var osmNodes);
                var jArray = osmNodes as JArray;
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
                List<OsmNode> result = jArray != null
                    ? jArray.Select(x => x.ToObject<OsmNode>()).Where(node => node != null).ToList()
                    : new List<OsmNode>();
#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overpass request to {BaseAddress} failed", _overpassHttpClient.BaseAddress);
                throw;
            }
        }
    }
}
