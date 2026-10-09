using DefikarteBackend.Interfaces;
using Microsoft.Extensions.Logging;
using NetTopologySuite;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Newtonsoft.Json;

namespace DefikarteBackend.Services
{
    public class GeofenceService : IGeofenceService
    {
        private readonly GeometryFactory _geometryFactory;
        private readonly NtsGeometryServices _ntsGeometryServices;
        private readonly IBlobStorageDataRepository _blobRepository;
        private readonly IServiceConfiguration _configuration;
        private readonly ILogger _logger;

        public GeofenceService(
            NtsGeometryServices ntsGeometryServices,
            IBlobStorageDataRepository blobRepository,
            IServiceConfiguration configuration,
            ILogger<GeofenceService> logger)
        {
            _ntsGeometryServices = ntsGeometryServices;
            _blobRepository = blobRepository;
            _configuration = configuration;
            _logger = logger;
            _geometryFactory = _ntsGeometryServices.CreateGeometryFactory(4326);
        }

        public async Task<bool> IsSwitzerlandAsync(double latitude, double longitude)
        {
            try
            {
                var geoJson = await _blobRepository.ReadAsync(_configuration.BlobStorageSwissBoundariesName);
                if (string.IsNullOrEmpty(geoJson))
                {
                    return false;
                }

                FeatureCollection? geometry;
                var serializer = GeoJsonSerializer.Create();
                using (var stringReader = new StringReader(geoJson))
                using (var jsonReader = new JsonTextReader(stringReader))
                {
                    geometry = serializer.Deserialize<FeatureCollection>(jsonReader);
                }

                var point = _geometryFactory.CreatePoint(new Coordinate(longitude, latitude));

                foreach (var geom in geometry ?? new FeatureCollection())
                {
                    if (geom.Geometry?.Contains(point) == true)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking if point is in Switzerland.");
            }

            return false;
        }
    }
}
