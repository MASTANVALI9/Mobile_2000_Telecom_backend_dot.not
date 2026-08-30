using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MainRechargeApi.Models
{
    [Table("CardImportBatches")]
    public class CardImportBatch
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(250)]
        public string FileName { get; set; } = string.Empty;

        public int TotalRows { get; set; }

        public int SuccessfulRows { get; set; }

        public int FailedRows { get; set; }

        [Required]
        [StringLength(100)]
        public string ImportedBy { get; set; } = string.Empty;

        public DateTime ImportedDate { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "PROCESSING";
    }
}
