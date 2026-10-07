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
    public class AedControllerV1
    {
        private static readonly HttpClient _httpClient = new();

        private readonly IServiceConfiguration _config;
        private readonly ICacheRepository<OsmNode> _cacheRepository;
        private readonly IGeofenceService _localisationService;
        private readonly OverpassClient _overpassClient;
        private readonly ILogger<AedControllerV1> _logger;

        public AedControllerV1(
            IServiceConfiguration config,
            ICacheRepository<OsmNode> cacheRepository,
            IGeofenceService localisationService,
            OverpassClient overpassClient,
            ILogger<AedControllerV1> logger)
        {
            _config = config;
            _cacheRepository = cacheRepository;
            _localisationService = localisationService;
            _overpassClient = overpassClient;
            _logger = logger;
        }

        [Function("Defibrillators_GETALL")]
        [OpenApiOperation(operationId: "GetDefibrillators_V1", tags: ["Defibrillator-V1"], Summary = "Get all defibrillators from switzerland as custom json.", Deprecated = true)]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(List<OsmNode>), Description = "The OK response")]
        public async Task<IActionResult> GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "defibrillator")] HttpRequest req)
        {
            try
            {
                if (TryParseIdQuery(req.Query, out var id))
                {
                    var byIdResponse = await _cacheRepository.GetByIdAsync(id);
                    return new OkObjectResult(byIdResponse);
                }

                var response = await _cacheRepository.GetAsync();
                if (response != null && response.Count > 0)
                {
                    _logger.LogInformation($"Get all AED from cache. Count: {response.Count}");
                    return new OkObjectResult(response);
                }

                var overpassApiUrl = _config.OverpassApiUrl;
                _logger.LogInformation($"Get all AED from {overpassApiUrl}. Cache is not available.");

                var overpassResponse = await _overpassClient.GetAllDefibrillatorsInSwitzerland();
                return new OkObjectResult(overpassResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new ObjectResult(new { Message = ex.Message })
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                };
            }
        }


        [Function("Defibrillators_POST")]
        [OpenApiOperation(operationId: "CreateDefibrillator_V1", tags: ["Defibrillator-V1"], Summary = "Create a new defibrillator.", Deprecated = true)]
        [OpenApiRequestBody("application/json", typeof(DefibrillatorRequest))]
        [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(DefibrillatorResponse), Description = "The OK response")]
        [OpenApiSecurity("api-key", SecuritySchemeType.ApiKey, In = OpenApiSecurityLocationType.Header, Name = "x-functions-key")]
        public async Task<IActionResult> Create(
            [HttpTrigger(AuthorizationLevel.Function, "Post", Route = "defibrillator")] HttpRequest req)
        {
            try
            {
                var username = _config.OsmUserName;
                var osmApiToken = _config.OsmApiToken;
                var osmApiUrl = _config.OsmApiUrl;

                if (string.IsNullOrEmpty(osmApiToken) || string.IsNullOrEmpty(osmApiUrl))
                {
                    _logger.LogWarning("No valid configuration available for eighter osmApitoken or osmApiUrl");
                    return new ObjectResult(new { Message = "Configuration error. Contact API-Admins." })
                    {
                        StatusCode = StatusCodes.Status500InternalServerError,
                    };
                }

                var validationResult = await req.GetValidatedRequestAsync<DefibrillatorRequest, DefibrillatorRequestValidator>();
                if (validationResult == null || validationResult.IsValid == false)
                {
                    _logger.LogInformation($"Invalid request data.");
                    return validationResult?.ToBadRequest() ?? new BadRequestObjectResult(new { Message = "Request cannot be parsed. Body is not a valid JSON or null." });
                }

                var body = validationResult.Value;
                var isInSwitzerland = await _localisationService.IsSwitzerlandAsync(body.Latitude, body.Longitude).ConfigureAwait(false);
                var newNode = CreateNode(body, isInSwitzerland);
                var clientFactory = new ClientsFactory(_logger, _httpClient, osmApiUrl);

                var authClient = clientFactory.CreateOAuth2Client(osmApiToken);
                var changeSetTags = new TagsCollection() { new Tag("created_by", username), new Tag("comment", "Create new AED.") };
                var changeSetId = await authClient.CreateChangeset(changeSetTags);

                newNode.ChangeSetId = changeSetId;
                var nodeId = await authClient.CreateElement(changeSetId, newNode);

                await authClient.CloseChangeset(changeSetId);

                var createdNode = await authClient.GetNode(nodeId);

                _logger.LogInformation($"Added new node {nodeId}");
                return new ObjectResult(createdNode) { StatusCode = StatusCodes.Status201Created };

            }
            catch (JsonSerializationException ex)
            {
                _logger.LogError(ex.ToString());
                return new BadRequestObjectResult(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new ObjectResult(new { Message = ex.Message }) { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }

        private static bool TryParseIdQuery(IQueryCollection query, out string id)
        {
            id = string.Empty;
            try
            {
                var idValues = query["id"].FirstOrDefault();
                bool available = !string.IsNullOrEmpty(idValues);
                id = idValues ?? string.Empty;
                return available;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Node CreateNode(DefibrillatorRequest request, bool isInSwitzerland)
        {
            var emergencyPhone = isInSwitzerland
                ? "144"
                : string.Empty;

            var tags = new Dictionary<string, string?>
            {
                {
                    "emergency", "defibrillator"
                },
                {
                    "emergency:phone", emergencyPhone
                },
                {
                    "defibrillator:location", request.Location
                },
                {
                    "opening_hours", request.OpeningHours
                },
                {
                    "phone", request.OperatorPhone
                },
                {
                    "operator", request.Operator
                },
                {
                    "access", request.Access ? "yes" : null
                },
                {
                    "indoor", request.Indoor ? "yes" : "no"
                },
                {
                    "description", request.Description
                },
                {
                    "level", request.Level
                },
                {
                    "source", request.Source
                },
            };

            var cleanTags = tags
                .Where(x => !string.IsNullOrEmpty(x.Value?.Trim()))
                .Select(x => new KeyValuePair<string, string?>(x.Key, StringHelper.RemoveDuplicatedWhitespace(x.Value)))
                .ToDictionary(x => x.Key, x => x.Value);

            return new Node()
            {
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Tags = new TagsCollection(cleanTags),
            };
        }
    }
}
