using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace XO
{
    public class BoardModel
    {
        public BoardModel()
        {
            Rules = new List<string>();
            Moves = new List<int>();
        }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("size")]
        public string Size { get; set; }

        [JsonPropertyName("rules")]
        public List<string> Rules { get; }

        [JsonPropertyName("firstPlayer")]
        public char FirstPlayer { get; set; }

        [JsonPropertyName("currentPlayer")]
        public char CurrentPlayer { get; set; }

        [JsonPropertyName("moves")]
        public List<int> Moves { get; }

        [JsonPropertyName("winningStraight")]
        public List<int> WinningStraight { get; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("stateReason")]
        public string StateReason { get; set; }
    }
}
