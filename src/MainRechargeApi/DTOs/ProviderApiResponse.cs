namespace MainRechargeApi.DTOs
{
    public class ProviderApiResponse
    {
        public string Status { get; set; } = string.Empty;
        public string ProviderReference { get; set; } = string.Empty;
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
