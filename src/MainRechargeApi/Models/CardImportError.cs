using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainRechargeApi.Models
{
    [Table("CardImportErrors")]
    public class CardImportError
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long BatchId { get; set; }

        [ForeignKey(nameof(BatchId))]
        public CardImportBatch? Batch { get; set; }

        public int RowNumber { get; set; }

        [StringLength(1000)]
        public string? RawRowData { get; set; }

        [Required]
        [StringLength(500)]
        public string ErrorMessage { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
