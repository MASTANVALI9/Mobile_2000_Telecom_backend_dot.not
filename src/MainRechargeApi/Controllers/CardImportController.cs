using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MainRechargeApi.DTOs;
using MainRechargeApi.Services;

namespace MainRechargeApi.Controllers
{
    [ApiController]
    [Route("api/cards")]
    public class CardImportController : ControllerBase
    {
        private readonly ICardImportService _cardImportService;
        private readonly ILogger<CardImportController> _logger;

        public CardImportController(ICardImportService cardImportService, ILogger<CardImportController> logger)
        {
            _cardImportService = cardImportService;
            _logger = logger;
        }

        [HttpPost("import")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(CardImportResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ImportCsvFile([FromForm] IFormFile? file, [FromForm] string? importedBy)
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("[VALIDATION_ERROR] CSV file was not uploaded or is empty.");
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "EMPTY_FILE",
                    "Please upload a valid non-empty CSV file."
                ));
            }

            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension != ".csv" && extension != ".txt")
            {
                _logger.LogWarning("[VALIDATION_ERROR] Invalid file extension '{Extension}' for card import.", extension);
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "INVALID_FILE_TYPE",
                    "Only .csv and .txt files are supported for voucher imports."
                ));
            }

            // Limit upload size to 10MB
            if (file.Length > 10 * 1024 * 1024)
            {
                _logger.LogWarning("[VALIDATION_ERROR] Uploaded file size {Size} bytes exceeds maximum limit (10MB).", file.Length);
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "FILE_TOO_LARGE",
                    "File size exceeds the 10MB limit."
                ));
            }

            string userPrincipal = !string.IsNullOrWhiteSpace(importedBy) ? importedBy.Trim() : "ADMIN";

            try
            {
                using var stream = file.OpenReadStream();
                var result = await _cardImportService.ImportCardsFromCsvAsync(stream, file.FileName, userPrincipal);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CARD_IMPORT_ERROR] Exception occurred while importing file {FileName}: {Message}", file.FileName, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "IMPORT_ERROR",
                    $"Internal server error while processing CSV: {ex.Message}"
                ));
            }
        }

        /// <summary>
        /// Imports prepaid cards/vouchers from raw CSV text content in JSON payload.
        /// </summary>
        [HttpPost("import/raw")]
        [ProducesResponseType(typeof(CardImportResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ImportRawCsv([FromBody] RawCsvImportRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.CsvContent))
            {
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "EMPTY_CONTENT",
                    "CSV content is required."
                ));
            }

            string fileName = string.IsNullOrWhiteSpace(request.FileName) ? "raw_import.csv" : request.FileName.Trim();
            string userPrincipal = string.IsNullOrWhiteSpace(request.ImportedBy) ? "API_CLIENT" : request.ImportedBy.Trim();

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(request.CsvContent);
                using var stream = new MemoryStream(bytes);
                var result = await _cardImportService.ImportCardsFromCsvAsync(stream, fileName, userPrincipal);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CARD_IMPORT_ERROR] Exception occurred while importing raw CSV: {Message}", ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "IMPORT_ERROR",
                    $"Internal server error while processing raw CSV: {ex.Message}"
                ));
            }
        }

        /// <summary>
        /// Retrieves the list of past card import batches.
        /// </summary>
        [HttpGet("batches")]
        [ProducesResponseType(typeof(List<CardImportBatchDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetBatches([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            var batches = await _cardImportService.GetBatchesAsync(page, pageSize);
            return Ok(batches);
        }

        /// <summary>
        /// Retrieves details of a specific batch including row validation errors.
        /// </summary>
        [HttpGet("batches/{batchId:long}")]
        [ProducesResponseType(typeof(CardImportResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBatchDetails([FromRoute] long batchId)
        {
            var batch = await _cardImportService.GetBatchDetailsAsync(batchId);
            if (batch == null)
            {
                return NotFound(new ApiErrorResponse(
                    StatusCodes.Status404NotFound,
                    "BATCH_NOT_FOUND",
                    $"Card import batch with ID {batchId} does not exist."
                ));
            }

            return Ok(batch);
        }

        /// <summary>
        /// Retrieves inventory stock summary grouped by operator, denomination, and availability status.
        /// </summary>
        [HttpGet("inventory")]
        [ProducesResponseType(typeof(List<CardInventorySummaryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetInventorySummary()
        {
            var inventory = await _cardImportService.GetInventorySummaryAsync();
            return Ok(inventory);
        }

        /// <summary>
        /// Downloads sample CSV template for card imports.
        /// </summary>
        [HttpGet("template/csv")]
        [Produces("text/csv")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> DownloadCsvTemplate()
        {
            var bytes = await _cardImportService.GenerateCsvTemplateAsync();
            return File(bytes, "text/csv", "card_import_template.csv");
        }

        /// <summary>
        /// Exports all voucher cards in inventory as a downloadable CSV file.
        /// </summary>
        [HttpGet("export/csv")]
        [Produces("text/csv")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ExportCardsCsv([FromQuery] string? operatorName = null, [FromQuery] string? status = null)
        {
            var bytes = await _cardImportService.ExportCardsToCsvAsync(operatorName, status);
            string fileName = $"voucher_cards_{(string.IsNullOrWhiteSpace(operatorName) ? "all" : operatorName.ToLowerInvariant())}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv", fileName);
        }
    }
}
