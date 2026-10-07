using Azure.Storage.Blobs;
using DefikarteBackend.Cache;
using DefikarteBackend.Configuration;
using DefikarteBackend.Interfaces;
using DefikarteBackend.Model;
using DefikarteBackend.OsmOverpassApi;
using DefikarteBackend.Repository;
using DefikarteBackend.Services;
using DefikarteBackend.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Implementation;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

internal class Program
{
    private static void Main(string[] args)
    {
        var configuration = LoadConfiguration();
        var serviceConfig = ServiceConfiguration.Initialize(configuration);

        var container = new BlobContainerClient(serviceConfig.BlobStorageConnectionString, serviceConfig.BlobStorageContainerName);
        container.CreateIfNotExists();

        var host = new HostBuilder()
            .ConfigureFunctionsWebApplication(b =>
            {
                b.UseNewtonsoftJson(new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver
                    {
                        NamingStrategy = new CamelCaseNamingStrategy(),
                    }
                });
            })
            .ConfigureServices(services =>
            {
                services.AddApplicationInsightsTelemetryWorkerService();
                services.ConfigureFunctionsApplicationInsights();
                services.AddMvc().AddNewtonsoftJson();
                services.AddResponseCompression(o =>
                {
                    o.EnableForHttps = true;
                    o.Providers.Add<BrotliCompressionProvider>();
                    o.Providers.Add<GzipCompressionProvider>();
                });

                services.AddLogging(options =>
                {
                    options.AddFilter(nameof(DefikarteBackend), LogLevel.Trace);
                });

                services.AddSingleton<IServiceConfiguration>((s) => serviceConfig);
                services.AddTransient<ICacheRepository<OsmNode>>(s =>
                    new BlobStorageCacheRepository(container, serviceConfig.BlobStorageBlobName));
                services.AddTransient<IGeoJsonCacheRepository>(s =>
                    new BlobStorageCacheRepositoryV2(container, serviceConfig.BlobStorageBlobNameV2, DataSourceType.Osm));
                services.AddTransient<IGeoJsonCacheRepository>(s =>
                   new BlobStorageCacheRepositoryV2(container, serviceConfig.BlobStorageBlobNameLocalV2, DataSourceType.Local));

                services.AddTransient<IUpdateGeoJsonCacheService, UpdateGeoJsonCacheService>();

                services.AddSingleton(container);
                services.AddSingleton<IBlobStorageDataRepository, BlobStorageDataRepository>();

                services.AddSingleton<IOpenApiConfigurationOptions>(s => new CustomOpenApiConfigurationOptions());

                services.AddTransient<IValidator<DefibrillatorRequest>, DefibrillatorRequestValidator>();
                services.AddTransient<IValidator<DefibrillatorRequestV2>, DefibrillatorRequestValidatorV2>();
                services.AddTransient<IValidator<FeatureCollection>, FeatureCollectionValidator>();

                services.AddTransient<IAddressSearchService, SwisstopoAddressSearchService>();

                services.AddSingleton<IGeofenceService, GeofenceService>();
                services.AddSingleton<OverpassClient>();

                RegisterNtsGeometryServices(services);
            })
            .Build();

        host.Run();
    }

    private static IConfigurationRoot LoadConfiguration()
    {
        return new ConfigurationBuilder()
               .AddEnvironmentVariables() // access (production) environment variables
               .Build();
    }

    private static void RegisterNtsGeometryServices(IServiceCollection services)
    {
        NtsGeometryServices.Instance = new NtsGeometryServices(
            CoordinateArraySequenceFactory.Instance,
            new PrecisionModel(1000d),
            4326,
            GeometryOverlay.NG,
            new CoordinateEqualityComparer());

        services.AddSingleton(NtsGeometryServices.Instance);
    }
}
