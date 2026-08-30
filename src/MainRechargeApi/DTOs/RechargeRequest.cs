namespace MainRechargeApi.DTOs
{
    public class RechargeRequest
    {
        // Optional client transaction ID for idempotency
        public string? TransactionId { get; set; }
        public string MobileNumber { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}
