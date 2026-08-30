-- ============================================================================
-- Mobile2000 Telecom Recharge Platform — Reporting & Analytical Queries
-- Target: Microsoft SQL Server 2019+
-- ============================================================================
-- These are standalone queries for ad-hoc analysis, dashboards, and support.
-- Copy-paste into SSMS or any SQL client connected to TelecomRechargePlatform.
-- ============================================================================


-- ============================================================================
-- Q1: All SUCCESSFUL transactions today
-- ============================================================================
SELECT t.TransactionId,
       t.MobileNumber,
       o.Name       AS OperatorName,
       t.Amount,
       t.ProviderReference,
       t.CreatedDate
FROM   RechargeTransactions t
INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
WHERE  t.Status = 'SUCCESS'
  AND  CAST(t.CreatedDate AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY t.CreatedDate DESC;


-- ============================================================================
-- Q2: All FAILED transactions today
-- ============================================================================
SELECT t.TransactionId,
       t.MobileNumber,
       o.Name       AS OperatorName,
       t.Amount,
       t.ErrorMessage,
       t.CreatedDate
FROM   RechargeTransactions t
INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
WHERE  t.Status = 'FAILED'
  AND  CAST(t.CreatedDate AS DATE) = CAST(GETDATE() AS DATE)
ORDER BY t.CreatedDate DESC;


-- ============================================================================
-- Q3: All PENDING transactions (oldest first for reconciliation priority)
-- ============================================================================
SELECT t.TransactionId,
       t.MobileNumber,
       o.Name       AS OperatorName,
       t.Amount,
       t.ProviderReference,
       t.ErrorMessage,
       t.CreatedDate,
       t.UpdatedDate,
       DATEDIFF(MINUTE, t.UpdatedDate, GETDATE()) AS MinutesSinceLastUpdate
FROM   RechargeTransactions t
INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
WHERE  t.Status = 'PENDING'
ORDER BY t.CreatedDate ASC;


-- ============================================================================
-- Q4: Total recharge amount by operator (successful transactions only)
-- ============================================================================
SELECT o.Name                          AS OperatorName,
       COUNT(t.Id)                     AS RechargeCount,
       COALESCE(SUM(t.Amount), 0)      AS TotalAmount,
       COALESCE(AVG(t.Amount), 0)      AS AverageAmount,
       COALESCE(MIN(t.Amount), 0)      AS MinAmount,
       COALESCE(MAX(t.Amount), 0)      AS MaxAmount
FROM   TelecomOperators o
LEFT JOIN RechargeTransactions t
    ON o.Id = t.OperatorId AND t.Status = 'SUCCESS'
GROUP BY o.Name
ORDER BY TotalAmount DESC;


-- ============================================================================
-- Q5: Duplicate mobile recharges
--     Same mobile + same operator + same amount on the same day
-- ============================================================================
SELECT t.MobileNumber,
       o.Name                          AS OperatorName,
       t.Amount,
       CAST(t.CreatedDate AS DATE)     AS RechargeDate,
       COUNT(*)                        AS TimesRecharged,
       STRING_AGG(t.TransactionId, ', ') AS TransactionIds
FROM   RechargeTransactions t
INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
WHERE  t.Status = 'SUCCESS'
GROUP BY t.MobileNumber, o.Name, t.Amount, CAST(t.CreatedDate AS DATE)
HAVING COUNT(*) > 1
ORDER BY TimesRecharged DESC;


-- ============================================================================
-- Q6: Top 10 mobile numbers by total recharge amount
-- ============================================================================
SELECT TOP 10
       t.MobileNumber,
       COUNT(t.Id)       AS SuccessCount,
       SUM(t.Amount)     AS TotalAmount,
       MIN(t.CreatedDate) AS FirstRecharge,
       MAX(t.CreatedDate) AS LastRecharge
FROM   RechargeTransactions t
WHERE  t.Status = 'SUCCESS'
GROUP BY t.MobileNumber
ORDER BY TotalAmount DESC;


-- ============================================================================
-- Q7: Transactions between two dates
--     ⚠ Update @StartDate / @EndDate before executing.
-- ============================================================================
DECLARE @StartDate DATETIME2 = '2026-08-01 00:00:00';
DECLARE @EndDate   DATETIME2 = '2026-08-31 23:59:59';

SELECT t.TransactionId,
       t.MobileNumber,
       o.Name       AS OperatorName,
       t.Amount,
       t.Status,
       t.ProviderReference,
       t.ErrorMessage,
       t.CreatedDate,
       t.UpdatedDate
FROM   RechargeTransactions t
INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
WHERE  t.CreatedDate >= @StartDate
  AND  t.CreatedDate <= @EndDate
ORDER BY t.CreatedDate DESC;


-- ============================================================================
-- Q8: Daily transaction summary (last 30 days)
-- ============================================================================
SELECT CAST(t.CreatedDate AS DATE) AS TransactionDate,
       COUNT(*)                     AS TotalTransactions,
       SUM(CASE WHEN t.Status = 'SUCCESS' THEN 1 ELSE 0 END)    AS SuccessCount,
       SUM(CASE WHEN t.Status = 'FAILED'  THEN 1 ELSE 0 END)    AS FailedCount,
       SUM(CASE WHEN t.Status = 'PENDING' THEN 1 ELSE 0 END)    AS PendingCount,
       SUM(CASE WHEN t.Status = 'SUCCESS' THEN t.Amount ELSE 0 END) AS SuccessAmount,
       SUM(t.Amount)                AS TotalAmount
FROM   RechargeTransactions t
WHERE  t.CreatedDate >= DATEADD(DAY, -30, CAST(GETDATE() AS DATE))
GROUP BY CAST(t.CreatedDate AS DATE)
ORDER BY TransactionDate DESC;


-- ============================================================================
-- Q9: Transaction success rate by operator
-- ============================================================================
SELECT o.Name AS OperatorName,
       COUNT(*)                                                              AS TotalTransactions,
       SUM(CASE WHEN t.Status = 'SUCCESS' THEN 1 ELSE 0 END)               AS SuccessCount,
       SUM(CASE WHEN t.Status = 'FAILED'  THEN 1 ELSE 0 END)               AS FailedCount,
       CAST(
           SUM(CASE WHEN t.Status = 'SUCCESS' THEN 1.0 ELSE 0 END)
           / NULLIF(COUNT(*), 0) * 100
       AS DECIMAL(5,2))                                                      AS SuccessRatePct
FROM   RechargeTransactions t
INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
GROUP BY o.Name
ORDER BY SuccessRatePct DESC;


-- ============================================================================
-- Q10: Average provider response time by operator
-- ============================================================================
SELECT o.Name  AS OperatorName,
       COUNT(pr.Id)                     AS TotalResponses,
       AVG(pr.ResponseTimeMs)           AS AvgResponseMs,
       MIN(pr.ResponseTimeMs)           AS MinResponseMs,
       MAX(pr.ResponseTimeMs)           AS MaxResponseMs
FROM   ProviderResponses pr
INNER JOIN RechargeTransactions t ON pr.TransactionId = t.TransactionId
INNER JOIN TelecomOperators o     ON t.OperatorId = o.Id
GROUP BY o.Name
ORDER BY AvgResponseMs DESC;


-- ============================================================================
-- Q11: Stale PENDING transactions (no update in over 30 minutes)
-- ============================================================================
SELECT t.TransactionId,
       t.MobileNumber,
       o.Name       AS OperatorName,
       t.Amount,
       t.CreatedDate,
       t.UpdatedDate,
       DATEDIFF(MINUTE, t.UpdatedDate, GETDATE()) AS MinutesSinceUpdate
FROM   RechargeTransactions t
INNER JOIN TelecomOperators o ON t.OperatorId = o.Id
WHERE  t.Status = 'PENDING'
  AND  DATEDIFF(MINUTE, t.UpdatedDate, GETDATE()) > 30
ORDER BY t.UpdatedDate ASC;


-- ============================================================================
-- Q12: Hourly transaction volume (today)
-- ============================================================================
SELECT DATEPART(HOUR, t.CreatedDate) AS HourOfDay,
       COUNT(*)                       AS TransactionCount,
       SUM(CASE WHEN t.Status = 'SUCCESS' THEN t.Amount ELSE 0 END) AS SuccessAmount
FROM   RechargeTransactions t
WHERE  CAST(t.CreatedDate AS DATE) = CAST(GETDATE() AS DATE)
GROUP BY DATEPART(HOUR, t.CreatedDate)
ORDER BY HourOfDay;


-- ============================================================================
-- Q13: Card import batches and error rate
-- ============================================================================
SELECT b.Id                                                          AS BatchId,
       b.FileName,
       b.TotalRows,
       b.SuccessfulRows,
       b.FailedRows,
       CAST(
           CASE WHEN b.TotalRows > 0 
                THEN (CAST(b.SuccessfulRows AS FLOAT) / b.TotalRows) * 100 
                ELSE 0 
           END AS DECIMAL(5,2))                                      AS SuccessRatePct,
       b.Status,
       b.ImportedBy,
       b.ImportedDate
FROM   CardImportBatches b
ORDER BY b.ImportedDate DESC;


-- ============================================================================
-- Q14: Available voucher inventory by operator & denomination (Summary)
-- ============================================================================
SELECT o.Name                                                         AS OperatorName,
       c.Denomination,
       SUM(CASE WHEN c.Status = 'AVAILABLE' THEN 1 ELSE 0 END)       AS AvailableCards,
       SUM(CASE WHEN c.Status = 'USED'      THEN 1 ELSE 0 END)       AS UsedCards,
       SUM(CASE WHEN c.Status = 'EXPIRED'   THEN 1 ELSE 0 END)       AS ExpiredCards,
       COUNT(*)                                                       AS TotalCards
FROM   RechargeCards c
INNER JOIN TelecomOperators o ON c.OperatorId = o.Id
GROUP BY o.Name, c.Denomination
ORDER BY o.Name, c.Denomination;


-- ============================================================================
-- Q15: Available cards by operator
-- ============================================================================
SELECT o.Name               AS OperatorName,
       c.CardNumber,
       c.SerialNumber,
       c.Denomination,
       c.ExpiryDate,
       c.ImportedDate
FROM   RechargeCards c
INNER JOIN TelecomOperators o ON c.OperatorId = o.Id
WHERE  c.Status = 'AVAILABLE'
ORDER BY o.Name, c.Denomination, c.ExpiryDate;


-- ============================================================================
-- Q16: Available cards by denomination
-- ============================================================================
SELECT c.Denomination,
       o.Name               AS OperatorName,
       c.CardNumber,
       c.SerialNumber,
       c.ExpiryDate,
       c.ImportedDate
FROM   RechargeCards c
INNER JOIN TelecomOperators o ON c.OperatorId = o.Id
WHERE  c.Status = 'AVAILABLE'
ORDER BY c.Denomination, o.Name;


-- ============================================================================
-- Q17: Used cards
-- ============================================================================
SELECT c.CardNumber,
       c.SerialNumber,
       o.Name               AS OperatorName,
       c.Denomination,
       c.UsedTransactionId,
       c.UsedDate,
       c.ExpiryDate
FROM   RechargeCards c
INNER JOIN TelecomOperators o ON c.OperatorId = o.Id
WHERE  c.Status = 'USED'
ORDER BY c.UsedDate DESC;


-- ============================================================================
-- Q18: Expired cards
-- ============================================================================
SELECT c.CardNumber,
       c.SerialNumber,
       o.Name               AS OperatorName,
       c.Denomination,
       c.Status,
       c.ExpiryDate,
       DATEDIFF(DAY, c.ExpiryDate, GETDATE()) AS DaysExpired
FROM   RechargeCards c
INNER JOIN TelecomOperators o ON c.OperatorId = o.Id
WHERE  c.Status = 'EXPIRED' 
   OR  (c.Status = 'AVAILABLE' AND c.ExpiryDate < CAST(GETDATE() AS DATE))
ORDER BY c.ExpiryDate ASC;


-- ============================================================================
-- Q19: Cards imported in a specific batch
--      ⚠ Replace @TargetBatchId with the desired batch ID.
-- ============================================================================
DECLARE @TargetBatchId BIGINT = 1;

SELECT c.Id                 AS CardId,
       c.CardNumber,
       c.SerialNumber,
       o.Name               AS OperatorName,
       c.Denomination,
       c.Status,
       c.ExpiryDate,
       c.ImportedDate,
       b.FileName,
       b.ImportedBy
FROM   RechargeCards c
INNER JOIN TelecomOperators o   ON c.OperatorId = o.Id
INNER JOIN CardImportBatches b  ON c.BatchId = b.Id
WHERE  c.BatchId = @TargetBatchId
ORDER BY c.Id ASC;


-- ============================================================================
-- Q20: Duplicate card numbers (Auditing / Error check)
-- ============================================================================
SELECT c.CardNumber,
       COUNT(*)                           AS OccurrenceCount,
       STRING_AGG(CAST(c.Id AS VARCHAR), ', ') AS CardIds,
       STRING_AGG(CAST(c.BatchId AS VARCHAR), ', ') AS BatchIds
FROM   RechargeCards c
GROUP BY c.CardNumber
HAVING COUNT(*) > 1;


-- ============================================================================
-- Q21: Number of available cards per operator and denomination (Aggregated)
-- ============================================================================
SELECT o.Name               AS OperatorName,
       c.Denomination,
       COUNT(*)             AS AvailableCardCount,
       SUM(c.Denomination)  AS TotalAvailableValue
FROM   RechargeCards c
INNER JOIN TelecomOperators o ON c.OperatorId = o.Id
WHERE  c.Status = 'AVAILABLE'
  AND  c.ExpiryDate >= CAST(GETDATE() AS DATE)
GROUP BY o.Name, c.Denomination
ORDER BY o.Name, c.Denomination;


-- ============================================================================
-- Q22: Cards used between two dates
--      ⚠ Replace @StartDate / @EndDate before executing.
-- ============================================================================
DECLARE @CardUsedStartDate DATETIME2 = '2026-08-01 00:00:00';
DECLARE @CardUsedEndDate   DATETIME2 = '2026-08-31 23:59:59';

SELECT c.CardNumber,
       c.SerialNumber,
       o.Name               AS OperatorName,
       c.Denomination,
       c.UsedTransactionId,
       c.UsedDate,
       c.ExpiryDate,
       t.MobileNumber,
       t.Status             AS TransactionStatus
FROM   RechargeCards c
INNER JOIN TelecomOperators o     ON c.OperatorId = o.Id
LEFT JOIN RechargeTransactions t  ON c.UsedTransactionId = t.TransactionId
WHERE  c.Status = 'USED'
  AND  c.UsedDate >= @CardUsedStartDate
  AND  c.UsedDate <= @CardUsedEndDate
ORDER BY c.UsedDate DESC;

