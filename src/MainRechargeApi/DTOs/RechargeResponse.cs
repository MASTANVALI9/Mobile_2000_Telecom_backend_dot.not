namespace MainRechargeApi.DTOs
{
    public class RechargeResponse
    {
        public string TransactionId { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? CardSerialNumber { get; set; }
        public string? CardPin { get; set; }
        public string? ProviderReference { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
