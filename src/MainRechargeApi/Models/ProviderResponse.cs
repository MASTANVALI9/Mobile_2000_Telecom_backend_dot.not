using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainRechargeApi.Models
{
    [Table("ProviderResponses")]
    public class ProviderResponse
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(50)]
        public string TransactionId { get; set; } = string.Empty;

        public int StatusCode { get; set; }

        public string? ResponseBody { get; set; }

        public int ResponseTimeMs { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
