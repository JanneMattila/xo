using System.Text.Json.Serialization;

namespace XO
{
    public class ErrorModel
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("subCode")]
        public int SubCode { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("details")]
        public string Details { get; set; }

        [JsonPropertyName("stackTrace")]
        public string StackTrace { get; set; }
    }
}
