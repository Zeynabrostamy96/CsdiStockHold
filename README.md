# StockHold - stock management and reservation API

An ASP.NET Core Web API (.NET 10) with SQL Server and Entity Framework Core, written for the "StockHold" technical challenge.
Customers reserve units of a product for a limited time. The system keeps the available stock correct,
never reserves more than exists (also when requests arrive at the same time) and does not create a second
reservation when a request is sent again.

| Customer type | Maximum units a customer can reserve | A reservation stays valid for |
|---|---|---|
| Regular | 3 | 10 minutes |
| Special | 10 | 30 minutes |

## Project structure

```
src/CsdiStockHold.Domain          entities and business rules (Customer, Product, Reservation); no dependencies
src/CsdiStockHold.Infrastructure  EF Core DbContext and migration, ReservationService (transaction, row locks, idempotency)
src/CsdiStockHold.Api             Minimal API endpoints, error mapping, OpenAPI document and Swagger UI
tests/CsdiStockHold.Tests         unit tests, in-memory HTTP tests and SQL Server integration tests
database/create-database.sql      script that creates the tables, keys and indexes
```

## Getting started

### Prerequisites

- The .NET 10 SDK (`global.json` asks for 10.0.100 or a newer 10.0 SDK).
- A SQL Server instance on which you can create a database. The project runs locally; no Docker setup is included.

### 1. Create the database

Create an empty database, then run [`database/create-database.sql`](database/create-database.sql) on it (SQL Server Management Studio,
Azure Data Studio, or `sqlcmd -S <server> -d <database> -i database/create-database.sql` with your usual authentication options).
The script contains no `CREATE DATABASE`, it only creates the three tables, their keys and the two indexes, and it can safely be run more than once.

Alternatively, with the connection string of step 2 set, apply the EF Core migration:

```bash
dotnet tool restore
dotnet ef database update --project src/CsdiStockHold.Infrastructure --startup-project src/CsdiStockHold.Api
```

### 2. Set the connection string

The repository contains no connection string (`ConnectionStrings:StockHold` is `null` in `appsettings.json`) and no credentials.
Provide your own, for example with an environment variable (note the double underscore):

```powershell
$env:ConnectionStrings__StockHold = "<your SQL Server connection string>"
```

```bash
export ConnectionStrings__StockHold="<your SQL Server connection string>"
```

It must point to the database of step 1. A local server with a self-signed certificate may need `TrustServerCertificate=True` in the connection string.
You can also put the value in `src/CsdiStockHold.Api/appsettings.Development.json`; do not commit it.

### 3. Run

```bash
dotnet run --project src/CsdiStockHold.Api
```

The API listens on <http://localhost:5104>. `--launch-profile https` also serves <https://localhost:7252>, which needs the ASP.NET Core development certificate (`dotnet dev-certs https --trust`).

- Swagger UI, to view and try the API: `/swagger`
- OpenAPI document: `/openapi/v1.json`

Without a connection string the application still starts and Swagger works, but every endpoint that needs the database answers 500.

## API

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/products` | Register a product (`name`, `stockQuantity`) |
| GET | `/api/products/{id}` | Get a product with its **available quantity** (stock minus the units held by active reservations) |
| POST | `/api/customers` | Register a customer (`type`: `Regular` or `Special`) |
| GET | `/api/customers/{id}` | Get a customer |
| POST | `/api/reservations` | Reserve units of a product; needs the `Idempotency-Key` header (`customerId`, `productId`, `quantity`) |
| GET | `/api/reservations/{id}` | Get a reservation (`isExpired` tells whether it has expired) |
| GET | `/api/reservations/expired` | List the expired reservations |

```bash
curl -X POST http://localhost:5104/api/products -H "Content-Type: application/json" -d '{"name":"Laptop","stockQuantity":5}'
curl -X POST http://localhost:5104/api/customers -H "Content-Type: application/json" -d '{"type":"Special"}'
curl -X POST http://localhost:5104/api/reservations -H "Content-Type: application/json" -H "Idempotency-Key: order-1001" -d '{"customerId":1,"productId":1,"quantity":4}'
```

### Responses

| Status | When |
|---|---|
| 201 Created | A product, customer or reservation was created (with a `Location` header) |
| 200 OK | A read succeeded, or a reservation request was **repeated** and the original reservation is returned |
| 400 Bad Request | The request cannot be read or breaks a rule: missing or invalid `Idempotency-Key`, quantity below 1, a blank or too long (over 200 characters) product name, a negative stock, an unknown customer type |
| 404 Not Found | The customer, product or reservation does not exist |
| 409 Conflict | Not enough stock |
| 422 Unprocessable Content | The customer limit is exceeded, or the idempotency key was already used for a different request |

Errors are RFC 9457 problem details. The `detail` property is the message and the `code` property names the violated rule:

```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10", "title": "Conflict", "status": 409,
  "detail": "Not enough stock. Available: 1, requested: 4.", "code": "InsufficientStock" }
```

The codes are `InvalidProductName`, `InvalidStockQuantity`, `InvalidCustomerType`, `InvalidQuantity`, `InvalidIdempotencyKey`,
`CustomerNotFound`, `ProductNotFound`, `InsufficientStock`, `CustomerLimitExceeded` and `IdempotencyKeyConflict`.
The status code of each of them is decided in one place, `DomainExceptionHandler`.

## How the requirements are met

**Available stock.** The available quantity of a product is its stock minus the units of its *active* reservations. A reservation is active while
`ExpiresAt > now`, so the stock of an expired reservation is available again without any job or status update.

**Customer limit.** The limit of the customer type (3 or 10 units) is checked when a reservation is requested, against the *total* units
of the customer's active reservations over all products.

**No overselling, also for concurrent requests.** `ReservationService` runs one request in one `ReadCommitted` transaction. It first locks the customer row and then the product row
(`UPDLOCK, ROWLOCK`; always in this order), calculates the active reservations and only then creates the reservation. Requests for the same product (or customer) therefore run one after another, and each of them sees
the reservations the previous ones committed. There is no automatic retry: a deadlock victim or a timeout is reported as 500.

**Repeated requests.** Every reservation request carries an `Idempotency-Key` header (at most 100 characters). A key is unique per customer, which the database enforces with a unique index on `(CustomerId, IdempotencyKey)`.
Sending the same request again returns the original reservation with 200, even after it has expired, and changes nothing. Using the key for another product or quantity is rejected with 422.
Concurrent requests with the same key are serialized by the customer lock, so exactly one of them creates the reservation.

**Expired reservations.** They are identified by `isExpired` and by `GET /api/reservations/expired`, and they are managed by the time-based rule above.
They are deliberately not deleted, because a repeated request must still find the original reservation.

**Business rules live in the Domain.** The API and `ReservationService` do not repeat them; the Domain throws a `DomainException` with a code and the API maps it to an HTTP response.

## Assumptions and decisions

The specification leaves these open, so they are decisions of this implementation:

- The customer limit applies to the total of a customer's active reservations (all products), not to a single request.
- A customer is only an id and a type; products and customers cannot be edited or deleted, and reservations cannot be cancelled, because the specification does not ask for it.
- The `Idempotency-Key` header is how a repeated request is recognized (a repeated request cannot be told from a new request for the same units otherwise). It is required.
- A reservation is expired from the moment `ExpiresAt` is reached (`now >= ExpiresAt`).
- The mapping of business rules to HTTP status codes (the specification only asks for a suitable message).
- SQL Server's default collation compares text case-insensitively, so two idempotency keys that differ only in letter case count as the same key.
- The list of expired reservations is not paged.

## Tests

```bash
dotnet test --filter "Category!=Integration"
```

runs everything that needs no database: the domain tests, the tests of the HTTP layer (the application is hosted in memory: request validation, error mapping, OpenAPI and Swagger UI), the model tests and a test that checks that `database/create-database.sql` is exactly the script of the EF Core migrations.

The tests marked `Category=Integration` need a real SQL Server. They cover the reservation scenarios of the specification (valid reservation, insufficient stock, customer limit,
expired reservations, repeated requests, concurrent requests) on the service level and on the HTTP level, and that the data survives a restart of the application. To run them, set `ConnectionStrings__StockHold` (see step 2) and run:

```bash
dotnet test --filter "Category=Integration"
```

Each test class creates its own temporary database named `CsdiStockHold_Test_<guid>`, applies the migration to it and drops it afterwards. The database named in your connection string is never used or changed;
only the server and the credentials are taken from it. The login therefore needs the permission to create and drop databases.

## Notes

- After a change of the EF Core model, add a migration and regenerate the script:

  ```bash
  dotnet ef migrations add <Name> --project src/CsdiStockHold.Infrastructure --startup-project src/CsdiStockHold.Api
  dotnet ef migrations script --idempotent --project src/CsdiStockHold.Infrastructure --startup-project src/CsdiStockHold.Api --output database/create-database.sql
  ```

- The code style is enforced during the build (`.editorconfig`, warnings are errors, line endings are LF).
