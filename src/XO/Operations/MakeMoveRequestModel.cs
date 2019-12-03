using System.Text.Json.Serialization;

namespace XO.Operations
{
    public class MakeMoveRequestModel
    {
        [JsonPropertyName("id")]
        public string ID { get; set; }

        [JsonPropertyName("move")]
        public int Move { get; set; }
    }
}
