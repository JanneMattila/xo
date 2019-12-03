using System.Text.Json.Serialization;

namespace XO
{
    public class GameLimitsModel
    {
        [JsonPropertyName("moveTimeout")]
        public string MoveTimeout { get; set; }
    }
}
