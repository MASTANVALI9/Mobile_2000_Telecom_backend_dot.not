// Background service that periodically polls the provider to resolve pending recharge transactions
using Microsoft.EntityFrameworkCore;
using MainRechargeApi.Data;
using MainRechargeApi.DTOs;
using System.Text.Json;

namespace MainRechargeApi.Services
{
    public class ReconciliationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ReconciliationWorker> _logger;
        private readonly IConfiguration _configuration;

        public ReconciliationWorker(
            IServiceProvider serviceProvider,
            IHttpClientFactory httpClientFactory,
            ILogger<ReconciliationWorker> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Reconciliation Worker started.");

            int pollIntervalSeconds = 30;
            if (int.TryParse(_configuration["Reconciliation:IntervalSeconds"], out int customInterval))
                pollIntervalSeconds = customInterval;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcilePendingTransactionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred during pending transactions reconciliation.");
                }

                await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("Reconciliation Worker is stopping.");
        }

        private async Task ReconcilePendingTransactionsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<RechargeDbContext>();
            var httpClient = _httpClientFactory.CreateClient("ProviderApi");

            var pendingTxList = await dbContext.RechargeTransactions
                .Where(t => t.Status == "PENDING")
                .OrderBy(t => t.CreatedDate)
                .ToListAsync(stoppingToken);

            if (pendingTxList.Count == 0)
                return;

            _logger.LogInformation("[RECONCILIATION] Found {Count} pending transactions to reconcile.", pendingTxList.Count);

            string mockBaseUrl = _configuration["MockTelecomApi:BaseUrl"] ?? "http://localhost:5081";

            foreach (var tx in pendingTxList)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                string statusCheckUrl = $"{mockBaseUrl.TrimEnd('/')}/api/provider/recharge/status/{tx.TransactionId}";

                // ── Event 3: Provider Request (Status Query) ──
                _logger.LogInformation(
                    "[PROVIDER_REQUEST] Reconciliation polling status for TransactionId: {TransactionId} at {Url}",
                    tx.TransactionId,
                    statusCheckUrl
                );

                try
                {
                    var response = await httpClient.GetAsync(statusCheckUrl, stoppingToken);

                    // ── Event 4: Provider Response ──
                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
                        _logger.LogInformation(
                            "[PROVIDER_RESPONSE] Reconciliation received status response for TransactionId: {TransactionId}. StatusCode: {StatusCode}, Body: {ResponseBody}",
                            tx.TransactionId,
                            (int)response.StatusCode,
                            responseBody
                        );

                        var apiResponse = JsonSerializer.Deserialize<ProviderApiResponse>(
                            responseBody,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        );

                        if (apiResponse != null)
                        {
                            if (apiResponse.Status == "SUCCESS")
                            {
                                // ── Event 7: Status Change (PENDING -> SUCCESS) ──
                                _logger.LogInformation(
                                    "[STATUS_CHANGE] TransactionId: {TransactionId} reconciled: PENDING -> SUCCESS. ProviderReference: {ProviderReference}",
                                    tx.TransactionId,
                                    apiResponse.ProviderReference
                                );

                                await dbContext.UpdateRechargeStatusAsync(
                                    tx.TransactionId,
                                    "SUCCESS",
                                    providerReference: apiResponse.ProviderReference,
                                    remarks: "Reconciled status to SUCCESS via background worker."
                                );
                            }
                            else if (apiResponse.Status == "FAILED")
                            {
                                // ── Event 7: Status Change (PENDING -> FAILED) ──
                                _logger.LogInformation(
                                    "[STATUS_CHANGE] TransactionId: {TransactionId} reconciled: PENDING -> FAILED. ProviderReference: {ProviderReference}, Error: {ErrorMessage}",
                                    tx.TransactionId,
                                    apiResponse.ProviderReference,
                                    apiResponse.ErrorMessage ?? "Provider rejected recharge."
                                );

                                await dbContext.UpdateRechargeStatusAsync(
                                    tx.TransactionId,
                                    "FAILED",
                                    providerReference: apiResponse.ProviderReference,
                                    errorMessage: apiResponse.ErrorMessage ?? "Provider rejected recharge.",
                                    remarks: $"Reconciled status to FAILED via background worker. Error: {apiResponse.ErrorMessage}"
                                );
                            }
                            else
                            {
                                _logger.LogInformation("[STATUS_CHANGE] TransactionId: {TransactionId} is still in PENDING state at provider.", tx.TransactionId);
                            }
                        }
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // ── Event 7: Status Change (PENDING -> FAILED - 404) ──
                        _logger.LogWarning(
                            "[STATUS_CHANGE] TransactionId: {TransactionId} not found on provider system (HTTP 404). Reconciling to FAILED.",
                            tx.TransactionId
                        );

                        await dbContext.UpdateRechargeStatusAsync(
                            tx.TransactionId,
                            "FAILED",
                            errorMessage: "Transaction not found on provider system.",
                            remarks: "Reconciled status to FAILED because the provider returned 404 (Not Found)."
                        );
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[PROVIDER_RESPONSE] Reconciliation status query for TransactionId: {TransactionId} returned unexpected HTTP {StatusCode}",
                            tx.TransactionId,
                            (int)response.StatusCode
                        );
                    }
                }
                catch (TaskCanceledException tex) when (!stoppingToken.IsCancellationRequested)
                {
                    // ── Event 5: Timeout ──
                    _logger.LogWarning(
                        tex,
                        "[TIMEOUT] Reconciliation status enquiry timed out for TransactionId: {TransactionId}.",
                        tx.TransactionId
                    );
                }
                catch (Exception ex)
                {
                    // ── Event 6: Exception ──
                    _logger.LogError(
                        ex,
                        "[EXCEPTION] Error reconciling transaction {TransactionId}: {Message}",
                        tx.TransactionId,
                        ex.Message
                    );
                }
            }
        }
    }
}
