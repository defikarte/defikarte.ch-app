using Azure.Storage.Blobs;
using DefikarteBackend.Interfaces;
using DefikarteBackend.Model;
using DefikarteBackend.Repository;
using Newtonsoft.Json;

namespace DefikarteBackend.Cache
{
    public class BlobStorageCacheRepository : BlobStorageDataRepository, ICacheRepository<OsmNode>
    {
        private readonly BlobContainerClient _containerClient;
        private readonly string _blobName;

        public BlobStorageCacheRepository(BlobContainerClient containerClient, string blobName)
            : base(containerClient)
        {
            _containerClient = containerClient;
            _blobName = blobName;
        }

        public async Task<IList<OsmNode>> GetAsync()
        {
            var content = await ReadAsync(_blobName);
            return JsonConvert.DeserializeObject<List<OsmNode>>(content) ?? new List<OsmNode>();
        }

        public async Task<OsmNode?> GetByIdAsync(string id)
        {
            return (await GetAsync()).FirstOrDefault(x => x.Id == id);
        }

        public async Task<bool> TryUpdateCacheAsync(IList<OsmNode> values)
        {
            var success = false;
            try
            {
                await UpdateAsync(JsonConvert.SerializeObject(values), _blobName);
                success = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            return success;
        }

        private async Task UpdateAsync(string jsonData, string blobName)
        {
            BlobClient blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(BinaryData.FromString(jsonData), overwrite: true);
        }
    }
}