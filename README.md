# PaymentSystem

A production-grade **e-commerce payment processing API** built with **.NET 9**, designed as a learning journey through 12 progressive phases — from a basic CRUD API to a resilient, event-driven, distributed-ready payment platform.

Every phase is **fully implemented, wired into the runtime, and covered by tests**. The current state: **147 / 147 tests passing**, zero warnings, zero errors.

---

## 🎯 What this project demonstrates

- Clean architecture (Domain → Application → Infrastructure → Api)
- Domain-Driven Design with rich aggregates and explicit state machines
- CQRS with MediatR (commands + queries separated, no anemic services)
- Transactional **Outbox pattern** for guaranteed event delivery
- **Idempotency** at every layer: DB unique constraints, application checks, Redis fast-paths
- **Resilience**: retry-with-backoff, transient-failure classification, compensation on provider errors
- **Background workers**: outbox publisher, payment reconciliation
- **Distributed infrastructure**: Redis cache, distributed locking, rate limiting
- **Event-driven architecture**: MassTransit + RabbitMQ, fan-out consumers
- **Testability**: in-memory fallbacks so the full pipeline runs without external services

---

## 🏗️ Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  Client                                                      │
└────────────────────────────┬─────────────────────────────────┘
                             │ HTTPS
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  Payment.Api (ASP.NET Core)                                  │
│  - Controllers (Checkout, Orders, Payments, Refunds,         │
│    Webhooks, Courses, Users)                                 │
│  - Middleware: exception handling, rate limiting             │
└────────────────────────────┬─────────────────────────────────┘
                             │ MediatR
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  Payment.Application (CQRS handlers, abstractions)          │
│  - IAppDbContext, IPaymentProvider, ICacheService,           │
│    IDistributedLock, IRateLimiter, INotificationService,     │
│    IOutboxPublisher                                          │
└────────────────────────────┬─────────────────────────────────┘
                             │
                             ▼
┌──────────────────────────────────────────────────────────────┐
│  Payment.Infrastructure                                      │
│  - EF Core + SQL Server + migrations                         │
│  - Stripe / Fake payment providers + resilience decorator    │
│  - Outbox processor (BackgroundService)                      │
│  - Reconciliation worker (BackgroundService)                 │
│  - Redis (cache, lock, rate limiter) or in-memory fallbacks  │
│  - MassTransit (in-memory / RabbitMQ) + consumers            │
└────────────────────────────┬─────────────────────────────────┘
                             │
          ┌──────────────────┼──────────────────┐
          ▼                  ▼                  ▼
   SQL Server             Redis            RabbitMQ
  (source of truth)   (optimization)     (event bus)
```

### Final event flow

```
Payment API
     │
     ▼ (same SQL transaction)
Outbox Messages
     │
     ▼
MassTransitOutboxPublisher
     │
     ▼
RabbitMQ / in-memory bus
     │
     ├──► EnrollmentConsumer     (grants course access)
     ├──► NotificationConsumer   (emails / push to customers)
     └──► AnalyticsConsumer      (append-only event history)
```

---

## 📦 Solution layout

```
PaymentSystem/
├── src/
│   ├── Payment.Domain/           # Entities, enums, state machines, outbox
│   ├── Payment.Application/      # CQRS handlers, abstractions, DTOs
│   ├── Payment.Infrastructure/   # EF Core, providers, workers, messaging
│   └── Payment.Api/              # ASP.NET Core host, controllers, middleware
├── tests/
│   └── Payment.Tests/            # 147 xUnit tests (unit + integration)
├── PLAN.md                       # Original 12-phase roadmap
└── README.md                     # this file
```

---

## 🛣️ The 12 phases

| Phase | Theme | Key deliverables |
|------:|-------|------------------|
| 1 | Basic e-commerce | Users, Courses, Orders, OrderItems |
| 2 | Payment domain | `Payment` aggregate with explicit state machine (`Pending → Processing → Succeeded/Failed`) |
| 3 | Payment provider | Stripe integration + in-memory fake provider for tests |
| 4 | Webhooks | Webhook verification + idempotent processing |
| 5 | Idempotency | DB unique constraints, event-type deduplication |
| 6 | Concurrency | Optimistic concurrency via EF Core, state guards |
| 7 | Failure handling | `ResilientPaymentProviderDecorator` (retries, transient classification), webhook failure compensation |
| 8 | Outbox pattern | `OutboxMessages` table, background publisher, retry-with-backoff, `Failed` instead of silent loss |
| 9 | Refunds | `Refund` aggregate, `Order.Refunded` state, full refund flow + webhook confirmation |
| 10 | Reconciliation | `PaymentReconciliationWorker` repairs payments stuck in `Pending/Processing` by querying the provider |
| 11 | Redis | Caching, distributed locking (Lua compare-and-delete), fixed-window rate limiting, webhook fast-path |
| 12 | Messaging | MassTransit + RabbitMQ, `EnrollmentConsumer`, `NotificationConsumer`, `AnalyticsConsumer` |

---

## 🔌 API endpoints

| Method | Path | Purpose |
|-------:|------|---------|
| POST   | `/api/checkout`                 | Create an order + Stripe checkout session |
| POST   | `/api/courses`                  | Create a course |
| GET    | `/api/courses`                  | List courses |
| GET    | `/api/courses/{id}`             | Get course by id |
| POST   | `/api/users`                    | Create a user |
| GET    | `/api/users/{id}`               | Get user by id |
| POST   | `/api/orders`                   | Create an order |
| GET    | `/api/orders`                   | List orders |
| GET    | `/api/orders/{id}`              | Get order by id |
| POST   | `/api/orders/{id}/cancel`       | Cancel an order |
| POST   | `/api/payments`                 | Create a payment |
| GET    | `/api/payments/{id}`            | Get payment (cached via Redis) |
| GET    | `/api/payments/{id}/reconcile`  | Reconcile a payment against the provider |
| POST   | `/api/payments/{id}/refund`     | Initiate a refund (returns 202) |
| POST   | `/api/webhooks/stripe`          | Stripe webhook (HMAC-verified) |

Rate-limited prefixes (per client IP, fixed window): `/api/checkout`, `/api/payments`. Webhook paths are deliberately excluded so provider retries aren't rejected.

---

## 🧱 Technology stack

| Concern | Library |
|--------|---------|
| Runtime | .NET 9 (API), .NET 10 (tests) |
| Web | ASP.NET Core |
| ORM | Entity Framework Core 9 + SQL Server |
| Mediation | MediatR 14 |
| Validation | FluentValidation 12 |
| Payments | Stripe.net 52 |
| Caching / Locking / Rate limiting | StackExchange.Redis 3.2 |
| Messaging | MassTransit 9.2 (in-memory + RabbitMQ transport) |
| Docs | Swashbuckle (OpenAPI + Swagger UI) |
| Testing | xUnit 2.9, Moq 4.20, Microsoft.EntityFrameworkCore.InMemory |

---

## 🚀 Getting started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (API) and [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (tests)
- SQL Server (LocalDB, Express, or any reachable instance)
- *(Optional)* Redis 6+ for caching/locking/rate-limiting at scale
- *(Optional)* RabbitMQ 3.x for real broker-based messaging
- *(Optional)* Stripe account + API keys for live payments

### 1. Clone and restore

```bash
git clone https://github.com/<your-user>/PaymentSystem.git
cd PaymentSystem
dotnet restore
```

### 2. Configure

Edit `src/Payment.Api/appsettings.json` (or use environment variables / user secrets):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=PaymentSystem;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "PaymentProvider": "Fake",
  "Stripe": { "SecretKey": "sk_test_...", "WebhookSecret": "whsec_..." },
  "Outbox": { "IntervalSeconds": 5, "BatchSize": 50, "MaxRetries": 5 },
  "Reconciliation": { "IntervalSeconds": 600, "StuckThresholdMinutes": 10, "BatchSize": 20 },
  "Redis": { "ConnectionString": "", "KeyPrefix": "paymentsystem:" },
  "RateLimiting": { "Enabled": true, "MaxRequests": 30, "WindowSeconds": 60, "ApplyToPathPrefixes": ["/api/checkout", "/api/payments"] },
  "Messaging": {
    "Provider": "InMemory",
    "RabbitMq": { "Host": "localhost", "VirtualHost": "/", "Username": "guest", "Password": "guest" }
  }
}
```

Key toggles:
- `PaymentProvider`: `Stripe` (real) or `Fake` (in-memory, for tests/dev)
- `Messaging:Provider`: `InMemory` (dev/tests) | `RabbitMQ` (production) | `None` (Phase-8 logging only)
- `Redis:ConnectionString`: empty = use in-memory fallbacks; set a real URL = use Redis

### 3. Apply migrations and run

```bash
dotnet ef database update --project src/Payment.Infrastructure --startup-project src/Payment.Api
dotnet run --project src/Payment.Api
```

The API starts on `https://localhost:7193` (or `http://localhost:5158` for the `http` profile). Swagger UI is available at `/swagger`.

---

## 🧪 Testing

```bash
dotnet test
```

**147 tests** cover:

- Domain state machines (Order, Payment, Refund, WebhookEvent, OutboxMessage, PaymentAttempt)
- CQRS handlers (checkout, refund, webhook processing, idempotency, outbox writes)
- Resilience (retry policy, transient classification, verify-not-retried guarantee)
- Background workers (outbox processor, reconciliation worker)
- Redis-backed behaviors (cache-aside, distributed lock semantics, fixed-window rate limiting)
- HTTP middleware (rate limiting → 429 + `Retry-After`, per-IP budgets, webhook-path exclusion)
- Messaging (MassTransit publisher, end-to-end outbox→bus→consumer integration)

No external services required — the tests run against in-memory database, in-memory Redis fallbacks, and MassTransit's in-memory transport.

---

## 🧠 Design decisions worth noting

### Outbox first, broker second
Every state change that should notify the outside world writes an `OutboxMessage` row **in the same SQL transaction**. The MassTransit publisher is just a bridge — if the broker is down, the row stays `Pending` and is retried with backoff. **Messages are never silently lost.**

### Idempotency in layers
Webhook delivery is protected at three levels:
1. **Redis fast-path** — a processed-event marker is checked before any DB work
2. **Application-level check** — `WebhookEvents.EventId` lookup
3. **DB unique constraint** — the authoritative fallback that survives cache eviction

Enrollments use the same pattern: app-level check + `UNIQUE(UserId, CourseId)`.

### Fail-open for optimization layers
Redis-backed cache, lock, and rate limiter all fail **open**: if Redis is down, the system keeps working (SQL remains the source of truth; rate limiting degrades to no-ops). The payment flow never depends on Redis availability.

### Resilience decorator
`ResilientPaymentProviderDecorator` wraps every payment provider (Stripe, Fake) with:
- Transient-failure classification (timeouts, 5xx, rate limits, network errors)
- Exponential backoff with jitter
- Max-attempt cap
- Explicit rule: `VerifyWebhook` is **never** retried (idempotency lives at the consumer, not the verifier)

### Reconciliation repairs drift
`PaymentReconciliationWorker` periodically queries payments stuck in `Pending` or `Processing` longer than `StuckThresholdMinutes`, asks the provider for the real status, and repairs state: confirm, cancel, or mark failed. No manual intervention required for the common "webhook got lost" case.

---

## 📁 Migrations

```
20260913143016_InitialCreate          Users, Courses, Orders, OrderItems, Payments, PaymentAttempts, WebhookEvents
20260913175721_AddOutboxMessages      OutboxMessages (Phase 8)
20260913183218_AddRefunds             Refunds (Phase 9)
20260914081954_AddEnrollmentsAndAnalytics   Enrollments + AnalyticsEvents (Phase 12)
```

To add a new migration:

```bash
dotnet ef migrations add <Name> --project src/Payment.Infrastructure --startup-project src/Payment.Api
```

---

## 📜 License

MIT — use it however you want. This is a learning project; attribution appreciated but not required.

---

## 🙏 Acknowledgments

Structured as a 12-phase roadmap inspired by real-world payment system requirements. Each phase builds on the previous one so the codebase grows organically from a CRUD API into a distributed, resilient, event-driven platform — with every decision justified by a concrete failure mode it prevents.
