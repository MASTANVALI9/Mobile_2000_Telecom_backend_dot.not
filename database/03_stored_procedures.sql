-- ============================================================================
-- Mobile2000 Telecom Recharge Platform — Stored Procedures
-- Target: Microsoft SQL Server 2019+
-- ============================================================================
-- Prerequisite: 01_schema.sql and 02_seed.sql must be executed first.
-- All procedures use CREATE OR ALTER for idempotent deployment.
-- ============================================================================


-- ============================================================================
-- SP 1: CreateRechargeTransaction
-- Initialises a new recharge and its first status-history row atomically.
-- Returns the created transaction joined with operator name.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.CreateRechargeTransaction
    @TransactionId   VARCHAR(50),
    @MobileNumber    VARCHAR(15),
    @OperatorName    VARCHAR(50),
    @Amount          DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Resolve operator
    DECLARE @OperatorId INT;
    SELECT @OperatorId = Id
    FROM   TelecomOperators
    WHERE  Name = @OperatorName AND IsActive = 1;

    IF @OperatorId IS NULL
    BEGIN
        RAISERROR('Operator ''%s'' not found or inactive.', 16, 1, @OperatorName);
        RETURN;
    END

    BEGIN TRANSACTION;

        INSERT INTO RechargeTransactions
            (TransactionId, MobileNumber, OperatorId, Amount, Status, CreatedDate, UpdatedDate)
        VALUES
            (@TransactionId, @MobileNumber, @OperatorId, @Amount, 'NEW', GETDATE(), GETDATE());

        INSERT INTO TransactionStatusHistory
            (TransactionId, PreviousStatus, NewStatus, ChangedDate, ChangedBy, Remarks)
        VALUES
            (@TransactionId, NULL, 'NEW', GETDATE(), 'SYSTEM', 'Transaction initialized.');

    COMMIT TRANSACTION;

    -- Return the created record
    SELECT t.Id, t.TransactionId, t.MobileNumber, t.OperatorId, t.Amount,
           t.Status, t.ProviderReference, t.ErrorMessage, t.CreatedDate, t.UpdatedDate,
           o.Name AS OperatorName
    FROM   RechargeTransactions t
    INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
    WHERE  t.TransactionId = @TransactionId;
END;
GO


-- ============================================================================
-- SP 2: UpdateRechargeStatus
-- Concurrency-safe status transition using pessimistic locking (UPDLOCK).
-- Writes both the transaction update and the history row in one atomic unit.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.UpdateRechargeStatus
    @TransactionId      VARCHAR(50),
    @NewStatus          VARCHAR(20),
    @ProviderReference  VARCHAR(100)  = NULL,
    @ErrorMessage       VARCHAR(500)  = NULL,
    @Remarks            VARCHAR(500)  = NULL,
    @ChangedBy          VARCHAR(100)  = 'SYSTEM'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY

        DECLARE @PrevStatus VARCHAR(20);

        -- UPDLOCK + ROWLOCK: prevents concurrent callers from racing on the same row
        SELECT @PrevStatus = Status
        FROM   RechargeTransactions WITH (UPDLOCK, ROWLOCK)
        WHERE  TransactionId = @TransactionId;

        IF @PrevStatus IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            RAISERROR('Transaction ''%s'' not found.', 16, 1, @TransactionId);
            RETURN;
        END

        -- Guard: no-op if already in the requested state
        IF @PrevStatus = @NewStatus AND @ProviderReference IS NULL AND @ErrorMessage IS NULL
        BEGIN
            COMMIT TRANSACTION;
            RETURN;
        END

        UPDATE RechargeTransactions
        SET    Status            = @NewStatus,
               ProviderReference = COALESCE(@ProviderReference, ProviderReference),
               ErrorMessage      = COALESCE(@ErrorMessage, ErrorMessage),
               UpdatedDate       = GETDATE()
        WHERE  TransactionId     = @TransactionId;

        INSERT INTO TransactionStatusHistory
            (TransactionId, PreviousStatus, NewStatus, ChangedDate, ChangedBy, Remarks)
        VALUES
            (@TransactionId, @PrevStatus, @NewStatus, GETDATE(), @ChangedBy, @Remarks);

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO


-- ============================================================================
-- SP 3: GetTransactionByTransactionId
-- Retrieves a single transaction by its unique TransactionId.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetTransactionByTransactionId
    @TransactionId VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT t.Id, t.TransactionId, t.MobileNumber, t.OperatorId, t.Amount,
           t.Status, t.ProviderReference, t.ErrorMessage, t.CreatedDate, t.UpdatedDate,
           o.Name AS OperatorName
    FROM   RechargeTransactions t
    INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
    WHERE  t.TransactionId = @TransactionId;
END;
GO


-- ============================================================================
-- SP 4: GetTransactionByProviderReference
-- Retrieves a single transaction by the provider's reference number.
-- Used during reconciliation / status-check callbacks.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetTransactionByProviderReference
    @ProviderReference VARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT t.Id, t.TransactionId, t.MobileNumber, t.OperatorId, t.Amount,
           t.Status, t.ProviderReference, t.ErrorMessage, t.CreatedDate, t.UpdatedDate,
           o.Name AS OperatorName
    FROM   RechargeTransactions t
    INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
    WHERE  t.ProviderReference = @ProviderReference;
END;
GO


-- ============================================================================
-- SP 5: GetTransactionsByStatus
-- Returns all transactions with a given status, ordered by oldest first.
-- Useful for dashboards and reconciliation batch jobs.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetTransactionsByStatus
    @Status VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT t.Id, t.TransactionId, t.MobileNumber, t.OperatorId, t.Amount,
           t.Status, t.ProviderReference, t.ErrorMessage, t.CreatedDate, t.UpdatedDate,
           o.Name AS OperatorName
    FROM   RechargeTransactions t
    INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
    WHERE  t.Status = @Status
    ORDER BY t.CreatedDate ASC;
END;
GO


-- ============================================================================
-- SP 6: GetTransactionsByDateRange
-- Parameterised date-range query with optional status filter.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetTransactionsByDateRange
    @StartDate  DATETIME2,
    @EndDate    DATETIME2,
    @Status     VARCHAR(20) = NULL      -- NULL = all statuses
AS
BEGIN
    SET NOCOUNT ON;

    SELECT t.Id, t.TransactionId, t.MobileNumber, t.OperatorId, t.Amount,
           t.Status, t.ProviderReference, t.ErrorMessage, t.CreatedDate, t.UpdatedDate,
           o.Name AS OperatorName
    FROM   RechargeTransactions t
    INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
    WHERE  t.CreatedDate >= @StartDate
      AND  t.CreatedDate <= @EndDate
      AND  (@Status IS NULL OR t.Status = @Status)
    ORDER BY t.CreatedDate DESC;
END;
GO


-- ============================================================================
-- SP 7: GetRechargeAmountByOperator
-- Summary of total successful recharge amount and count per operator.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetRechargeAmountByOperator
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.Id   AS OperatorId,
           o.Name AS OperatorName,
           COUNT(t.Id)                AS RechargeCount,
           COALESCE(SUM(t.Amount), 0) AS TotalAmount
    FROM   TelecomOperators o
    LEFT JOIN RechargeTransactions t
        ON o.Id = t.OperatorId AND t.Status = 'SUCCESS'
    GROUP BY o.Id, o.Name
    ORDER BY TotalAmount DESC;
END;
GO


-- ============================================================================
-- SP 8: GetDuplicateMobileRecharges
-- Detects mobiles that received multiple successful recharges for the same
-- operator and amount on the same calendar day (potential duplicates).
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetDuplicateMobileRecharges
    @LookbackDays INT = 1                -- default: today only
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SinceDate DATETIME2 = DATEADD(DAY, -@LookbackDays, CAST(GETDATE() AS DATE));

    SELECT t.MobileNumber,
           o.Name       AS OperatorName,
           t.Amount,
           CAST(t.CreatedDate AS DATE) AS RechargeDate,
           COUNT(*)      AS TimesRecharged,
           STRING_AGG(t.TransactionId, ', ') AS TransactionIds
    FROM   RechargeTransactions t
    INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
    WHERE  t.Status = 'SUCCESS'
      AND  t.CreatedDate >= @SinceDate
    GROUP BY t.MobileNumber, o.Name, t.Amount, CAST(t.CreatedDate AS DATE)
    HAVING COUNT(*) > 1
    ORDER BY TimesRecharged DESC;
END;
GO


-- ============================================================================
-- SP 9: UseRechargeCard
-- Pessimistic locking (UPDLOCK + ROWLOCK) prevents two concurrent requests
-- from claiming the same card.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.UseRechargeCard
    @CardNumber         VARCHAR(50),
    @UsedTransactionId  VARCHAR(50),
    @Success            BIT           OUTPUT,
    @Message            VARCHAR(250)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @Success = 0;
    SET @Message = 'Card not found or not available.';

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @CurrentStatus VARCHAR(20);

        SELECT @CurrentStatus = Status
        FROM   RechargeCards WITH (UPDLOCK, ROWLOCK)
        WHERE  CardNumber = @CardNumber;

        IF @CurrentStatus IS NULL
        BEGIN
            ROLLBACK TRANSACTION;
            SET @Message = 'Card does not exist.';
            RETURN;
        END

        IF @CurrentStatus = 'AVAILABLE'
        BEGIN
            UPDATE RechargeCards
            SET    Status            = 'USED',
                   UsedTransactionId = @UsedTransactionId,
                   UsedDate          = GETDATE()
            WHERE  CardNumber        = @CardNumber;

            SET @Success = 1;
            SET @Message = 'Card marked as USED successfully.';
        END
        ELSE
        BEGIN
            SET @Message = 'Card is not available (current status: ' + @CurrentStatus + ').';
        END

        COMMIT TRANSACTION;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        SET @Success = 0;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END;
GO


-- ============================================================================
-- SP 10: GetTransactionStatusHistory
-- Returns the complete status lifecycle for a given transaction.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetTransactionStatusHistory
    @TransactionId VARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, TransactionId, PreviousStatus, NewStatus,
           ChangedDate, ChangedBy, Remarks
    FROM   TransactionStatusHistory
    WHERE  TransactionId = @TransactionId
    ORDER BY ChangedDate ASC;
END;
GO


-- ============================================================================
-- SP 11: GetCardImportBatchDetails
-- Returns batch header metadata and associated row-level errors for auditing.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetCardImportBatchDetails
    @BatchId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    -- Result set 1: Batch Header
    SELECT Id AS BatchId,
           FileName,
           TotalRows,
           SuccessfulRows,
           FailedRows,
           ImportedBy,
           ImportedDate,
           Status
    FROM   CardImportBatches
    WHERE  Id = @BatchId;

    -- Result set 2: Row Validation Errors
    SELECT RowNumber,
           RawRowData,
           ErrorMessage,
           CreatedDate
    FROM   CardImportErrors
    WHERE  BatchId = @BatchId
    ORDER BY RowNumber ASC;
END;
GO


-- ============================================================================
-- SP 12: GetCardInventorySummary
-- Aggregates voucher inventory counts grouped by operator, denomination, and status.
-- ============================================================================
CREATE OR ALTER PROCEDURE dbo.GetCardInventorySummary
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.Name       AS OperatorName,
           c.Denomination,
           c.Status,
           COUNT(*)     AS TotalCount
    FROM   RechargeCards c
    INNER JOIN TelecomOperators o ON c.OperatorId = o.Id
    GROUP BY o.Name, c.Denomination, c.Status
    ORDER BY o.Name, c.Denomination, c.Status;
END;
GO


PRINT 'All stored procedures created/updated successfully.';
GO

