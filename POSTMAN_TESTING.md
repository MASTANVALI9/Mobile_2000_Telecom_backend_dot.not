# Postman API Testing

Use Postman or Swagger to test the APIs.

## 1. API Keys

Both APIs use API key authentication.

| API               | Port | Header               |
| ----------------- | ---: | -------------------- |
| Main Recharge API | 5080 | `X-API-Key`          |
| Mock Telecom API  | 5081 | `X-Provider-API-Key` |

Test keys are stored in `appsettings.json` and can also be supplied through environment variables.

> Do not use real credentials in the repository.

## 2. Start the APIs

Open two terminals.

**Mock Telecom API**

```bash
dotnet run --project src/MockTelecomApi
```

**Main Recharge API**

```bash
dotnet run --project src/MainRechargeApi
```

Swagger:

* Main API: `http://localhost:5080/swagger`
* Mock Provider: `http://localhost:5081/swagger`

Add the required API key using the **Authorize** button in Swagger.

---

## 3. Main API Tests

### Test 1 — Successful Recharge

**POST**

`http://localhost:5080/api/recharge`

Headers:

```text
Content-Type: application/json
X-API-Key: <main-api-key>
```

Body:

```json
{
  "mobileNumber": "9876543210",
  "operatorName": "Jio",
  "amount": 100.00
}
```

Expected:

* `200 OK`
* Transaction is created.
* Provider request is recorded.
* Recharge status is `SUCCESS`.

---

### Test 2 — Check Transaction Status

**GET**

`http://localhost:5080/api/recharge/status/{transactionId}`

Header:

```text
X-API-Key: <main-api-key>
```

Expected:

* `200 OK`
* Current transaction details are returned.

---

### Test 3 — Invalid Authentication

Send the recharge request without `X-API-Key`, or use an invalid key.

Expected:

```text
401 Unauthorized
```

The recharge should not be processed.

---

### Test 4 — Invalid Mobile Number

Use:

```json
{
  "mobileNumber": "98765",
  "operatorName": "Jio",
  "amount": 100.00
}
```

Expected:

```text
400 Bad Request
```

The provider should not be called.

---

### Test 5 — Invalid Amount

Use:

```json
{
  "mobileNumber": "9876543210",
  "operatorName": "Jio",
  "amount": -50.00
}
```

Expected:

```text
400 Bad Request
```

---

### Test 6 — Unsupported Operator

Use:

```json
{
  "mobileNumber": "9876543210",
  "operatorName": "Docomo",
  "amount": 100.00
}
```

Expected:

```text
400 Bad Request
```

Supported operators:

* Jio
* Airtel
* Vi
* BSNL

---

### Test 7 — Provider Error

Use the provider test amount that triggers an HTTP 500 response.

Example:

```json
{
  "mobileNumber": "9876543210",
  "operatorName": "Airtel",
  "amount": 500.00
}
```

Expected:

* Provider returns an error.
* Main API handles it without exposing internal details.
* Transaction is stored with the appropriate failed status.

---

### Test 8 — Provider Timeout

Use the amount configured for the timeout scenario.

Example:

```json
{
  "mobileNumber": "9876543210",
  "operatorName": "Airtel",
  "amount": 299.00
}
```

Expected:

1. Provider request is sent.
2. Provider response times out.
3. Transaction is stored as `PENDING`.
4. The recharge is **not sent again**.
5. Background reconciliation checks the provider status.
6. Transaction is eventually updated to the actual provider result.

Check the transaction again using:

```text
GET /api/recharge/status/{transactionId}
```

---

### Test 9 — Duplicate Recharge

Send the same transaction again.

Expected:

* Existing transaction is returned.
* A second recharge is not created.
* The provider is not called again.

This should also be safe when two identical requests arrive at nearly the same time.

---

## 4. Card Import Tests

### Test 10 — Import Cards

**POST**

`http://localhost:5080/api/cards/import/raw`

Headers:

```text
Content-Type: application/json
X-API-Key: <main-api-key>
```

Example:

```json
{
  "fileName": "batch_import_2027.csv",
  "csvContent": "CardNumber,SerialNumber,Operator,Denomination,ExpiryDate\n987654321001,SER10001,Airtel,100,2027-12-31\n987654321002,SER10002,Jio,199,2027-12-31\n987654321003,SER10003,Vi,249,2027-12-31\n987654321004,SER10004,BSNL,50,2027-12-31",
  "importedBy": "ADMIN_USER"
}
```

Expected:

* Valid rows are imported.
* Invalid rows are reported.
* Duplicate cards are rejected.
* Import summary is returned.
* Import batch is stored.

---

### Test 11 — Card Inventory

**GET**

```text
http://localhost:5080/api/cards/inventory
```

Header:

```text
X-API-Key: <main-api-key>
```

Returns card counts grouped by operator, denomination and status.

---

### Test 12 — Import Batches

**GET**

```text
http://localhost:5080/api/cards/batches?page=1&pageSize=20
```

Returns the import batch history.

To view one batch:

```text
GET /api/cards/batches/{batchId}
```

---

## 5. Mock Telecom API

The mock provider runs on port `5081`.

### Rules

**GET**

```text
http://localhost:5081/api/provider/recharge/rules
```

This shows the test rules configured for the provider.

### Recharge

**POST**

```text
http://localhost:5081/api/provider/recharge
```

Header:

```text
X-Provider-API-Key: <provider-api-key>
```

### Provider Status

**GET**

```text
http://localhost:5081/api/provider/recharge/status/{referenceId}
```

Header:

```text
X-Provider-API-Key: <provider-api-key>
```

This endpoint is used to check the final provider status, especially after a timeout.

---

## 6. Main Scenarios to Verify

Before submitting the project, verify these scenarios:

| Scenario                     | Expected Result                |
| ---------------------------- | ------------------------------ |
| Successful recharge          | `SUCCESS`                      |
| Failed provider request      | `FAILED`                       |
| Provider timeout             | `PENDING`, then reconciliation |
| Provider HTTP 500            | Handled safely                 |
| Invalid mobile               | `400`                          |
| Invalid amount               | `400`                          |
| Unsupported operator         | `400`                          |
| Invalid API key              | `401`                          |
| Duplicate transaction        | No second recharge             |
| Concurrent duplicate request | Only one provider request      |
| CSV import                   | Valid rows imported            |
| Duplicate card               | Rejected                       |
| Expired card                 | Cannot be used                 |
| Concurrent card usage        | Only one request succeeds      |

## 7. Test Evidence

Postman screenshots for the main scenarios are kept in the project documentation.

The important scenarios to capture are:

* Successful recharge
* Failed recharge
* Provider timeout
* Duplicate transaction
* Invalid request
* Authentication failure
* CSV import
* Provider status enquiry

Use screenshots only as evidence of tests that were actually run.
