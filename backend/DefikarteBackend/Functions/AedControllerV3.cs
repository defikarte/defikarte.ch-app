using DefikarteBackend.Helpers;
using DefikarteBackend.Interfaces;
using DefikarteBackend.Model;
using DefikarteBackend.OsmOverpassApi;
using DefikarteBackend.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using OsmSharp;
using OsmSharp.IO.API;
using OsmSharp.Tags;
using System.Net;

namespace DefikarteBackend.Functions
{
    public class AedControllerV3
    {
        private static readonly HttpClient _httpClient = new();

        private readonly IServiceConfiguration _config;
        private readonly IUpdateGeoJsonCacheService _updateGeoJsonCacheService;
        private readonly IGeoJsonCacheRepository _cacheRepository;
        private readonly IGeofenceService _localisationService;
        private readonly OverpassClient _overpassClient;
        private readonly ILogger<AedControllerV3> _logger;

        public AedControllerV3(
            IServiceConfiguration config,
            IEnumerable<IGeoJsonCacheRepository> cacheRepositories,
            IUpdateGeoJsonCacheService updateGeoJsonCacheService,
            IGeofenceService localisationService,
            OverpassClient overpassClient,
            ILogger<AedControllerV3> logger)
        {
            _config = config;
            _logger = logger;
            _updateGeoJsonCacheService = updateGeoJsonCacheService;
            _localisationService = localisationService;
            _overpassClient = overpassClient;
            _cacheRepository = cacheRepositories.First(x => x.DataSourceType == DataSourceType.Osm);
        }

        [Function("AED_GET_BY_ID_V3")]
        [OpenApiOperation(operationId: "AED_GET_BY_ID_V3", tags: ["AED_V3"], Summary = "Get AED by ID as geojson.")]
        [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Summary = "ID of AED to return as geojson")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(FeatureCollection), Description = "The OK response")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.NotFound, contentType: "application/json", bodyType: typeof(object), Description = "The NotFound response.")]
        [OpenApiSecurity("api-key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-functions-key")]
        public async Task<IActionResult> GetById(
         [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v3/aed/{id}")] HttpRequest req,
         string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    return new ObjectResult(new { Message = $"ID parameter missing. Cannot return any AED." }) { StatusCode = StatusCodes.Status400BadRequest };
                }

                var byIdResponse = await _cacheRepository.GetByIdAsync(id);
                if (byIdResponse != null)
                {
                    var result = new FeatureCollection();
                    result.Features.Add(byIdResponse);
                    return new OkObjectResult(result);
                }

                var geojsonResponse = await GetAedFromOverpassAsync();
                var overPassByIdResponse = geojsonResponse.Features.Find(x => x.Id == id);
                if (overPassByIdResponse != null)
                {
                    var overpassResult = new FeatureCollection();
                    overpassResult.Features.Add(overPassByIdResponse);
                    return new OkObjectResult(overpassResult);
                }

                return new ObjectResult(new { Message = $"AED with Id: {id} not found." }) { StatusCode = StatusCodes.Status404NotFound };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new ObjectResult(new { ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                };
            }
        }

        [Function("AED_GET_V3")]
        [OpenApiOperation(operationId: "AED_GET_V3", tags: ["AED_V3"], Summary = "Get all AEDs in switzerland as geojson.")]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(FeatureCollection), Description = "The OK response")]
        [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NotModified, Description = "Content not modified")]
        [OpenApiSecurity("api-key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-functions-key")]
        public async Task<IActionResult> GetAll(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v3/aed")] HttpRequest req,
            string? id)
        {
            try
            {
                var response = await _cacheRepository.GetRawAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(response))
                {
                    var etag = ETagHashCalculator.Calculate(response);
                    _logger.LogInformation($"Get all AED from server-cache. Etag:{etag}");
                    if (req.Headers.TryGetValue("If-None-Match", out var incomingETag) && incomingETag == etag)
                    {
                        return new StatusCodeResult(StatusCodes.Status304NotModified);
                    }

                    req.HttpContext.Response.Headers.ETag = etag;
                    return new GeoJsonContentResult(response);
                }

                _logger.LogWarning($"Get all AED from overpass. Server-cache not available.");
                var geojsonResponse = await GetAedFromOverpassAsync();
                return new OkObjectResult(geojsonResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new ObjectResult(new { ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                };
            }
        }

        [Function("AED_POST_V3")]
        [OpenApiOperation(operationId: "AED_POST_V3", tags: ["AED_V3"], Summary = "Create a new AED from geojson in OSM.")]
        [OpenApiRequestBody("application/json", typeof(FeatureCollection))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(FeatureCollection), Description = "The OK response")]
        [OpenApiSecurity("api-key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-functions-key")]
        public async Task<IActionResult> Create(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "v3/aed")] HttpRequest req)
        {
            try
            {
                var username = _config.OsmUserName;
                var osmApiToken = _config.OsmApiToken;
                var osmApiUrl = _config.OsmApiUrl;

                if (string.IsNullOrEmpty(osmApiToken) || string.IsNullOrEmpty(osmApiUrl))
                {
                    _logger.LogWarning("No valid configuration available for either username, token or osmApiUrl");
                    return new ObjectResult(new { Message = "Configuration error. Contact API-Admins." })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError,
                    };
                }

                var validationResult = await req.GetValidatedRequestAsync<FeatureCollection, FeatureCollectionValidator>();
                if (validationResult == null || validationResult.IsValid == false)
                {
                    _logger.LogInformation($"Invalid request data: {JsonConvert.SerializeObject(validationResult?.Errors)}");
                    return validationResult?.ToBadRequest() ?? new BadRequestObjectResult(new { Message = "Request cannot be parsed. Body is not a valid JSON or null." });
                }

                // currently only 1 feature change is allowed, but still we will iterate over the all features to make it easier for further implementations
                var clientFactory = new ClientsFactory(_logger, _httpClient, osmApiUrl);
                var osmClient = clientFactory.CreateOAuth2Client(osmApiToken);
                var changeSetTags = new TagsCollection() { new Tag("created_by", username), new Tag("comment", "Create new AED.") };
                var changeSetId = await osmClient.CreateChangeset(changeSetTags);

                var resultIds = new List<Node>();

                var body = validationResult.Value;
                foreach (var feature in body.Features)
                {
                    var coordinates = feature.Geometry.Coordinates;
                    var isInSwitzerland = await _localisationService.IsSwitzerlandAsync(coordinates[1], coordinates[0]).ConfigureAwait(false);
                    var newNode = CreateNode(feature, isInSwitzerland, null);

                    newNode.ChangeSetId = changeSetId;
                    var nodeId = await osmClient.CreateElement(changeSetId, newNode);

                    var createdNode = await osmClient.GetNode(nodeId);
                    resultIds.Add(createdNode);
                    _logger.LogInformation($"Added new node {nodeId}, isInSwitzerland:{isInSwitzerland}");
                }

                await osmClient.CloseChangeset(changeSetId);
                var geoJsonResponse = GeoJsonConverter.Convert2GeoJson(resultIds);
                await _updateGeoJsonCacheService.AddOrUpdateFeaturesInLocalCacheAsync(geoJsonResponse).ConfigureAwait(false);

                return new OkObjectResult(geoJsonResponse) { StatusCode = StatusCodes.Status201Created };
            }
            catch (JsonSerializationException ex)
            {
                _logger.LogError(ex.ToString());
                return new BadRequestObjectResult(new { ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new ObjectResult(new { ex.Message }) { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }

        [Function("AED_PUT_V3")]
        [OpenApiOperation(operationId: "AED_PUT_V3", tags: ["AED_V3"], Summary = "Updates (replaces) AED with given ID and props in OSM.")]
        [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(long), Summary = "Id of the AED to replace.")]
        [OpenApiRequestBody("application/json", typeof(FeatureCollection))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(FeatureCollection), Description = "The OK response")]
        [OpenApiSecurity("api-key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-functions-key")]
        public async Task<IActionResult> Update(
          [HttpTrigger(AuthorizationLevel.Function, "put", Route = "v3/aed/{id}")] HttpRequest req, long id)
        {
            try
            {
                var username = _config.OsmUserName;
                var osmApiToken = _config.OsmApiToken;
                var osmApiUrl = _config.OsmApiUrl;

                if (string.IsNullOrEmpty(osmApiToken) || string.IsNullOrEmpty(osmApiUrl))
                {
                    _logger.LogWarning("No valid configuration available for either username, token or osmApiUrl");
                    return new ObjectResult(new { Message = "Configuration error. Contact API-Admins." })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError,
                    };
                }

                var validationResult = await req.GetValidatedRequestAsync<FeatureCollection, FeatureCollectionValidator>();
                if (validationResult == null || validationResult.IsValid == false)
                {
                    _logger.LogInformation($"Invalid request data: {JsonConvert.SerializeObject(validationResult?.Errors)}");
                    return validationResult?.ToBadRequest() ?? new BadRequestObjectResult(new { Message = "Request cannot be parsed. Body is not a valid JSON or null." });
                }

                var feature = validationResult.Value.Features.First();

                if (!long.TryParse(feature.Id, out var featureId) || featureId != id)
                {
                    return new BadRequestObjectResult(new { Message = "Request ID does not match feature ID. IDs must be equal." });
                }

                var coordinates = feature.Geometry.Coordinates;
                var isInSwitzerland = await _localisationService.IsSwitzerlandAsync(coordinates[1], coordinates[0]).ConfigureAwait(false);

                var clientFactory = new ClientsFactory(_logger, _httpClient, osmApiUrl);
                var osmClient = clientFactory.CreateOAuth2Client(osmApiToken);
                var currentNode = await osmClient.GetNode(id);
                var node = CreateNode(feature, isInSwitzerland, currentNode.Tags);
                node.Id = id;
                node.Version = currentNode.Version;

                var changeSetTags = new TagsCollection() { new Tag("created_by", username), new Tag("comment", "Update AED.") };
                var changeSetId = await osmClient.CreateChangeset(changeSetTags);

                node.ChangeSetId = changeSetId;
                await osmClient.UpdateElement(changeSetId, (OsmGeo)node);
                await osmClient.CloseChangeset(changeSetId);

                var updatedNode = await osmClient.GetNode(id);
                _logger.LogInformation($"Updated node {id}, isInSwitzerland:{isInSwitzerland}");
                var geoJsonResponse = GeoJsonConverter.Convert2GeoJson(new List<Node> { updatedNode });
                await _updateGeoJsonCacheService.AddOrUpdateFeaturesInLocalCacheAsync(geoJsonResponse).ConfigureAwait(false);

                return new OkObjectResult(geoJsonResponse);
            }
            catch (JsonSerializationException ex)
            {
                _logger.LogError(ex.ToString());
                return new BadRequestObjectResult(new { ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new ObjectResult(new { ex.Message }) { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }

        private static Node CreateNode(Feature feature, bool isInSwitzerland, TagsCollectionBase? currentNodeTags)
        {
            var props = GeoJsonConverter.Convert2AedPropertyData(feature.Properties);
            var emergencyPhone = isInSwitzerland
                ? "144"
                : string.Empty;

            var tags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "emergency", "defibrillator"
                },
                {
                    "emergency:phone",  emergencyPhone
                },
                {
                    "defibrillator:location", props.Location
                },
                {
                    "opening_hours", props.OpeningHours
                },
                {
                    "phone", props.OperatorPhone
                },
                {
                    "email", props.OperatorEmail
                },
                {
                    "operator", props.Operator
                },
                {
                    // Ensure no values are not set anymore, i.e. by older versions of the app
                    "access", props.Access == "no" ? null : props.Access
                },
                {
                    "indoor", props.Indoor
                },
                {
                    "description", props.Description
                },
                {
                    "level", props.Level
                },
                {
                    "source", props.Source
                },
            };

            if (currentNodeTags != null)
            {
                // Preserve existing tags from the current OSM node that are not already set
                foreach (var tag in currentNodeTags)
                {
                    if (!tags.ContainsKey(tag.Key))
                    {
                        tags[tag.Key] = tag.Value;
                    }
                }
            }

            var cleanTags = tags
                .Where(x => !string.IsNullOrEmpty(x.Value?.Trim()))
                .Select(x => new KeyValuePair<string, string?>(x.Key, StringHelper.RemoveDuplicatedWhitespace(x.Value)))
                .ToDictionary(x => x.Key, x => x.Value);

            return new Node()
            {
                Latitude = feature.Geometry.Coordinates[1],
                Longitude = feature.Geometry.Coordinates[0],
                Tags = new TagsCollection(cleanTags),
            };
        }

        private async Task<FeatureCollection> GetAedFromOverpassAsync()
        {
            var overpassResponse = await _overpassClient.GetAllDefibrillatorsInSwitzerland();
            var geojsonResponse = GeoJsonConverter.Convert2GeoJson(overpassResponse);
            return geojsonResponse;
        }
    }
}