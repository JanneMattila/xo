using System.Text.Json.Serialization;

namespace XO
{
    public class GamePlayersModel
    {
        [JsonPropertyName("x")]
        public Player X { get; set; }

        [JsonPropertyName("o")]
        public Player O { get; set; }
    }
}
