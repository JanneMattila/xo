using System;
using System.Text.Json.Serialization;

namespace XO
{
    public class Player
    {
        [JsonPropertyName("id")]
        public string ID { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("uri")]
        public Uri Uri { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }
    }
}
