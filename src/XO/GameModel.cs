using System;
using System.Text.Json.Serialization;

namespace XO
{
    public class GameModel
    {
        [JsonPropertyName("id")]
        public string ID { get; set; }

        [JsonPropertyName("uri")]
        public Uri Uri { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("players")]
        public GamePlayersModel Players { get; set; }

        [JsonPropertyName("game")]
        public BoardModel Board { get; set; }

        [JsonPropertyName("limits")]
        public GameLimitsModel Limits { get; set; }
    }
}
