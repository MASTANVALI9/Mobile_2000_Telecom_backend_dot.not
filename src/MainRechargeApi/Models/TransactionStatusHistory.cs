using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainRechargeApi.Models
{
    [Table("TransactionStatusHistory")]
    public class TransactionStatusHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(50)]
        public string TransactionId { get; set; } = string.Empty;

        [StringLength(20)]
        public string? PreviousStatus { get; set; }

        [Required]
        [StringLength(20)]
        public string NewStatus { get; set; } = string.Empty;

        public DateTime ChangedDate { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string? Remarks { get; set; }
    }
}
