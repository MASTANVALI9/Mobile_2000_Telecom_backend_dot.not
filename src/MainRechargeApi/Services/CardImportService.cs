using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MainRechargeApi.Data;
using MainRechargeApi.DTOs;
using MainRechargeApi.Models;

namespace MainRechargeApi.Services
{
    public class CardImportService : ICardImportService
    {
        private readonly RechargeDbContext _context;
        private readonly ILogger<CardImportService> _logger;

        public CardImportService(RechargeDbContext context, ILogger<CardImportService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CardImportResponse> ImportCardsFromCsvAsync(Stream stream, string fileName, string importedBy)
        {
            _logger.LogInformation(
                "[CARD_IMPORT_START] Initiating card import for file: {FileName}, imported by: {ImportedBy}",
                fileName, importedBy
            );

            if (string.IsNullOrWhiteSpace(importedBy))
            {
                importedBy = "SYSTEM";
            }

            var operators = await _context.TelecomOperators
                .Where(o => o.IsActive)
                .ToListAsync();

            var operatorMap = new Dictionary<string, TelecomOperator>(StringComparer.OrdinalIgnoreCase);
            foreach (var op in operators)
            {
                operatorMap[op.Name] = op;
            }

            var validCardsToInsert = new List<RechargeCard>();
            var errorLogsToInsert = new List<CardImportError>();
            var responseErrors = new List<CardImportErrorDto>();

            var batchCardNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var batchSerialNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int totalRows = 0;
            int duplicatesCount = 0;
            int rowNumber = 0;

            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            {
                string? headerLine = await reader.ReadLineAsync();
                if (headerLine == null || string.IsNullOrWhiteSpace(headerLine))
                {
                    _logger.LogWarning("[CARD_IMPORT_ERROR] Uploaded file {FileName} is completely empty.", fileName);
                    return new CardImportResponse
                    {
                        FileName = fileName,
                        TotalRows = 0,
                        Imported = 0,
                        SuccessfulRows = 0,
                        Failed = 0,
                        FailedRows = 0,
                        Duplicates = 0,
                        Status = "FAILED",
                        ImportedBy = importedBy,
                        ImportedDate = DateTime.UtcNow,
                        Message = "CSV file is empty."
                    };
                }

                // Parse header columns
                var headers = ParseCsvLine(headerLine);
                int colCardNumber = -1, colSerialNumber = -1, colOperator = -1, colDenomination = -1, colExpiryDate = -1;

                for (int i = 0; i < headers.Length; i++)
                {
                    string h = headers[i].Trim().Replace(" ", "").Replace("_", "").ToLowerInvariant();
                    if (h.Equals("cardnumber", StringComparison.OrdinalIgnoreCase) || h.Equals("cardno", StringComparison.OrdinalIgnoreCase) || h.Equals("pin", StringComparison.OrdinalIgnoreCase))
                        colCardNumber = i;
                    else if (h.Equals("serialnumber", StringComparison.OrdinalIgnoreCase) || h.Equals("serialno", StringComparison.OrdinalIgnoreCase) || h.Equals("serial", StringComparison.OrdinalIgnoreCase))
                        colSerialNumber = i;
                    else if (h.Equals("operator", StringComparison.OrdinalIgnoreCase) || h.Equals("operatorname", StringComparison.OrdinalIgnoreCase))
                        colOperator = i;
                    else if (h.Equals("denomination", StringComparison.OrdinalIgnoreCase) || h.Equals("amount", StringComparison.OrdinalIgnoreCase) || h.Equals("value", StringComparison.OrdinalIgnoreCase))
                        colDenomination = i;
                    else if (h.Equals("expirydate", StringComparison.OrdinalIgnoreCase) || h.Equals("expiry", StringComparison.OrdinalIgnoreCase) || h.Equals("expirationdate", StringComparison.OrdinalIgnoreCase))
                        colExpiryDate = i;
                }

                // If headers were not recognized by name, fallback to standard 0..4 position if at least 5 columns exist
                if (colCardNumber == -1 || colSerialNumber == -1 || colOperator == -1 || colDenomination == -1 || colExpiryDate == -1)
                {
                    if (headers.Length >= 5)
                    {
                        colCardNumber = 0;
                        colSerialNumber = 1;
                        colOperator = 2;
                        colDenomination = 3;
                        colExpiryDate = 4;
                    }
                    else
                    {
                        _logger.LogWarning("[CARD_IMPORT_ERROR] CSV Header missing required columns in file {FileName}. Headers: {Headers}", fileName, string.Join(",", headers));
                        return new CardImportResponse
                        {
                            FileName = fileName,
                            TotalRows = 0,
                            Imported = 0,
                            SuccessfulRows = 0,
                            Failed = 0,
                            FailedRows = 0,
                            Duplicates = 0,
                            Status = "FAILED",
                            ImportedBy = importedBy,
                            ImportedDate = DateTime.UtcNow,
                            Message = "Invalid CSV header format. Expected columns: CardNumber,SerialNumber,Operator,Denomination,ExpiryDate"
                        };
                    }
                }

                // Query existing card numbers & serial numbers from database to check for collisions
                var existingCardNumbers = new HashSet<string>(
                    await _context.RechargeCards.Select(c => c.CardNumber).ToListAsync(),
                    StringComparer.OrdinalIgnoreCase
                );

                var existingSerialNumbers = new HashSet<string>(
                    await _context.RechargeCards.Select(c => c.SerialNumber).ToListAsync(),
                    StringComparer.OrdinalIgnoreCase
                );

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    rowNumber++;
                    totalRows++;

                    var columns = ParseCsvLine(line);
                    int maxCol = Math.Max(colCardNumber, Math.Max(colSerialNumber, Math.Max(colOperator, Math.Max(colDenomination, colExpiryDate))));

                    if (columns.Length <= maxCol)
                    {
                        string error = $"Row {rowNumber} – Missing Columns (Expected at least 5 columns, found {columns.Length})";
                        _logger.LogWarning("[CARD_IMPORT_ROW_ERROR] {Error}. Raw: {Line}", error, line);
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    string rawCardNumber = columns[colCardNumber].Trim();
                    string rawSerialNumber = columns[colSerialNumber].Trim();
                    string rawOperator = columns[colOperator].Trim();
                    string rawDenomination = columns[colDenomination].Trim();
                    string rawExpiryDate = columns[colExpiryDate].Trim();

                    if (string.IsNullOrWhiteSpace(rawCardNumber))
                    {
                        string error = $"Row {rowNumber} – Missing CardNumber";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (rawCardNumber.Length < 6 || rawCardNumber.Length > 50)
                    {
                        string error = $"Row {rowNumber} – Invalid CardNumber Length (Must be 6-50 characters)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(rawSerialNumber))
                    {
                        string error = $"Row {rowNumber} – Missing SerialNumber";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (rawSerialNumber.Length < 3 || rawSerialNumber.Length > 50)
                    {
                        string error = $"Row {rowNumber} – Invalid SerialNumber Length (Must be 3-50 characters)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    // Check intra-batch and database uniqueness
                    if (batchCardNumbers.Contains(rawCardNumber))
                    {
                        duplicatesCount++;
                        string error = $"Row {rowNumber} – Duplicate CardNumber (Found duplicate '{rawCardNumber}' within this batch)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (batchSerialNumbers.Contains(rawSerialNumber))
                    {
                        duplicatesCount++;
                        string error = $"Row {rowNumber} – Duplicate SerialNumber (Found duplicate '{rawSerialNumber}' within this batch)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (existingCardNumbers.Contains(rawCardNumber))
                    {
                        duplicatesCount++;
                        string error = $"Row {rowNumber} – Duplicate CardNumber (CardNumber '{rawCardNumber}' already exists in database)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (existingSerialNumbers.Contains(rawSerialNumber))
                    {
                        duplicatesCount++;
                        string error = $"Row {rowNumber} – Duplicate SerialNumber (SerialNumber '{rawSerialNumber}' already exists in database)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    string normalizedOpKey = NormalizeOperatorName(rawOperator);
                    if (!operatorMap.TryGetValue(normalizedOpKey, out var opRecord))
                    {
                        string error = $"Row {rowNumber} – Invalid Operator ('{rawOperator}' is unsupported or inactive)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (!decimal.TryParse(rawDenomination, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal denomination) || denomination <= 0)
                    {
                        string error = $"Row {rowNumber} – Invalid Denomination ('{rawDenomination}' must be a positive decimal number)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (!DateTime.TryParseExact(rawExpiryDate, new[] { "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime expiryDate)
                        && !DateTime.TryParse(rawExpiryDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out expiryDate))
                    {
                        string error = $"Row {rowNumber} – Invalid ExpiryDate ('{rawExpiryDate}' is invalid; expected YYYY-MM-DD)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    if (expiryDate.Date < DateTime.UtcNow.Date)
                    {
                        string error = $"Row {rowNumber} – Invalid ExpiryDate (Card is expired; expiry date '{rawExpiryDate}' cannot be in the past)";
                        responseErrors.Add(new CardImportErrorDto { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        errorLogsToInsert.Add(new CardImportError { RowNumber = rowNumber, RawRowData = line, ErrorMessage = error });
                        continue;
                    }

                    batchCardNumbers.Add(rawCardNumber);
                    batchSerialNumbers.Add(rawSerialNumber);
                    existingCardNumbers.Add(rawCardNumber);
                    existingSerialNumbers.Add(rawSerialNumber);

                    validCardsToInsert.Add(new RechargeCard
                    {
                        CardNumber = rawCardNumber,
                        SerialNumber = rawSerialNumber,
                        OperatorId = opRecord.Id,
                        Denomination = denomination,
                        Status = "AVAILABLE",
                        ExpiryDate = expiryDate.Date,
                        ImportedDate = DateTime.UtcNow
                    });
                }
            }

            int successfulRows = validCardsToInsert.Count;
            int failedRows = errorLogsToInsert.Count;
            string batchStatus = (successfulRows > 0 && failedRows > 0) 
                ? "PARTIAL_SUCCESS" 
                : (successfulRows > 0 ? "COMPLETED" : "FAILED");

            // Save to database atomically within an ACID transaction
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var batchRecord = new CardImportBatch
                {
                    FileName = Path.GetFileName(fileName),
                    TotalRows = totalRows,
                    SuccessfulRows = successfulRows,
                    FailedRows = failedRows,
                    ImportedBy = importedBy,
                    ImportedDate = DateTime.UtcNow,
                    Status = batchStatus
                };

                _context.CardImportBatches.Add(batchRecord);
                await _context.SaveChangesAsync();

                long batchId = batchRecord.Id;

                // Associate batchId with valid cards
                foreach (var card in validCardsToInsert)
                {
                    card.BatchId = batchId;
                }

                // Associate batchId with errors
                foreach (var err in errorLogsToInsert)
                {
                    err.BatchId = batchId;
                }

                // Batch insert valid cards in chunks of 2,000 for high performance with large files
                const int chunkSize = 2000;
                for (int i = 0; i < validCardsToInsert.Count; i += chunkSize)
                {
                    var chunk = validCardsToInsert.Skip(i).Take(chunkSize);
                    _context.RechargeCards.AddRange(chunk);
                    await _context.SaveChangesAsync();
                }

                // Batch insert errors in chunks
                for (int i = 0; i < errorLogsToInsert.Count; i += chunkSize)
                {
                    var chunk = errorLogsToInsert.Skip(i).Take(chunkSize);
                    _context.CardImportErrors.AddRange(chunk);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                _logger.LogInformation(
                    "[CARD_IMPORT_COMPLETED] Batch {BatchId} processed: Total: {Total}, Successful: {Success}, Failed: {Failed}, Duplicates: {Duplicates}, Status: {Status}",
                    batchId, totalRows, successfulRows, failedRows, duplicatesCount, batchStatus
                );

                string message = batchStatus switch
                {
                    "COMPLETED" => $"Successfully imported all {successfulRows} voucher cards.",
                    "PARTIAL_SUCCESS" => $"Import completed with warnings: {successfulRows} cards imported, {failedRows} rows failed validation ({duplicatesCount} duplicates).",
                    _ => $"Import failed: 0 cards imported, {failedRows} rows failed validation ({duplicatesCount} duplicates)."
                };

                return new CardImportResponse
                {
                    BatchId = batchId,
                    FileName = Path.GetFileName(fileName),
                    TotalRows = totalRows,
                    Imported = successfulRows,
                    SuccessfulRows = successfulRows,
                    Failed = failedRows,
                    FailedRows = failedRows,
                    Duplicates = duplicatesCount,
                    Status = batchStatus,
                    ImportedBy = importedBy,
                    ImportedDate = batchRecord.ImportedDate,
                    Message = message,
                    Errors = responseErrors
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "[CARD_IMPORT_FAILED] Database transaction failed during batch import for file {FileName}: {Message}", fileName, ex.Message);
                throw;
            }
        }

        public async Task<List<CardImportBatchDto>> GetBatchesAsync(int page = 1, int pageSize = 50)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 50;

            return await _context.CardImportBatches
                .OrderByDescending(b => b.ImportedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => new CardImportBatchDto
                {
                    BatchId = b.Id,
                    FileName = b.FileName,
                    TotalRows = b.TotalRows,
                    SuccessfulRows = b.SuccessfulRows,
                    FailedRows = b.FailedRows,
                    ImportedBy = b.ImportedBy,
                    ImportedDate = b.ImportedDate,
                    Status = b.Status
                })
                .ToListAsync();
        }

        public async Task<CardImportResponse?> GetBatchDetailsAsync(long batchId)
        {
            var batch = await _context.CardImportBatches.FindAsync(batchId);
            if (batch == null)
                return null;

            var errors = await _context.CardImportErrors
                .Where(e => e.BatchId == batchId)
                .OrderBy(e => e.RowNumber)
                .Select(e => new CardImportErrorDto
                {
                    RowNumber = e.RowNumber,
                    RawRowData = e.RawRowData,
                    ErrorMessage = e.ErrorMessage
                })
                .ToListAsync();

            return new CardImportResponse
            {
                BatchId = batch.Id,
                FileName = batch.FileName,
                TotalRows = batch.TotalRows,
                SuccessfulRows = batch.SuccessfulRows,
                FailedRows = batch.FailedRows,
                Status = batch.Status,
                ImportedBy = batch.ImportedBy,
                ImportedDate = batch.ImportedDate,
                Message = $"Batch {batch.Id} status is {batch.Status}.",
                Errors = errors
            };
        }

        public async Task<List<CardInventorySummaryDto>> GetInventorySummaryAsync()
        {
            return await _context.RechargeCards
                .Include(c => c.Operator)
                .GroupBy(c => new { OperatorName = c.Operator != null ? c.Operator.Name : "Unknown", c.Denomination, c.Status })
                .Select(g => new CardInventorySummaryDto
                {
                    OperatorName = g.Key.OperatorName,
                    Denomination = g.Key.Denomination,
                    Status = g.Key.Status,
                    Count = g.Count()
                })
                .OrderBy(r => r.OperatorName)
                .ThenBy(r => r.Denomination)
                .ThenBy(r => r.Status)
                .ToListAsync();
        }

        public Task<byte[]> GenerateCsvTemplateAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("CardNumber,SerialNumber,Operator,Denomination,ExpiryDate");
            sb.AppendLine("987654321001,SER10001,Airtel,100,2027-12-31");
            sb.AppendLine("987654321002,SER10002,Jio,199,2027-12-31");
            sb.AppendLine("987654321003,SER10003,Vi,249,2027-12-31");
            sb.AppendLine("987654321004,SER10004,BSNL,50,2027-12-31");
            sb.AppendLine("987654321005,SER10005,BSNL,100,2027-12-31");

            return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        public async Task<byte[]> ExportCardsToCsvAsync(string? operatorName = null, string? status = null)
        {
            var query = _context.RechargeCards.Include(c => c.Operator).AsQueryable();

            if (!string.IsNullOrWhiteSpace(operatorName))
            {
                string normOp = NormalizeOperatorName(operatorName);
                query = query.Where(c => c.Operator != null && c.Operator.Name == normOp);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(c => c.Status == status);
            }

            var cards = await query
                .OrderBy(c => c.Operator != null ? c.Operator.Name : "")
                .ThenBy(c => c.Denomination)
                .ThenBy(c => c.Id)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("CardNumber,SerialNumber,Operator,Denomination,Status,ExpiryDate,ImportedDate,UsedTransactionId,UsedDate");

            foreach (var card in cards)
            {
                string opName = card.Operator?.Name ?? "Unknown";
                string usedTxn = card.UsedTransactionId ?? "";
                string usedDate = card.UsedDate.HasValue ? card.UsedDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : "";

                sb.AppendLine($"{card.CardNumber},{card.SerialNumber},{opName},{card.Denomination.ToString("0.00", CultureInfo.InvariantCulture)},{card.Status},{card.ExpiryDate:yyyy-MM-dd},{card.ImportedDate:yyyy-MM-dd HH:mm:ss},{usedTxn},{usedDate}");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private static string[] ParseCsvLine(string line)
        {
            var values = new List<string>();
            var currentValue = new StringBuilder();
            bool insideQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentValue.Append('"');
                        i++; // skip escaped quote
                    }
                    else
                    {
                        insideQuotes = !insideQuotes;
                    }
                }
                else if (c == ',' && !insideQuotes)
                {
                    values.Add(currentValue.ToString());
                    currentValue.Clear();
                }
                else
                {
                    currentValue.Append(c);
                }
            }

            values.Add(currentValue.ToString());
            return values.ToArray();
        }

        private static string NormalizeOperatorName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string clean = input.Trim();

            if (clean.Equals("jio", StringComparison.OrdinalIgnoreCase) || clean.Equals("reliance jio", StringComparison.OrdinalIgnoreCase))
                return "Jio";
            if (clean.Equals("airtel", StringComparison.OrdinalIgnoreCase) || clean.Equals("bharti airtel", StringComparison.OrdinalIgnoreCase))
                return "Airtel";
            if (clean.Equals("vi", StringComparison.OrdinalIgnoreCase) || clean.Equals("vodafone idea", StringComparison.OrdinalIgnoreCase) || clean.Equals("vodafone", StringComparison.OrdinalIgnoreCase) || clean.Equals("idea", StringComparison.OrdinalIgnoreCase))
                return "Vi";
            if (clean.Equals("bsnl", StringComparison.OrdinalIgnoreCase))
                return "BSNL";

            return clean;
        }
    }
}
