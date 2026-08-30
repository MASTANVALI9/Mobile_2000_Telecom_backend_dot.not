using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockTelecomApi.Data;
using MockTelecomApi.Models;

namespace MockTelecomApi.Controllers
{
    [ApiController]
    [Route("api/provider/recharge")]
    public class ProviderRechargeController : ControllerBase
    {
        private readonly MockDbContext _context;
        private readonly ILogger<ProviderRechargeController> _logger;

        public ProviderRechargeController(MockDbContext context, ILogger<ProviderRechargeController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/provider/recharge/rules
        /// Returns the documented simulation rules so testers can reference them from Postman/Swagger.
        /// </summary>
        [HttpGet("rules")]
        public IActionResult GetSimulationRules()
        {
            var rules = new[]
            {
                new { Amount = "100",      Behavior = "SUCCESS — Instant success response" },
                new { Amount = "200",      Behavior = "FAILED — Provider rejects the recharge" },
                new { Amount = "299",      Behavior = "TIMEOUT — ₹299 Airtel scenario: provider processes SUCCESS but connection aborted before response reaches caller" },
                new { Amount = "300",      Behavior = "SUCCESS — 15-second delay before responding (simulates slow provider)" },
                new { Amount = "400",      Behavior = "TIMEOUT — Connection aborted, no response sent (simulates network timeout)" },
                new { Amount = "500",      Behavior = "ERROR — HTTP 500 Internal Server Error" },
                new { Amount = "600",      Behavior = "PENDING — Async processing, resolves to SUCCESS after 30 seconds" },
                new { Amount = "700",      Behavior = "PENDING — Async processing, resolves to FAILED after 30 seconds" },
                new { Amount = "13",       Behavior = "FAILED — Always fails (legacy test amount)" },
                new { Amount = "*.99",     Behavior = "PENDING — Any amount ending in .99 triggers async processing" },
                new { Amount = "Mobile *9", Behavior = "PENDING — Mobile number ending in 9 triggers async processing" },
                new { Amount = "Other",    Behavior = "SUCCESS (90%) / FAILED (10%) — Random outcome" }
            };

            return Ok(new { SimulationRules = rules, Note = "Use specific amounts to trigger deterministic test scenarios." });
        }

        [HttpPost]
        public async Task<IActionResult> Recharge([FromBody] ProviderRechargeRequest request)
        {
            _logger.LogInformation(
                "[PROVIDER_RECEIVED] Incoming recharge request from backend. ReferenceId: {ReferenceId}, Mobile: {MobileNumber}, Operator: {Operator}, Amount: {Amount}",
                request?.ReferenceId,
                request?.MobileNumber,
                request?.Operator,
                request?.Amount
            );

            if (request == null || string.IsNullOrWhiteSpace(request.ReferenceId) ||
                string.IsNullOrWhiteSpace(request.MobileNumber) || string.IsNullOrWhiteSpace(request.Operator))
            {
                _logger.LogWarning("[VALIDATION_ERROR] Mock provider rejected malformed request payload.");
                return BadRequest(new ProviderRechargeResponse
                {
                    Status = "FAILED",
                    ErrorMessage = "Invalid request parameters."
                });
            }

            var existing = await _context.MockProviderTransactions
                .FirstOrDefaultAsync(t => t.ReferenceId == request.ReferenceId);
            if (existing != null)
            {
                _logger.LogWarning("[DUPLICATE_REQUEST] Duplicate ReferenceId '{ReferenceId}' received by provider.", request.ReferenceId);
                return Conflict(new ProviderRechargeResponse
                {
                    Status = existing.Status,
                    ProviderReference = existing.ProviderReference,
                    ErrorMessage = "Duplicate reference ID."
                });
            }

            // Amount 500: Simulate HTTP 500 server error
            if (request.Amount == 500)
            {
                _logger.LogError("[EXCEPTION] Simulating internal server error (HTTP 500) for ReferenceId: {ReferenceId}", request.ReferenceId);
                return StatusCode(500, new ProviderRechargeResponse
                {
                    Status = "ERROR",
                    ErrorMessage = "Internal server error. The provider system is temporarily unavailable."
                });
            }

            // Amount 299: Simulates processed success where connection dropped before responding
            if (request.Amount == 299)
            {
                string timeoutRef299 = $"MOCK-REF-{Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper()}";
                _context.MockProviderTransactions.Add(new MockProviderTransaction
                {
                    ReferenceId = request.ReferenceId,
                    MobileNumber = request.MobileNumber,
                    Operator = request.Operator,
                    Amount = request.Amount,
                    Status = "SUCCESS",
                    ProviderReference = timeoutRef299,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogWarning(
                    "[TIMEOUT] Simulating ₹299 Airtel timeout scenario: Saved SUCCESS in DB (Ref: {ProviderRef}) and aborting connection for ReferenceId: {ReferenceId}",
                    timeoutRef299,
                    request.ReferenceId
                );

                HttpContext.Abort();
                return StatusCode(500);
            }

            // Amount 400: Simulate connection timeout (abort response)
            if (request.Amount == 400)
            {
                string timeoutRef = $"MOCK-REF-{Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper()}";
                _context.MockProviderTransactions.Add(new MockProviderTransaction
                {
                    ReferenceId = request.ReferenceId,
                    MobileNumber = request.MobileNumber,
                    Operator = request.Operator,
                    Amount = request.Amount,
                    Status = "SUCCESS",
                    ProviderReference = timeoutRef,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogWarning(
                    "[TIMEOUT] Simulating connection timeout: Saved SUCCESS in DB (Ref: {ProviderRef}) and aborting connection for ReferenceId: {ReferenceId}",
                    timeoutRef,
                    request.ReferenceId
                );

                HttpContext.Abort();
                return StatusCode(500);
            }

            string providerRef = $"MOCK-REF-{Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper()}";

            // ── Amount 300: Simulate slow provider (15-second delay, then SUCCESS) ──
            if (request.Amount == 300)
            {
                _logger.LogInformation("[SLOW_PROVIDER] Simulating 15-second provider processing latency for ReferenceId: {ReferenceId}", request.ReferenceId);
                await Task.Delay(TimeSpan.FromSeconds(15));

                _context.MockProviderTransactions.Add(new MockProviderTransaction
                {
                    ReferenceId = request.ReferenceId,
                    MobileNumber = request.MobileNumber,
                    Operator = request.Operator,
                    Amount = request.Amount,
                    Status = "SUCCESS",
                    ProviderReference = providerRef,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "[PROVIDER_RESPONSE] Responding SUCCESS (delayed) for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                    request.ReferenceId,
                    providerRef
                );

                return Ok(new ProviderRechargeResponse
                {
                    Status = "SUCCESS",
                    ProviderReference = providerRef,
                    Message = "Recharge successful (delayed response)."
                });
            }

            // ── Amount 100: Deterministic SUCCESS ──
            if (request.Amount == 100)
            {
                _context.MockProviderTransactions.Add(new MockProviderTransaction
                {
                    ReferenceId = request.ReferenceId,
                    MobileNumber = request.MobileNumber,
                    Operator = request.Operator,
                    Amount = request.Amount,
                    Status = "SUCCESS",
                    ProviderReference = providerRef,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "[PROVIDER_RESPONSE] Responding SUCCESS for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                    request.ReferenceId,
                    providerRef
                );

                return Ok(new ProviderRechargeResponse
                {
                    Status = "SUCCESS",
                    ProviderReference = providerRef,
                    Message = "Recharge successful."
                });
            }

            // ── Amount 200: Deterministic FAILED ──
            if (request.Amount == 200)
            {
                _context.MockProviderTransactions.Add(new MockProviderTransaction
                {
                    ReferenceId = request.ReferenceId,
                    MobileNumber = request.MobileNumber,
                    Operator = request.Operator,
                    Amount = request.Amount,
                    Status = "FAILED",
                    ProviderReference = providerRef,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogWarning(
                    "[PROVIDER_RESPONSE] Responding FAILED for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                    request.ReferenceId,
                    providerRef
                );

                return Ok(new ProviderRechargeResponse
                {
                    Status = "FAILED",
                    ProviderReference = providerRef,
                    ErrorMessage = "Provider rejected the recharge request. Insufficient balance or invalid plan."
                });
            }

            // ── Amount 600: PENDING → resolves to SUCCESS after 30s ──
            if (request.Amount == 600)
            {
                _context.MockProviderTransactions.Add(new MockProviderTransaction
                {
                    ReferenceId = request.ReferenceId,
                    MobileNumber = request.MobileNumber,
                    Operator = request.Operator,
                    Amount = request.Amount,
                    Status = "SUCCESS", // Real status stored; PENDING returned to caller
                    ProviderReference = providerRef,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "[PROVIDER_RESPONSE] Responding PENDING (async resolving to SUCCESS) for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                    request.ReferenceId,
                    providerRef
                );

                return Accepted(new ProviderRechargeResponse
                {
                    Status = "PENDING",
                    ProviderReference = providerRef,
                    Message = "Transaction is processing asynchronously. Check status after 30 seconds."
                });
            }

            // ── Amount 700: PENDING → resolves to FAILED after 30s ──
            if (request.Amount == 700)
            {
                _context.MockProviderTransactions.Add(new MockProviderTransaction
                {
                    ReferenceId = request.ReferenceId,
                    MobileNumber = request.MobileNumber,
                    Operator = request.Operator,
                    Amount = request.Amount,
                    Status = "FAILED", // Real status stored; PENDING returned to caller
                    ProviderReference = providerRef,
                    CreatedDate = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "[PROVIDER_RESPONSE] Responding PENDING (async resolving to FAILED) for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                    request.ReferenceId,
                    providerRef
                );

                return Accepted(new ProviderRechargeResponse
                {
                    Status = "PENDING",
                    ProviderReference = providerRef,
                    Message = "Transaction is processing asynchronously. Check status after 30 seconds."
                });
            }

            // ── Fallback: existing simulation logic ──
            bool isPendingCriteria = request.MobileNumber.EndsWith("9") || (request.Amount % 1 == 0.99m);

            string dbStatus = "SUCCESS";

            if (!isPendingCriteria)
            {
                if (request.Amount == 13.00m || Random.Shared.Next(0, 100) < 10)
                    dbStatus = "FAILED";
            }

            var transaction = new MockProviderTransaction
            {
                ReferenceId = request.ReferenceId,
                MobileNumber = request.MobileNumber,
                Operator = request.Operator,
                Amount = request.Amount,
                Status = dbStatus,
                ProviderReference = providerRef,
                CreatedDate = DateTime.UtcNow
            };

            _context.MockProviderTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            if (isPendingCriteria)
            {
                _logger.LogInformation(
                    "[PROVIDER_RESPONSE] Responding PENDING for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                    request.ReferenceId,
                    providerRef
                );

                return Accepted(new ProviderRechargeResponse
                {
                    Status = "PENDING",
                    ProviderReference = providerRef,
                    Message = "Transaction is processing asynchronously."
                });
            }

            if (dbStatus == "FAILED")
            {
                _logger.LogWarning(
                    "[PROVIDER_RESPONSE] Responding FAILED for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                    request.ReferenceId,
                    providerRef
                );

                return Ok(new ProviderRechargeResponse
                {
                    Status = "FAILED",
                    ProviderReference = providerRef,
                    ErrorMessage = "Provider rejected the recharge request."
                });
            }

            _logger.LogInformation(
                "[PROVIDER_RESPONSE] Responding SUCCESS for ReferenceId: {ReferenceId}, ProviderReference: {ProviderReference}",
                request.ReferenceId,
                providerRef
            );

            return Ok(new ProviderRechargeResponse
            {
                Status = "SUCCESS",
                ProviderReference = providerRef,
                Message = "Recharge successful."
            });
        }

        [HttpGet("status/{referenceId}")]
        public async Task<IActionResult> GetStatus(string referenceId)
        {
            _logger.LogInformation(
                "[STATUS_ENQUIRY] Provider received status check for ReferenceId: {ReferenceId}",
                referenceId
            );

            var transaction = await _context.MockProviderTransactions
                .FirstOrDefaultAsync(t => t.ReferenceId == referenceId);

            if (transaction == null)
            {
                _logger.LogWarning(
                    "[STATUS_ENQUIRY] ReferenceId: {ReferenceId} not found in provider records.",
                    referenceId
                );
                return NotFound(new { Message = "Transaction not found" });
            }

            // Amounts 600, 700 and PENDING-criteria transactions resolve after 30 seconds
            bool isPendingScenario = transaction.Amount == 600 || transaction.Amount == 700 ||
                                     transaction.MobileNumber.EndsWith("9") || (transaction.Amount % 1 == 0.99m);

            if (isPendingScenario)
            {
                var timeElapsed = DateTime.UtcNow - transaction.CreatedDate;
                if (timeElapsed.TotalSeconds < 30)
                {
                    _logger.LogInformation(
                        "[STATUS_ENQUIRY] ReferenceId: {ReferenceId} is still within async window ({Elapsed:F1}s < 30s). Returning PENDING.",
                        referenceId,
                        timeElapsed.TotalSeconds
                    );

                    return Ok(new ProviderRechargeResponse
                    {
                        Status = "PENDING",
                        ProviderReference = transaction.ProviderReference,
                        Message = "Transaction is still being processed."
                    });
                }
            }

            _logger.LogInformation(
                "[STATUS_ENQUIRY] ReferenceId: {ReferenceId} resolved. Returning Status: {Status}, ProviderReference: {ProviderReference}",
                referenceId,
                transaction.Status,
                transaction.ProviderReference
            );

            // After 30s (or for non-PENDING transactions), return the real stored status
            return Ok(new ProviderRechargeResponse
            {
                Status = transaction.Status,
                ProviderReference = transaction.ProviderReference,
                Message = transaction.Status == "SUCCESS" ? "Recharge successful." : string.Empty,
                ErrorMessage = transaction.Status == "FAILED" ? "Provider rejected the recharge request." : string.Empty
            });
        }
    }

    public class ProviderRechargeRequest
    {
        public string ReferenceId { get; set; } = string.Empty;
        public string MobileNumber { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    public class ProviderRechargeResponse
    {
        public string Status { get; set; } = string.Empty;
        public string ProviderReference { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
