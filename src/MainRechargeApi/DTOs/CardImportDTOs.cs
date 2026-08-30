using System;
using System.Collections.Generic;

namespace MainRechargeApi.DTOs
{
    public class CardImportResponse
    {
        public long BatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int Imported { get; set; }
        public int SuccessfulRows { get; set; }
        public int Failed { get; set; }
        public int FailedRows { get; set; }
        public int Duplicates { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ImportedBy { get; set; } = string.Empty;
        public DateTime ImportedDate { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<CardImportErrorDto> Errors { get; set; } = new List<CardImportErrorDto>();
    }

    public class CardImportErrorDto
    {
        public int RowNumber { get; set; }
        public string? RawRowData { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class CardImportBatchDto
    {
        public long BatchId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int SuccessfulRows { get; set; }
        public int FailedRows { get; set; }
        public string ImportedBy { get; set; } = string.Empty;
        public DateTime ImportedDate { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class CardInventorySummaryDto
    {
        public string OperatorName { get; set; } = string.Empty;
        public decimal Denomination { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class RawCsvImportRequest
    {
        public string FileName { get; set; } = "raw_import.csv";
        public string CsvContent { get; set; } = string.Empty;
        public string ImportedBy { get; set; } = "SYSTEM";
    }
}
