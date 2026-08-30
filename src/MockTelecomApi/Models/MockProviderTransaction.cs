using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MockTelecomApi.Models
{
    [Table("MockProviderTransactions")]
    public class MockProviderTransaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ReferenceId { get; set; } = string.Empty;

        [Required]
        [StringLength(10)]
        public string MobileNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Operator { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string ProviderReference { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
