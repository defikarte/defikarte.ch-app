using Newtonsoft.Json;
using System.Collections.Generic;

namespace DefikarteBackend.Model
{
    public class OsmNode
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("lat")]
        public double Lat { get; set; }

        [JsonProperty("lon")]
        public double Lon { get; set; }

        [JsonProperty("tags")]
        public Dictionary<string, string> Tags { get; set; } = new();
    }
}