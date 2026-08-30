using System.Text.Json.Serialization;

namespace MainRechargeApi.DTOs
{
    public class ApiErrorResponse
    {
        public int StatusCode { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Details { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public ApiErrorResponse() { }

        public ApiErrorResponse(int statusCode, string error, string message, List<string>? details = null)
        {
            StatusCode = statusCode;
            Error = error;
            Message = message;
            Details = details;
            Timestamp = DateTime.UtcNow;
        }
    }
}
