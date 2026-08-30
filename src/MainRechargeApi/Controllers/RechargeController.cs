using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MainRechargeApi.Data;
using MainRechargeApi.DTOs;
using MainRechargeApi.Models;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace MainRechargeApi.Controllers
{
    [ApiController]
    [Route("api/recharge")]
    public class RechargeController : ControllerBase
    {
        private readonly RechargeDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RechargeController> _logger;

        public RechargeController(
            RechargeDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<RechargeController> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Recharge([FromBody] RechargeRequest request)
        {
            _logger.LogInformation(
                "[INCOMING_REQUEST] Received recharge request. MobileNumber: {MobileNumber}, Operator: {OperatorName}, Amount: {Amount}, ClientTxnId: {ClientTxnId}",
                request?.MobileNumber,
                request?.OperatorName,
                request?.Amount,
                request?.TransactionId ?? "(auto-generated)"
            );

            if (request == null)
            {
                _logger.LogWarning("[VALIDATION_ERROR] Recharge request body was null.");
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "INVALID_REQUEST",
                    "Request body cannot be null."
                ));
            }

            if (string.IsNullOrWhiteSpace(request.MobileNumber) || request.MobileNumber.Length != 10 || !request.MobileNumber.All(char.IsDigit))
            {
                _logger.LogWarning("[VALIDATION_ERROR] Invalid mobile number supplied: '{MobileNumber}'. Must be 10 digits.", request.MobileNumber);
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "INVALID_MOBILE_NUMBER",
                    "Mobile number must be exactly 10 digits numeric (e.g. 9876543210)."
                ));
            }

            if (string.IsNullOrWhiteSpace(request.OperatorName))
            {
                _logger.LogWarning("[VALIDATION_ERROR] Operator name was missing.");
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "INVALID_OPERATOR",
                    "Operator name is required. Supported operators: Jio, Airtel, Vi, BSNL."
                ));
            }

            if (request.Amount <= 0)
            {
                _logger.LogWarning("[VALIDATION_ERROR] Recharge amount must be positive. Supplied: {Amount}", request.Amount);
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "INVALID_AMOUNT",
                    "Recharge amount must be greater than zero."
                ));
            }

            if (request.Amount > 50000)
            {
                _logger.LogWarning("[VALIDATION_ERROR] Recharge amount exceeds maximum allowed limit. Supplied: {Amount}", request.Amount);
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "INVALID_AMOUNT",
                    "Recharge amount exceeds the maximum limit of ₹50,000."
                ));
            }

            string normalizedOperator = NormalizeOperatorName(request.OperatorName);
            TelecomOperator? operatorRecord;

            try
            {
                operatorRecord = await _context.TelecomOperators
                    .FirstOrDefaultAsync(o => o.Name == normalizedOperator && o.IsActive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DATABASE_ERROR] Error looking up operator record for '{OperatorName}': {Message}", request.OperatorName, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "DATABASE_ERROR",
                    "Database error while validating operator."
                ));
            }

            if (operatorRecord == null)
            {
                _logger.LogWarning("[VALIDATION_ERROR] Operator '{OperatorName}' is unsupported or inactive.", request.OperatorName);
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "UNSUPPORTED_OPERATOR",
                    $"Operator '{request.OperatorName}' is not supported or is currently inactive. Supported operators: Jio, Airtel, Vi, BSNL."
                ));
            }

            string transactionId = !string.IsNullOrWhiteSpace(request.TransactionId)
                ? request.TransactionId.Trim()
                : $"TXN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper()}";

            _logger.LogInformation(
                "[TRANSACTION_ID] Transaction ID determined: {TransactionId} for MobileNumber: {MobileNumber}, Operator: {OperatorName}, Amount: {Amount}",
                transactionId,
                request.MobileNumber,
                operatorRecord.Name,
                request.Amount
            );

            // Return existing transaction if duplicate client ID is replayed
            try
            {
                var existingTx = await _context.GetTransactionByTransactionIdAsync(transactionId);
                if (existingTx != null)
                {
                    _logger.LogInformation(
                        "[TRANSACTION_ID] Idempotent retry detected for TransactionId: {TransactionId}. Returning existing Status: {Status}",
                        existingTx.TransactionId,
                        existingTx.Status
                    );

                    var op = await _context.TelecomOperators.FindAsync(existingTx.OperatorId);

                    string? cardSerial = null;
                    string? cardPin = null;
                    if (existingTx.OperatorName == "BSNL" && existingTx.Status == "SUCCESS")
                    {
                        var card = await _context.RechargeCards
                            .FirstOrDefaultAsync(c => c.UsedTransactionId == existingTx.TransactionId);
                        if (card != null)
                        {
                            cardSerial = card.SerialNumber;
                            cardPin = card.CardNumber;
                        }
                    }

                    return Ok(new RechargeResponse
                    {
                        TransactionId = existingTx.TransactionId,
                        MobileNumber = existingTx.MobileNumber,
                        OperatorName = op?.Name ?? string.Empty,
                        Amount = existingTx.Amount,
                        Status = existingTx.Status,
                        ProviderReference = existingTx.ProviderReference,
                        Message = "Already recharged! This pack will activate after completion of the first pack.",
                        ErrorMessage = existingTx.ErrorMessage,
                        CardSerialNumber = cardSerial,
                        CardPin = cardPin
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DATABASE_ERROR] Error checking existing transaction {TransactionId}: {Message}", transactionId, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "DATABASE_ERROR",
                    "Database error while verifying transaction uniqueness."
                ));
            }

            // Prevent accidental duplicate recharges within a 10-minute window
            try
            {
                var recentDuplicate = await _context.RechargeTransactions
                    .Where(t => t.MobileNumber == request.MobileNumber
                             && t.OperatorId == operatorRecord.Id
                             && t.Amount == request.Amount
                             && (t.Status == "SUCCESS" || t.Status == "PENDING")
                             && t.CreatedDate >= DateTime.UtcNow.AddMinutes(-10))
                    .OrderByDescending(t => t.CreatedDate)
                    .FirstOrDefaultAsync();

                if (recentDuplicate != null && string.IsNullOrWhiteSpace(request.TransactionId))
                {
                    _logger.LogInformation(
                        "[TRANSACTION_ID] Recent duplicate recharge detected for Mobile: {MobileNumber}, Operator: {Operator}, Amount: {Amount}. Returning existing TransactionId: {TransactionId}, Status: {Status}",
                        request.MobileNumber,
                        operatorRecord.Name,
                        request.Amount,
                        recentDuplicate.TransactionId,
                        recentDuplicate.Status
                    );

                    return Ok(new RechargeResponse
                    {
                        TransactionId = recentDuplicate.TransactionId,
                        MobileNumber = recentDuplicate.MobileNumber,
                        OperatorName = operatorRecord.Name,
                        Amount = recentDuplicate.Amount,
                        Status = recentDuplicate.Status,
                        ProviderReference = recentDuplicate.ProviderReference,
                        Message = "Already recharged! This pack will activate after completion of the first pack.",
                        ErrorMessage = null
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DATABASE_ERROR] Error checking recent duplicates for mobile {MobileNumber}: {Message}", request.MobileNumber, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "DATABASE_ERROR",
                    "Database error while checking duplicate transactions."
                ));
            }

            Models.RechargeTransaction? createdTx;
            try
            {
                createdTx = await _context.CreateRechargeTransactionAsync(
                    transactionId,
                    request.MobileNumber,
                    operatorRecord.Name,
                    request.Amount
                );

                _logger.LogInformation(
                    "[STATUS_CHANGE] TransactionId: {TransactionId} created successfully in database with Status: NEW",
                    transactionId
                );
            }
            catch (Exception ex) when (ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                                    || ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true
                                    || ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                                    || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    ex,
                    "[EXCEPTION] Concurrent duplicate collision on unique TransactionId constraint for: {TransactionId}. Fetching existing transaction.",
                    transactionId
                );

                var concurrentTx = await _context.GetTransactionByTransactionIdAsync(transactionId);
                if (concurrentTx != null)
                {
                    var op = await _context.TelecomOperators.FindAsync(concurrentTx.OperatorId);
                    return Ok(new RechargeResponse
                    {
                        TransactionId = concurrentTx.TransactionId,
                        MobileNumber = concurrentTx.MobileNumber,
                        OperatorName = op?.Name ?? string.Empty,
                        Amount = concurrentTx.Amount,
                        Status = concurrentTx.Status,
                        ProviderReference = concurrentTx.ProviderReference,
                        Message = "Already recharged! This pack will activate after completion of the first pack.",
                        ErrorMessage = concurrentTx.ErrorMessage
                    });
                }

                return Conflict(new ApiErrorResponse(
                    StatusCodes.Status409Conflict,
                    "DUPLICATE_TRANSACTION",
                    $"Duplicate transaction ID '{transactionId}'. The transaction already exists."
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[EXCEPTION] Error creating transaction record in database for TransactionId: {TransactionId}. Error: {Message}",
                    transactionId,
                    ex.Message
                );
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "DATABASE_ERROR",
                    "Failed to initialize the recharge transaction in database. Please try again later."
                ));
            }

            if (createdTx == null)
            {
                _logger.LogError("[EXCEPTION] Stored procedure returned null for created transaction {TransactionId}", transactionId);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "DATABASE_ERROR",
                    "Failed to initialize the recharge transaction in database."
                ));
            }

            // BSNL uses voucher inventory; other operators route through provider API
            if (normalizedOperator == "BSNL")
                return await ProcessCardRecharge(createdTx);
            else
                return await ProcessApiRecharge(createdTx, normalizedOperator);
        }

        [HttpGet("status/{transactionId}")]
        public async Task<IActionResult> GetStatus(string transactionId)
        {
            if (string.IsNullOrWhiteSpace(transactionId))
            {
                return BadRequest(new ApiErrorResponse(
                    StatusCodes.Status400BadRequest,
                    "INVALID_TRANSACTION_ID",
                    "Transaction ID is required."
                ));
            }

            _logger.LogInformation(
                "[STATUS_ENQUIRY] Received status enquiry for TransactionId: {TransactionId}",
                transactionId
            );

            RechargeTransaction? tx;
            try
            {
                tx = await _context.GetTransactionByTransactionIdAsync(transactionId.Trim());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DATABASE_ERROR] Error fetching transaction status for {TransactionId}: {Message}", transactionId, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(
                    StatusCodes.Status500InternalServerError,
                    "DATABASE_ERROR",
                    "Database error while querying transaction status."
                ));
            }

            if (tx == null)
            {
                _logger.LogWarning(
                    "[STATUS_ENQUIRY] Transaction not found for TransactionId: {TransactionId}",
                    transactionId
                );
                return NotFound(new ApiErrorResponse(
                    StatusCodes.Status404NotFound,
                    "TRANSACTION_NOT_FOUND",
                    $"Transaction with ID '{transactionId}' was not found."
                ));
            }

            _logger.LogInformation(
                "[STATUS_ENQUIRY] Found TransactionId: {TransactionId}, Operator: {Operator}, Amount: {Amount}, Status: {Status}, ProviderRef: {ProviderRef}",
                tx.TransactionId,
                tx.OperatorName,
                tx.Amount,
                tx.Status,
                tx.ProviderReference ?? "(none)"
            );

            string? cardSerial = null;
            string? cardPin = null;

            if (tx.OperatorName == "BSNL" && tx.Status == "SUCCESS")
            {
                var card = await _context.RechargeCards
                    .FirstOrDefaultAsync(c => c.UsedTransactionId == tx.TransactionId);
                if (card != null)
                {
                    cardSerial = card.SerialNumber;
                    cardPin = card.CardNumber;
                }
            }

            var op = await _context.TelecomOperators.FindAsync(tx.OperatorId);

            return Ok(new RechargeResponse
            {
                TransactionId = tx.TransactionId,
                MobileNumber = tx.MobileNumber,
                OperatorName = op?.Name ?? string.Empty,
                Amount = tx.Amount,
                Status = tx.Status,
                ProviderReference = tx.ProviderReference,
                ErrorMessage = tx.ErrorMessage,
                CardSerialNumber = cardSerial,
                CardPin = cardPin
            });
        }

        private string NormalizeOperatorName(string name)
        {
            name = name.Trim().ToLower();
            if (name.Contains("jio")) return "Jio";
            if (name.Contains("airtel")) return "Airtel";
            if (name.Contains("vi") || name.Equals("vodafone") || name.Equals("idea")) return "Vi";
            if (name.Contains("bsnl")) return "BSNL";
            return name;
        }

        private async Task<IActionResult> ProcessCardRecharge(RechargeTransaction tx)
        {
            _logger.LogInformation(
                "[STATUS_CHANGE] Processing card-based recharge for TransactionId: {TransactionId}, Operator: BSNL, Amount: {Amount}",
                tx.TransactionId,
                tx.Amount
            );

            var card = await _context.RechargeCards
                .Where(c => c.OperatorId == tx.OperatorId && c.Denomination == tx.Amount && c.Status == "AVAILABLE")
                .OrderBy(c => c.ImportedDate)
                .FirstOrDefaultAsync();

            if (card == null)
            {
                _logger.LogWarning(
                    "[STATUS_CHANGE] TransactionId: {TransactionId} status changing to FAILED. Reason: Out of card stock for denomination ₹{Amount}",
                    tx.TransactionId,
                    tx.Amount
                );

                await _context.UpdateRechargeStatusAsync(
                    tx.TransactionId,
                    "FAILED",
                    errorMessage: "No recharge pins/cards available for this denomination.",
                    remarks: "Auto-failed during card lookup: Out of stock."
                );

                return Ok(new RechargeResponse
                {
                    TransactionId = tx.TransactionId,
                    MobileNumber = tx.MobileNumber,
                    OperatorName = "BSNL",
                    Amount = tx.Amount,
                    Status = "FAILED",
                    ErrorMessage = "No recharge pins/cards available for this denomination."
                });
            }

            var (success, procMessage) = await _context.UseRechargeCardAsync(card.CardNumber, tx.TransactionId);

            if (success)
            {
                _logger.LogInformation(
                    "[STATUS_CHANGE] TransactionId: {TransactionId} status changing to SUCCESS. Card Serial: {SerialNumber} allocated.",
                    tx.TransactionId,
                    card.SerialNumber
                );

                await _context.UpdateRechargeStatusAsync(
                    tx.TransactionId,
                    "SUCCESS",
                    remarks: $"Recharge completed successfully using card serial {card.SerialNumber}."
                );

                return Ok(new RechargeResponse
                {
                    TransactionId = tx.TransactionId,
                    MobileNumber = tx.MobileNumber,
                    OperatorName = "BSNL",
                    Amount = tx.Amount,
                    Status = "SUCCESS",
                    CardSerialNumber = card.SerialNumber,
                    CardPin = card.CardNumber
                });
            }
            else
            {
                _logger.LogWarning(
                    "[STATUS_CHANGE] TransactionId: {TransactionId} status changing to FAILED. Reason: {ProcMessage}",
                    tx.TransactionId,
                    procMessage
                );

                await _context.UpdateRechargeStatusAsync(
                    tx.TransactionId,
                    "FAILED",
                    errorMessage: $"Failed to claim recharge card: {procMessage}",
                    remarks: "Failed during stored procedure UseRechargeCard."
                );

                return Ok(new RechargeResponse
                {
                    TransactionId = tx.TransactionId,
                    MobileNumber = tx.MobileNumber,
                    OperatorName = "BSNL",
                    Amount = tx.Amount,
                    Status = "FAILED",
                    ErrorMessage = $"Failed to claim recharge card: {procMessage}"
                });
            }
        }

        private async Task<IActionResult> ProcessApiRecharge(RechargeTransaction tx, string operatorName)
        {
            _logger.LogInformation(
                "[STATUS_CHANGE] TransactionId: {TransactionId} status changing: NEW -> PROCESSING",
                tx.TransactionId
            );

            await _context.UpdateRechargeStatusAsync(tx.TransactionId, "PROCESSING", remarks: "Routing request to telecom provider API.");

            string mockBaseUrl = _configuration["MockTelecomApi:BaseUrl"] ?? "http://localhost:5081";
            string requestUrl = $"{mockBaseUrl.TrimEnd('/')}/api/provider/recharge";

            var providerRequest = new
            {
                ReferenceId = tx.TransactionId,
                MobileNumber = tx.MobileNumber,
                Operator = operatorName,
                Amount = tx.Amount
            };

            string requestBodyJson = JsonSerializer.Serialize(providerRequest);

            _logger.LogInformation(
                "[PROVIDER_REQUEST] Dispatching request to telecom provider. Url: {Url}, TransactionId: {TransactionId}, Mobile: {MobileNumber}, Operator: {Operator}, Amount: {Amount}, Body: {RequestBody}",
                requestUrl,
                tx.TransactionId,
                tx.MobileNumber,
                operatorName,
                tx.Amount,
                requestBodyJson
            );

            var auditRequest = new ProviderRequest
            {
                TransactionId = tx.TransactionId,
                RequestUrl = requestUrl,
                RequestBody = requestBodyJson,
                CreatedDate = DateTime.UtcNow
            };
            _context.ProviderRequests.Add(auditRequest);
            await _context.SaveChangesAsync();

            var httpClient = _httpClientFactory.CreateClient("ProviderApi");
            var stopwatch = Stopwatch.StartNew();
            HttpResponseMessage? response = null;
            string? responseBody = null;
            int statusCode = 500;

            try
            {
                response = await httpClient.PostAsJsonAsync(requestUrl, providerRequest);
                statusCode = (int)response.StatusCode;
                responseBody = await response.Content.ReadAsStringAsync();
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    ex,
                    "[TIMEOUT] Provider HTTP request TIMED OUT for TransactionId: {TransactionId} after {ElapsedMs}ms. Marking transaction as PENDING for background reconciliation.",
                    tx.TransactionId,
                    stopwatch.ElapsedMilliseconds
                );

                _logger.LogInformation(
                    "[STATUS_CHANGE] TransactionId: {TransactionId} status changing: PROCESSING -> PENDING (Reason: Provider timeout)",
                    tx.TransactionId
                );

                await _context.UpdateRechargeStatusAsync(
                    tx.TransactionId,
                    "PENDING",
                    errorMessage: $"HttpClient timeout after {stopwatch.ElapsedMilliseconds}ms",
                    remarks: "Provider API call timed out. Placed in PENDING for background reconciliation. The provider may have processed the recharge successfully."
                );

                _context.ProviderResponses.Add(new ProviderResponse
                {
                    TransactionId = tx.TransactionId,
                    StatusCode = 0,
                    ResponseBody = $"TIMEOUT: HttpClient.Timeout exceeded after {stopwatch.ElapsedMilliseconds}ms. Exception: {ex.Message}",
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return Ok(new RechargeResponse
                {
                    TransactionId = tx.TransactionId,
                    MobileNumber = tx.MobileNumber,
                    OperatorName = operatorName,
                    Amount = tx.Amount,
                    Status = "PENDING",
                    ErrorMessage = "Provider connection timed out. Your recharge is being verified and will be updated shortly."
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "[EXCEPTION] Network/Connection failure while calling provider for TransactionId: {TransactionId} after {ElapsedMs}ms. Error: {Message}",
                    tx.TransactionId,
                    stopwatch.ElapsedMilliseconds,
                    ex.Message
                );

                _logger.LogInformation(
                    "[STATUS_CHANGE] TransactionId: {TransactionId} status changing: PROCESSING -> PENDING (Reason: Connection exception)",
                    tx.TransactionId
                );

                await _context.UpdateRechargeStatusAsync(
                    tx.TransactionId,
                    "PENDING",
                    errorMessage: $"Connection failed: {ex.Message}",
                    remarks: "API call failed (non-timeout). Placed in PENDING state for background reconciliation."
                );

                _context.ProviderResponses.Add(new ProviderResponse
                {
                    TransactionId = tx.TransactionId,
                    StatusCode = 0,
                    ResponseBody = $"Exception: {ex.Message}\nStackTrace: {ex.StackTrace}",
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                return Ok(new RechargeResponse
                {
                    TransactionId = tx.TransactionId,
                    MobileNumber = tx.MobileNumber,
                    OperatorName = operatorName,
                    Amount = tx.Amount,
                    Status = "PENDING",
                    ErrorMessage = $"Provider connection failed, reconciliation will occur shortly. Error: {ex.Message}"
                });
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "[PROVIDER_RESPONSE] Received HTTP {StatusCode} from provider for TransactionId: {TransactionId} in {ElapsedMs}ms. Body: {ResponseBody}",
                statusCode,
                tx.TransactionId,
                stopwatch.ElapsedMilliseconds,
                responseBody
            );

            _context.ProviderResponses.Add(new ProviderResponse
            {
                TransactionId = tx.TransactionId,
                StatusCode = statusCode,
                ResponseBody = responseBody,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                CreatedDate = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            if (statusCode == 500)
            {
                _logger.LogWarning(
                    "[PROVIDER_HTTP_500] Provider returned HTTP 500 for TransactionId: {TransactionId}. Marking as FAILED with reconciliation fallback.",
                    tx.TransactionId
                );

                string providerErrMsg = "Provider internal server error (HTTP 500). Please retry later.";
                try
                {
                    var errorPayload = JsonSerializer.Deserialize<ProviderApiResponse>(
                        responseBody ?? string.Empty,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );
                    if (!string.IsNullOrWhiteSpace(errorPayload?.ErrorMessage))
                        providerErrMsg = errorPayload.ErrorMessage;
                }
                catch { }

                await _context.UpdateRechargeStatusAsync(
                    tx.TransactionId,
                    "FAILED",
                    errorMessage: providerErrMsg,
                    remarks: $"Provider returned HTTP 500: {providerErrMsg}"
                );

                return Ok(new RechargeResponse
                {
                    TransactionId = tx.TransactionId,
                    MobileNumber = tx.MobileNumber,
                    OperatorName = operatorName,
                    Amount = tx.Amount,
                    Status = "FAILED",
                    ErrorMessage = providerErrMsg
                });
            }

            try
            {
                var apiResponse = JsonSerializer.Deserialize<ProviderApiResponse>(
                    responseBody ?? string.Empty,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (apiResponse == null)
                    throw new Exception("Unable to deserialize provider response payload.");

                if (apiResponse.Status == "SUCCESS")
                {
                    _logger.LogInformation(
                        "[STATUS_CHANGE] TransactionId: {TransactionId} status changing: PROCESSING -> SUCCESS. ProviderReference: {ProviderReference}",
                        tx.TransactionId,
                        apiResponse.ProviderReference
                    );

                    await _context.UpdateRechargeStatusAsync(
                        tx.TransactionId,
                        "SUCCESS",
                        providerReference: apiResponse.ProviderReference,
                        remarks: "Recharge confirmed SUCCESS by provider API."
                    );

                    return Ok(new RechargeResponse
                    {
                        TransactionId = tx.TransactionId,
                        MobileNumber = tx.MobileNumber,
                        OperatorName = operatorName,
                        Amount = tx.Amount,
                        Status = "SUCCESS",
                        ProviderReference = apiResponse.ProviderReference
                    });
                }
                else if (apiResponse.Status == "PENDING")
                {
                    _logger.LogInformation(
                        "[STATUS_CHANGE] TransactionId: {TransactionId} status changing: PROCESSING -> PENDING. ProviderReference: {ProviderReference}",
                        tx.TransactionId,
                        apiResponse.ProviderReference
                    );

                    await _context.UpdateRechargeStatusAsync(
                        tx.TransactionId,
                        "PENDING",
                        providerReference: apiResponse.ProviderReference,
                        remarks: "Recharge placed in PENDING state by provider API."
                    );

                    return Ok(new RechargeResponse
                    {
                        TransactionId = tx.TransactionId,
                        MobileNumber = tx.MobileNumber,
                        OperatorName = operatorName,
                        Amount = tx.Amount,
                        Status = "PENDING",
                        ProviderReference = apiResponse.ProviderReference
                    });
                }
                else
                {
                    _logger.LogWarning(
                        "[STATUS_CHANGE] TransactionId: {TransactionId} status changing: PROCESSING -> FAILED. ProviderReference: {ProviderReference}, Error: {ErrorMessage}",
                        tx.TransactionId,
                        apiResponse.ProviderReference,
                        apiResponse.ErrorMessage ?? "Recharge rejected by provider."
                    );

                    await _context.UpdateRechargeStatusAsync(
                        tx.TransactionId,
                        "FAILED",
                        providerReference: apiResponse.ProviderReference,
                        errorMessage: apiResponse.ErrorMessage ?? "Recharge rejected by provider.",
                        remarks: $"Recharge marked FAILED by provider API. Error: {apiResponse.ErrorMessage}"
                    );

                    return Ok(new RechargeResponse
                    {
                        TransactionId = tx.TransactionId,
                        MobileNumber = tx.MobileNumber,
                        OperatorName = operatorName,
                        Amount = tx.Amount,
                        Status = "FAILED",
                        ProviderReference = apiResponse.ProviderReference,
                        ErrorMessage = apiResponse.ErrorMessage ?? "Recharge rejected by provider."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[EXCEPTION] Failed to parse provider response payload for TransactionId: {TransactionId}. Error: {Message}. Placing in PENDING for safety.",
                    tx.TransactionId,
                    ex.Message
                );

                _logger.LogInformation(
                    "[STATUS_CHANGE] TransactionId: {TransactionId} status changing: PROCESSING -> PENDING (Reason: Parsing failure)",
                    tx.TransactionId
                );

                // Provider might have processed the request; place in PENDING for reconciliation
                await _context.UpdateRechargeStatusAsync(
                    tx.TransactionId,
                    "PENDING",
                    errorMessage: $"Response processing error: {ex.Message}",
                    remarks: $"Failed to parse provider response: {ex.Message}. Placed in PENDING state for reconciliation safety."
                );

                return Ok(new RechargeResponse
                {
                    TransactionId = tx.TransactionId,
                    MobileNumber = tx.MobileNumber,
                    OperatorName = operatorName,
                    Amount = tx.Amount,
                    Status = "PENDING",
                    ErrorMessage = "Provider responded but verification failed. Reconciliation service will resolve this transaction shortly."
                });
            }
        }
    }
}
