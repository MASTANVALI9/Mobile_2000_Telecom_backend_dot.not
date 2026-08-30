# Postman API Testing Guide

This guide explains how to test all API endpoints using Postman or Swagger UI, including authentication headers, error handling scenarios, and reconciliation flows.

---

## 1. Authentication Headers

Both APIs are protected with API Key authentication:

| API | Port | Header Name | Configured Test Key |
|-----|------|-------------|---------------------|
| **Main Recharge API** | 5080 | `X-API-Key` | `mobile2000-secret-api-key-2026` |
| **Mock Telecom Provider API** | 5081 | `X-Provider-API-Key` | `telecom-provider-test-key-2026` |

> [!NOTE]
> All API keys and secrets are loaded from `appsettings.json` (or environment variables) and are never logged or hardcoded.

---

## 2. How to Start the Servers

Open two separate terminals:

```bash
# Terminal 1: Start Mock Telecom Provider (Port 5081)
dotnet run --project src/MockTelecomApi

# Terminal 2: Start Main Recharge API (Port 5080)
dotnet run --project src/MainRechargeApi
```

- **Main API Swagger**: http://localhost:5080/swagger (Click **Authorize** and enter `mobile2000-secret-api-key-2026`)
- **Mock Provider Swagger**: http://localhost:5081/swagger (Click **Authorize** and enter `telecom-provider-test-key-2026`)

---

## 3. Step-by-Step Testing Guide

### Test Case 1: Successful Recharge (Jio / Airtel / Vi)
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/recharge`
- **Headers**:
  - `Content-Type`: `application/json`
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Body**:
  ```json
  {
    "mobileNumber": "9876543210",
    "operatorName": "Jio",
    "amount": 100.00
  }
  ```
- **Expected Response (`200 OK`)**:
  ```json
  {
    "transactionId": "TXN-20260830-XXXX",
    "mobileNumber": "9876543210",
    "operatorName": "Jio",
    "amount": 100.00,
    "status": "SUCCESS",
    "providerReference": "MOCK-REF-XXXX",
    "errorMessage": null
  }
  ```

---

### Test Case 2: Check Transaction Status
- **Method**: `GET`
- **URL**: `http://localhost:5080/api/recharge/status/TXN-20260830-XXXX`
- **Headers**:
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Expected Response (`200 OK`)**:
  ```json
  {
    "transactionId": "TXN-20260830-XXXX",
    "mobileNumber": "9876543210",
    "operatorName": "Jio",
    "amount": 100.00,
    "status": "SUCCESS",
    "providerReference": "MOCK-REF-XXXX",
    "errorMessage": null
  }
  ```

---

### Test Case 3: Authentication Failure (401 Unauthorized)
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/recharge`
- **Headers**:
  - `Content-Type`: `application/json`
  - *(Omit `X-API-Key` or provide an invalid key)*
- **Body**:
  ```json
  {
    "mobileNumber": "9876543210",
    "operatorName": "Jio",
    "amount": 100.00
  }
  ```
- **Expected Response (`401 Unauthorized`)**:
  ```json
  {
    "statusCode": 401,
    "error": "AUTHENTICATION_FAILED",
    "message": "Missing 'X-API-Key' authentication header.",
    "timestamp": "2026-08-30T17:05:00Z"
  }
  ```

---

### Test Case 4: Invalid Mobile Number (400 Bad Request)
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/recharge`
- **Headers**:
  - `Content-Type`: `application/json`
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Body**:
  ```json
  {
    "mobileNumber": "98765",
    "operatorName": "Jio",
    "amount": 100.00
  }
  ```
- **Expected Response (`400 Bad Request`)**:
  ```json
  {
    "statusCode": 400,
    "error": "INVALID_MOBILE_NUMBER",
    "message": "Mobile number must be exactly 10 digits numeric (e.g. 9876543210).",
    "timestamp": "2026-08-30T17:05:00Z"
  }
  ```

---

### Test Case 5: Invalid Amount (400 Bad Request)
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/recharge`
- **Headers**:
  - `Content-Type`: `application/json`
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Body**:
  ```json
  {
    "mobileNumber": "9876543210",
    "operatorName": "Jio",
    "amount": -50.00
  }
  ```
- **Expected Response (`400 Bad Request`)**:
  ```json
  {
    "statusCode": 400,
    "error": "INVALID_AMOUNT",
    "message": "Recharge amount must be greater than zero.",
    "timestamp": "2026-08-30T17:05:00Z"
  }
  ```

---

### Test Case 6: Unsupported Operator (400 Bad Request)
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/recharge`
- **Headers**:
  - `Content-Type`: `application/json`
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Body**:
  ```json
  {
    "mobileNumber": "9876543210",
    "operatorName": "Docomo",
    "amount": 100.00
  }
  ```
- **Expected Response (`400 Bad Request`)**:
  ```json
  {
    "statusCode": 400,
    "error": "UNSUPPORTED_OPERATOR",
    "message": "Operator 'Docomo' is not supported or is currently inactive. Supported operators: Jio, Airtel, Vi, BSNL.",
    "timestamp": "2026-08-30T17:05:00Z"
  }
  ```

---

### Test Case 7: Provider HTTP 500 Simulation
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/recharge`
- **Headers**:
  - `Content-Type`: `application/json`
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Body**:
  ```json
  {
    "mobileNumber": "9876543210",
    "operatorName": "Airtel",
    "amount": 500.00
  }
  ```
- **Expected Response (`200 OK`)**:
  ```json
  {
    "transactionId": "TXN-20260830-XXXX",
    "mobileNumber": "9876543210",
    "operatorName": "Airtel",
    "amount": 500.00,
    "status": "FAILED",
    "errorMessage": "Internal server error. The provider system is temporarily unavailable."
  }
  ```

---

### Test Case 8: Provider Timeout & Background Reconciliation
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/recharge`
- **Headers**:
  - `Content-Type`: `application/json`
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Body**:
  ```json
  {
    "mobileNumber": "9876543210",
    "operatorName": "Airtel",
    "amount": 299.00
  }
  ```
- **Immediate Response (`200 OK`)**:
  ```json
  {
    "transactionId": "TXN-20260830-XXXX",
    "mobileNumber": "9876543210",
    "operatorName": "Airtel",
    "amount": 299.00,
    "status": "PENDING",
    "errorMessage": "Provider connection timed out. Your recharge is being verified and will be updated shortly."
  }
  ```
- **Reconciliation**:
  Wait 30 seconds for the `ReconciliationWorker` to poll the provider.
  Query `GET http://localhost:5080/api/recharge/status/TXN-20260830-XXXX` to see the updated status: `SUCCESS`.

---

### Test Case 9: Duplicate Recharge Prevention
- Send the same request twice within 10 minutes (or with the same `transactionId`).
- The second request returns the existing transaction with an informative message: `"Already recharged! This pack will activate after completion of the first pack."`

---

### Test Case 10: CSV Card / Voucher Import (Feature 18)
- **Method**: `POST`
- **URL**: `http://localhost:5080/api/cards/import/raw`
- **Headers**:
  - `Content-Type`: `application/json`
  - `X-API-Key`: `mobile2000-secret-api-key-2026`
- **Body**:
  ```json
  {
    "fileName": "batch_import_2027.csv",
    "csvContent": "CardNumber,SerialNumber,Operator,Denomination,ExpiryDate\n987654321001,SER10001,Airtel,100,2027-12-31\n987654321002,SER10002,Jio,199,2027-12-31\n987654321003,SER10003,Vi,249,2027-12-31\n987654321004,SER10004,BSNL,50,2027-12-31\n987654321005,SER10005,BSNL,100,2027-12-31",
    "importedBy": "ADMIN_USER"
  }
  ```
- **Expected Response (`200 OK`)**:
  ```json
  {
    "batchId": 1,
    "fileName": "batch_import_2027.csv",
    "totalRows": 5,
    "successfulRows": 5,
    "failedRows": 0,
    "status": "COMPLETED",
    "importedBy": "ADMIN_USER",
    "importedDate": "2026-08-30T16:50:00Z",
    "message": "Successfully imported all 5 voucher cards.",
    "errors": []
  }
  ```

---

### Test Case 11: Voucher Inventory & Batch Querying
- **Batches Query**: `GET http://localhost:5080/api/cards/batches?page=1&pageSize=20`
  - Header: `X-API-Key: mobile2000-secret-api-key-2026`
  - Returns paginated list of import batches with total, successful, and failed counts.
- **Batch Details Query**: `GET http://localhost:5080/api/cards/batches/1`
  - Returns full batch header and individual row error details.
- **Voucher Stock Summary**: `GET http://localhost:5080/api/cards/inventory`
  - Returns card counts grouped by operator, denomination, and availability status (`AVAILABLE`, `USED`, `EXPIRED`).

---

## 4. Direct Mock Provider Testing (Port 5081)

- **Rules Endpoint (Public)**: `GET http://localhost:5081/api/provider/recharge/rules`
- **Recharge Endpoint**: `POST http://localhost:5081/api/provider/recharge`
  - Header: `X-Provider-API-Key: telecom-provider-test-key-2026`
- **Status Endpoint**: `GET http://localhost:5081/api/provider/recharge/status/{referenceId}`
  - Header: `X-Provider-API-Key: telecom-provider-test-key-2026`

