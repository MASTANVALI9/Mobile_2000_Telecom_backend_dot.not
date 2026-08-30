using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using MainRechargeApi.Models;

namespace MainRechargeApi.Data
{
    public class RechargeDbContext : DbContext
    {
        public RechargeDbContext(DbContextOptions<RechargeDbContext> options) : base(options)
        {
        }

        public DbSet<TelecomOperator> TelecomOperators { get; set; }
        public DbSet<RechargeTransaction> RechargeTransactions { get; set; }
        public DbSet<ProviderRequest> ProviderRequests { get; set; }
        public DbSet<ProviderResponse> ProviderResponses { get; set; }
        public DbSet<TransactionStatusHistory> TransactionStatusHistories { get; set; }
        public DbSet<RechargeCard> RechargeCards { get; set; }
        public DbSet<CardImportBatch> CardImportBatches { get; set; }
        public DbSet<CardImportError> CardImportErrors { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TelecomOperator>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
            });

            modelBuilder.Entity<RechargeTransaction>(entity =>
            {
                entity.HasIndex(e => e.TransactionId).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedDate);
                entity.HasIndex(e => e.MobileNumber);
            });

            modelBuilder.Entity<ProviderRequest>(entity =>
            {
                entity.HasIndex(e => e.TransactionId);
            });

            modelBuilder.Entity<ProviderResponse>(entity =>
            {
                entity.HasIndex(e => e.TransactionId);
            });

            modelBuilder.Entity<TransactionStatusHistory>(entity =>
            {
                entity.HasIndex(e => e.TransactionId);
            });

            modelBuilder.Entity<RechargeCard>(entity =>
            {
                entity.HasIndex(e => e.CardNumber).IsUnique();
                entity.HasIndex(e => e.SerialNumber).IsUnique();
                entity.HasIndex(e => new { e.OperatorId, e.Denomination, e.Status }).HasDatabaseName("IX_RechargeCards_Search");
            });
        }

        public async Task<RechargeTransaction?> CreateRechargeTransactionAsync(string transactionId, string mobileNumber, string operatorName, decimal amount)
        {
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.VarChar, 50) { Value = transactionId };
            var mobileNumberParam = new SqlParameter("@MobileNumber", SqlDbType.VarChar, 10) { Value = mobileNumber };
            var operatorNameParam = new SqlParameter("@OperatorName", SqlDbType.VarChar, 50) { Value = operatorName };
            var amountParam = new SqlParameter("@Amount", SqlDbType.Decimal) { Value = amount, Precision = 18, Scale = 2 };

            var results = await this.RechargeTransactions
                .FromSqlRaw("EXEC CreateRechargeTransaction @TransactionId, @MobileNumber, @OperatorName, @Amount",
                    transactionIdParam, mobileNumberParam, operatorNameParam, amountParam)
                .ToListAsync();

            return results.FirstOrDefault();
        }

        public async Task UpdateRechargeStatusAsync(string transactionId, string newStatus, string? providerReference = null, string? errorMessage = null, string? remarks = null)
        {
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.VarChar, 50) { Value = transactionId };
            var newStatusParam = new SqlParameter("@NewStatus", SqlDbType.VarChar, 20) { Value = newStatus };
            var providerReferenceParam = new SqlParameter("@ProviderReference", SqlDbType.VarChar, 100) { Value = (object?)providerReference ?? DBNull.Value };
            var errorMessageParam = new SqlParameter("@ErrorMessage", SqlDbType.VarChar, 500) { Value = (object?)errorMessage ?? DBNull.Value };
            var remarksParam = new SqlParameter("@Remarks", SqlDbType.VarChar, 500) { Value = (object?)remarks ?? DBNull.Value };

            await this.Database.ExecuteSqlRawAsync("EXEC UpdateRechargeStatus @TransactionId, @NewStatus, @ProviderReference, @ErrorMessage, @Remarks",
                transactionIdParam, newStatusParam, providerReferenceParam, errorMessageParam, remarksParam);
        }

        public async Task<RechargeTransaction?> GetTransactionByTransactionIdAsync(string transactionId)
        {
            var transactionIdParam = new SqlParameter("@TransactionId", SqlDbType.VarChar, 50) { Value = transactionId };

            var results = await this.RechargeTransactions
                .FromSqlRaw("EXEC GetTransactionByTransactionId @TransactionId", transactionIdParam)
                .ToListAsync();

            return results.FirstOrDefault();
        }

        public async Task<RechargeTransaction?> GetTransactionByProviderReferenceAsync(string providerReference)
        {
            var providerReferenceParam = new SqlParameter("@ProviderReference", SqlDbType.VarChar, 100) { Value = providerReference };

            var results = await this.RechargeTransactions
                .FromSqlRaw("EXEC GetTransactionByProviderReference @ProviderReference", providerReferenceParam)
                .ToListAsync();

            return results.FirstOrDefault();
        }

        // Stored procedure uses UPDLOCK to prevent double card allocation
        public async Task<(bool success, string message)> UseRechargeCardAsync(string cardNumber, string usedTransactionId)
        {
            var cardNumberParam = new SqlParameter("@CardNumber", SqlDbType.VarChar, 50) { Value = cardNumber };
            var usedTransactionIdParam = new SqlParameter("@UsedTransactionId", SqlDbType.VarChar, 50) { Value = usedTransactionId };

            var successParam = new SqlParameter("@Success", SqlDbType.Bit)
            {
                Direction = ParameterDirection.Output
            };

            var messageParam = new SqlParameter("@Message", SqlDbType.VarChar, 250)
            {
                Direction = ParameterDirection.Output
            };

            await this.Database.ExecuteSqlRawAsync("EXEC UseRechargeCard @CardNumber, @UsedTransactionId, @Success OUTPUT, @Message OUTPUT",
                cardNumberParam, usedTransactionIdParam, successParam, messageParam);

            bool success = successParam.Value != DBNull.Value && (bool)successParam.Value;
            string message = messageParam.Value != DBNull.Value ? (string)messageParam.Value : string.Empty;

            return (success, message);
        }
    }
}
