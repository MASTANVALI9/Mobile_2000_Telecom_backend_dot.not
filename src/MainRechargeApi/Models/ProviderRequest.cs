using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainRechargeApi.Models
{
    [Table("ProviderRequests")]
    public class ProviderRequest
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(50)]
        public string TransactionId { get; set; } = string.Empty;

        [Required]
        [StringLength(250)]
        public string RequestUrl { get; set; } = string.Empty;

        [Required]
        public string RequestBody { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
