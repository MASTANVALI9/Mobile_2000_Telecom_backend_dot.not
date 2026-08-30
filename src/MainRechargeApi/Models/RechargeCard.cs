using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainRechargeApi.Models
{
    [Table("RechargeCards")]
    public class RechargeCard
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(50)]
        public string CardNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string SerialNumber { get; set; } = string.Empty;

        public int OperatorId { get; set; }

        [ForeignKey(nameof(OperatorId))]
        public TelecomOperator? Operator { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Denomination { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "AVAILABLE";

        public DateTime ExpiryDate { get; set; }

        public DateTime ImportedDate { get; set; } = DateTime.UtcNow;

        public long BatchId { get; set; }

        [ForeignKey(nameof(BatchId))]
        public CardImportBatch? Batch { get; set; }

        [StringLength(50)]
        public string? UsedTransactionId { get; set; }

        public DateTime? UsedDate { get; set; }
    }
}
