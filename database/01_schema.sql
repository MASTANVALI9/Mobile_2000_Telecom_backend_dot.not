-- ============================================================================
-- Mobile2000 Telecom Recharge Platform — Database Schema
-- Target: Microsoft SQL Server 2019+
-- Database: TelecomRechargePlatform
-- ============================================================================
-- Run order: 01_schema.sql → 02_seed.sql → 03_stored_procedures.sql
-- ============================================================================

-- ============================================================================
-- 1. TelecomOperators — master list of supported mobile operators
-- ============================================================================
IF OBJECT_ID('dbo.TelecomOperators', 'U') IS NULL
BEGIN
    CREATE TABLE TelecomOperators (
        Id              INT IDENTITY(1,1)   NOT NULL,
        Name            VARCHAR(50)         NOT NULL,
        Code            VARCHAR(10)         NULL,           -- short code: JIO, AIRTL, VI, BSNL
        IsActive        BIT                 NOT NULL DEFAULT 1,
        CreatedDate     DATETIME2           NOT NULL DEFAULT GETDATE(),

        CONSTRAINT PK_TelecomOperators          PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_TelecomOperators_Name     UNIQUE (Name)
    );
END
GO

-- ============================================================================
-- 2. RechargeTransactions — core transaction table
-- ============================================================================
IF OBJECT_ID('dbo.RechargeTransactions', 'U') IS NULL
BEGIN
    CREATE TABLE RechargeTransactions (
        Id                  BIGINT IDENTITY(1,1)    NOT NULL,
        TransactionId       VARCHAR(50)             NOT NULL,
        MobileNumber        VARCHAR(15)             NOT NULL,
        OperatorId          INT                     NOT NULL,
        Amount              DECIMAL(18,2)           NOT NULL,
        Status              VARCHAR(20)             NOT NULL DEFAULT 'NEW',
        ProviderReference   VARCHAR(100)            NULL,
        ErrorMessage        VARCHAR(500)            NULL,
        CreatedDate         DATETIME2               NOT NULL DEFAULT GETDATE(),
        UpdatedDate         DATETIME2               NOT NULL DEFAULT GETDATE(),

        CONSTRAINT PK_RechargeTransactions              PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RechargeTransactions_TxnId        UNIQUE (TransactionId),
        CONSTRAINT FK_RechargeTransactions_Operator      FOREIGN KEY (OperatorId)
                                                            REFERENCES TelecomOperators(Id),
        CONSTRAINT CHK_RechargeTransactions_Status       CHECK (Status IN ('NEW','PROCESSING','SUCCESS','FAILED','PENDING')),
        CONSTRAINT CHK_RechargeTransactions_Amount       CHECK (Amount > 0)
    );
END
GO

-- Non-clustered indexes for common query patterns
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RechargeTransactions_Status' AND object_id = OBJECT_ID('dbo.RechargeTransactions'))
    CREATE NONCLUSTERED INDEX IX_RechargeTransactions_Status       ON RechargeTransactions (Status)        INCLUDE (TransactionId, MobileNumber, Amount, CreatedDate);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RechargeTransactions_CreatedDate' AND object_id = OBJECT_ID('dbo.RechargeTransactions'))
    CREATE NONCLUSTERED INDEX IX_RechargeTransactions_CreatedDate  ON RechargeTransactions (CreatedDate)   INCLUDE (Status, MobileNumber, Amount);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RechargeTransactions_MobileNumber' AND object_id = OBJECT_ID('dbo.RechargeTransactions'))
    CREATE NONCLUSTERED INDEX IX_RechargeTransactions_MobileNumber ON RechargeTransactions (MobileNumber)  INCLUDE (Status, Amount, CreatedDate);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RechargeTransactions_ProviderRef' AND object_id = OBJECT_ID('dbo.RechargeTransactions'))
    CREATE NONCLUSTERED INDEX IX_RechargeTransactions_ProviderRef  ON RechargeTransactions (ProviderReference) WHERE ProviderReference IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RechargeTransactions_OperatorId' AND object_id = OBJECT_ID('dbo.RechargeTransactions'))
    CREATE NONCLUSTERED INDEX IX_RechargeTransactions_OperatorId   ON RechargeTransactions (OperatorId)    INCLUDE (Status, Amount);

-- Composite index for duplicate-recharge detection (same mobile + operator within a day)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RechargeTransactions_DuplicateCheck' AND object_id = OBJECT_ID('dbo.RechargeTransactions'))
    CREATE NONCLUSTERED INDEX IX_RechargeTransactions_DuplicateCheck ON RechargeTransactions (MobileNumber, OperatorId, Amount, CreatedDate);
GO

-- ============================================================================
-- 3. ProviderRequests — audit log of outgoing API calls
-- ============================================================================
IF OBJECT_ID('dbo.ProviderRequests', 'U') IS NULL
BEGIN
    CREATE TABLE ProviderRequests (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        TransactionId   VARCHAR(50)             NOT NULL,
        RequestUrl      VARCHAR(500)            NOT NULL,
        RequestMethod   VARCHAR(10)             NOT NULL DEFAULT 'POST',
        RequestBody     NVARCHAR(MAX)           NOT NULL,
        RequestHeaders  NVARCHAR(MAX)           NULL,
        CreatedDate     DATETIME2               NOT NULL DEFAULT GETDATE(),

        CONSTRAINT PK_ProviderRequests                  PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ProviderRequests_Transaction      FOREIGN KEY (TransactionId)
                                                            REFERENCES RechargeTransactions(TransactionId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProviderRequests_TransactionId' AND object_id = OBJECT_ID('dbo.ProviderRequests'))
    CREATE NONCLUSTERED INDEX IX_ProviderRequests_TransactionId ON ProviderRequests (TransactionId) INCLUDE (RequestUrl, CreatedDate);
GO

-- ============================================================================
-- 4. ProviderResponses — audit log of incoming API responses
-- ============================================================================
IF OBJECT_ID('dbo.ProviderResponses', 'U') IS NULL
BEGIN
    CREATE TABLE ProviderResponses (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        TransactionId   VARCHAR(50)             NOT NULL,
        StatusCode      INT                     NOT NULL,
        ResponseBody    NVARCHAR(MAX)           NULL,
        ResponseTimeMs  INT                     NOT NULL,
        CreatedDate     DATETIME2               NOT NULL DEFAULT GETDATE(),

        CONSTRAINT PK_ProviderResponses                 PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ProviderResponses_Transaction     FOREIGN KEY (TransactionId)
                                                            REFERENCES RechargeTransactions(TransactionId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProviderResponses_TransactionId' AND object_id = OBJECT_ID('dbo.ProviderResponses'))
    CREATE NONCLUSTERED INDEX IX_ProviderResponses_TransactionId ON ProviderResponses (TransactionId) INCLUDE (StatusCode, ResponseTimeMs, CreatedDate);
GO

-- ============================================================================
-- 5. TransactionStatusHistory — full status lifecycle audit trail
-- ============================================================================
IF OBJECT_ID('dbo.TransactionStatusHistory', 'U') IS NULL
BEGIN
    CREATE TABLE TransactionStatusHistory (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        TransactionId   VARCHAR(50)             NOT NULL,
        PreviousStatus  VARCHAR(20)             NULL,
        NewStatus       VARCHAR(20)             NOT NULL,
        ChangedDate     DATETIME2               NOT NULL DEFAULT GETDATE(),
        ChangedBy       VARCHAR(100)            NULL DEFAULT 'SYSTEM',
        Remarks         VARCHAR(500)            NULL,

        CONSTRAINT PK_TransactionStatusHistory              PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_TransactionStatusHistory_Transaction  FOREIGN KEY (TransactionId)
                                                                REFERENCES RechargeTransactions(TransactionId)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TransactionStatusHistory_TransactionId' AND object_id = OBJECT_ID('dbo.TransactionStatusHistory'))
    CREATE NONCLUSTERED INDEX IX_TransactionStatusHistory_TransactionId ON TransactionStatusHistory (TransactionId) INCLUDE (PreviousStatus, NewStatus, ChangedDate);
GO

-- ============================================================================
-- 6. CardImportBatches — tracks bulk card import jobs
-- ============================================================================
IF OBJECT_ID('dbo.CardImportBatches', 'U') IS NULL
BEGIN
    CREATE TABLE CardImportBatches (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        FileName        VARCHAR(250)            NOT NULL,
        TotalRows       INT                     NOT NULL,
        SuccessfulRows  INT                     NOT NULL DEFAULT 0,
        FailedRows      INT                     NOT NULL DEFAULT 0,
        ImportedBy      VARCHAR(100)            NOT NULL,
        ImportedDate    DATETIME2               NOT NULL DEFAULT GETDATE(),
        Status          VARCHAR(20)             NOT NULL DEFAULT 'PROCESSING',

        CONSTRAINT PK_CardImportBatches             PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CHK_CardImportBatches_Status     CHECK (Status IN ('PROCESSING','COMPLETED','FAILED','PARTIAL_SUCCESS'))
    );
END
GO

-- ============================================================================
-- 7. RechargeCards — pre-loaded card/PIN inventory for card-based operators
-- ============================================================================
IF OBJECT_ID('dbo.RechargeCards', 'U') IS NULL
BEGIN
    CREATE TABLE RechargeCards (
        Id                  BIGINT IDENTITY(1,1)    NOT NULL,
        CardNumber          VARCHAR(50)             NOT NULL,
        SerialNumber        VARCHAR(50)             NOT NULL,
        OperatorId          INT                     NOT NULL,
        Denomination        DECIMAL(18,2)           NOT NULL,
        Status              VARCHAR(20)             NOT NULL DEFAULT 'AVAILABLE',
        ExpiryDate          DATE                    NOT NULL,
        ImportedDate        DATETIME2               NOT NULL DEFAULT GETDATE(),
        BatchId             BIGINT                  NOT NULL,
        UsedTransactionId   VARCHAR(50)             NULL,
        UsedDate            DATETIME2               NULL,

        CONSTRAINT PK_RechargeCards                 PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RechargeCards_CardNumber       UNIQUE (CardNumber),
        CONSTRAINT UQ_RechargeCards_SerialNumber     UNIQUE (SerialNumber),
        CONSTRAINT FK_RechargeCards_Operator         FOREIGN KEY (OperatorId)    REFERENCES TelecomOperators(Id),
        CONSTRAINT FK_RechargeCards_Batch            FOREIGN KEY (BatchId)       REFERENCES CardImportBatches(Id),
        CONSTRAINT CHK_RechargeCards_Status          CHECK (Status IN ('AVAILABLE','RESERVED','USED','EXPIRED','BLOCKED')),
        CONSTRAINT CHK_RechargeCards_Denomination    CHECK (Denomination > 0)
    );
END
GO

-- Composite index for fast card lookup: "give me an available card for this operator + denomination"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RechargeCards_Search' AND object_id = OBJECT_ID('dbo.RechargeCards'))
    CREATE NONCLUSTERED INDEX IX_RechargeCards_Search ON RechargeCards (OperatorId, Denomination, Status) INCLUDE (CardNumber, SerialNumber, ExpiryDate);
GO

-- ============================================================================
-- 8. CardImportErrors — row-level errors from bulk card imports
-- ============================================================================
IF OBJECT_ID('dbo.CardImportErrors', 'U') IS NULL
BEGIN
    CREATE TABLE CardImportErrors (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        BatchId         BIGINT                  NOT NULL,
        RowNumber       INT                     NOT NULL,
        RawRowData      NVARCHAR(1000)          NULL,
        ErrorMessage    VARCHAR(500)            NOT NULL,
        CreatedDate     DATETIME2               NOT NULL DEFAULT GETDATE(),

        CONSTRAINT PK_CardImportErrors              PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_CardImportErrors_Batch        FOREIGN KEY (BatchId) REFERENCES CardImportBatches(Id)
    );
END
GO

-- ============================================================================
-- 9. MockProviderTransactions — state table for the Mock Provider API
-- ============================================================================
IF OBJECT_ID('dbo.MockProviderTransactions', 'U') IS NULL
BEGIN
    CREATE TABLE MockProviderTransactions (
        Id                  BIGINT IDENTITY(1,1)    NOT NULL,
        ReferenceId         VARCHAR(50)             NOT NULL,
        MobileNumber        VARCHAR(15)             NOT NULL,
        Operator            VARCHAR(50)             NOT NULL,
        Amount              DECIMAL(18,2)           NOT NULL,
        Status              VARCHAR(20)             NOT NULL,
        ProviderReference   VARCHAR(100)            NOT NULL,
        CreatedDate         DATETIME2               NOT NULL DEFAULT GETDATE(),

        CONSTRAINT PK_MockProviderTransactions              PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_MockProviderTransactions_RefId        UNIQUE (ReferenceId),
        CONSTRAINT CHK_MockProviderTransactions_Status      CHECK (Status IN ('SUCCESS','FAILED'))
    );
END
GO

-- ============================================================================
-- 10. AuditLogs — general-purpose application audit trail (optional)
-- ============================================================================
IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE AuditLogs (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        EventType       VARCHAR(50)             NOT NULL,     -- RECHARGE_INITIATED, STATUS_CHANGED, CARD_USED, etc.
        EntityType      VARCHAR(50)             NOT NULL,     -- RechargeTransaction, RechargeCard, etc.
        EntityId        VARCHAR(50)             NOT NULL,     -- TransactionId or CardNumber
        Details         NVARCHAR(MAX)           NULL,
        IpAddress       VARCHAR(45)             NULL,
        CreatedDate     DATETIME2               NOT NULL DEFAULT GETDATE(),
        CreatedBy       VARCHAR(100)            NULL DEFAULT 'SYSTEM',

        CONSTRAINT PK_AuditLogs                 PRIMARY KEY CLUSTERED (Id)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_EntityType_EntityId' AND object_id = OBJECT_ID('dbo.AuditLogs'))
    CREATE NONCLUSTERED INDEX IX_AuditLogs_EntityType_EntityId ON AuditLogs (EntityType, EntityId) INCLUDE (EventType, CreatedDate);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AuditLogs_CreatedDate' AND object_id = OBJECT_ID('dbo.AuditLogs'))
    CREATE NONCLUSTERED INDEX IX_AuditLogs_CreatedDate ON AuditLogs (CreatedDate);
GO

PRINT 'Schema creation complete — all tables, constraints, and indexes applied.';
GO
