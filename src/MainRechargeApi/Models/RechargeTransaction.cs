using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainRechargeApi.Models
{
    [Table("RechargeTransactions")]
    public class RechargeTransaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(50)]
        public string TransactionId { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string MobileNumber { get; set; } = string.Empty;

        public int OperatorId { get; set; }

        [ForeignKey(nameof(OperatorId))]
        public TelecomOperator? Operator { get; set; }

        [NotMapped]
        public string? OperatorName { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "NEW";

        [StringLength(100)]
        public string? ProviderReference { get; set; }

        [StringLength(500)]
        public string? ErrorMessage { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    }
}
