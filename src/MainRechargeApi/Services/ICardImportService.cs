using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using MainRechargeApi.DTOs;

namespace MainRechargeApi.Services
{
    public interface ICardImportService
    {
        /// <summary>
        /// Parses, validates, and imports card data from a CSV stream.
        /// </summary>
        /// <param name="stream">The CSV file/text content stream.</param>
        /// <param name="fileName">The source filename.</param>
        /// <param name="importedBy">The username or system principal initiating the import.</param>
        /// <returns>A summary of the batch import execution.</returns>
        Task<CardImportResponse> ImportCardsFromCsvAsync(Stream stream, string fileName, string importedBy);

        /// <summary>
        /// Retrieves paginated list of card import batches.
        /// </summary>
        Task<List<CardImportBatchDto>> GetBatchesAsync(int page = 1, int pageSize = 50);

        /// <summary>
        /// Retrieves full details and row error records for a specific batch.
        /// </summary>
        Task<CardImportResponse?> GetBatchDetailsAsync(long batchId);

        /// <summary>
        /// Retrieves card inventory breakdown grouped by operator, denomination, and status.
        /// </summary>
        Task<List<CardInventorySummaryDto>> GetInventorySummaryAsync();

        /// <summary>
        /// Generates a sample CSV template for card imports.
        /// </summary>
        Task<byte[]> GenerateCsvTemplateAsync();

        /// <summary>
        /// Exports voucher card inventory as a CSV file.
        /// </summary>
        Task<byte[]> ExportCardsToCsvAsync(string? operatorName = null, string? status = null);
    }
}
