# Payment Backend — ASP.NET Core + SQL Server

## 1. Project Overview

Build a production-oriented **Course/E-Commerce Payment Backend** using:

- ASP.NET Core
- SQL Server
- Entity Framework Core
- Clean Architecture
- CQRS + MediatR
- FluentValidation
- Stripe or another payment provider
- Redis — optional later
- Background workers — later
- Docker — optional
- JWT authentication

The main objective is **not simply processing payments**.

The objective is to learn how to build a backend that remains correct when:

- the same webhook arrives multiple times
- two requests attempt the same operation simultaneously
- the client retries a request
- the payment provider retries a webhook
- your server crashes during processing
- the database transaction fails
- the payment succeeds but your application does not receive the webhook immediately
- the webhook arrives before/after another related operation
- requests arrive in the wrong order

---

# 2. Core Architecture

The basic workflow:

```text
                    Client
                      |
                      v
              POST /checkout
                      |
                      v
              Create Order
                      |
                      v
            Create Payment Attempt
                      |
                      v
             Payment Provider
                      |
                      v
                  Checkout
                      |
                      v
             Customer Payment
                      |
                      v
                 Webhook
                      |
                      v
             Verify Signature
                      |
                      v
             Idempotency Check
                      |
                      v
             Process Event
                      |
             +--------+--------+
             |                 |
             v                 v
        Update Payment    Update Order
             |                 |
             +--------+--------+
                      |
                      v
                Commit TX
```

The most important principle:

> The client should not be trusted to tell your backend that a payment succeeded.

For example, don't do:

```http
POST /orders/123/payment-success
```

The authoritative payment state should come from the payment provider's verified webhook/event.

---

# 3. Recommended Technology Stack

## Backend

```text
ASP.NET Core
Entity Framework Core
SQL Server
MediatR
FluentValidation
JWT Authentication
```

## Payment

Start with:

```text
Stripe Checkout
Stripe Webhooks
```

You can later abstract the provider:

```text
IPaymentProvider
       |
       +---- StripePaymentProvider
       |
       +---- FakePaymentProvider
       |
       +---- PayPalPaymentProvider
```

This teaches the **Strategy / Adapter pattern** and prevents Stripe-specific code from leaking throughout your application.

---

# 4. Clean Architecture

Recommended structure:

```text
src/
│
├── Payment.Api/
│   ├── Controllers/
│   ├── Middleware/
│   ├── Extensions/
│   └── Program.cs
│
├── Payment.Application/
│   ├── Abstractions/
│   │   ├── Persistence/
│   │   ├── Payments/
│   │   └── Identity/
│   │
│   ├── Features/
│   │   ├── Orders/
│   │   ├── Payments/
│   │   └── Webhooks/
│   │
│   ├── Behaviors/
│   └── Common/
│
├── Payment.Domain/
│   ├── Entities/
│   ├── Enums/
│   ├── ValueObjects/
│   ├── Events/
│   └── Exceptions/
│
└── Payment.Infrastructure/
    ├── Persistence/
    │   ├── AppDbContext.cs
    │   ├── Configurations/
    │   └── Migrations/
    │
    ├── Payments/
    │   └── Stripe/
    │
    └── Identity/
```

Dependency direction:

```text
API
 |
 v
Application
 |
 v
Domain

Infrastructure
      |
      +------> Application
      |
      +------> Domain
```

The Domain should know nothing about Stripe or EF Core.

---

# 5. Domain Model

Start with these entities:

```text
User
Product / Course
Order
OrderItem
Payment
PaymentAttempt
WebhookEvent
```

You can simplify the first version by using:

```text
Course
Order
OrderItem
Payment
WebhookEvent
```

---

# 6. Order Entity

Example conceptual model:

```text
Order
--------------------------------
Id
UserId
OrderNumber
Status
Currency
Subtotal
Total
CreatedAt
UpdatedAt
```

Order statuses:

```csharp
public enum OrderStatus
{
    Pending,
    PaymentProcessing,
    Paid,
    Failed,
    Cancelled,
    Expired
}
```

Think of this as a state machine.

```text
Pending
   |
   v
PaymentProcessing
   |
   +----> Paid
   |
   +----> Failed
   |
   +----> Expired
   |
   +----> Cancelled
```

Do not allow arbitrary transitions.

For example:

```text
Paid -> Pending
```

should normally be invalid.

---

# 7. OrderItem

```text
OrderItem
--------------------------------
Id
OrderId
CourseId
Quantity
UnitPrice
TotalPrice
```

Important:

> Store the price at the time of purchase.

Do not calculate an old order using the current Course price.

Example:

```text
Course current price = $100

Customer creates order
OrderItem.UnitPrice = $100

Later:

Course price = $150
```

The existing order should still contain:

```text
UnitPrice = $100
```

---

# 8. Payment Entity

```text
Payment
--------------------------------
Id
OrderId
Provider
ProviderPaymentId
Amount
Currency
Status
CreatedAt
UpdatedAt
```

Payment statuses:

```csharp
public enum PaymentStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Cancelled,
    Refunded
}
```

Relationship:

```text
Order
  |
  | 1
  |
  | *
Payment
```

You may eventually allow multiple payment attempts:

```text
Order
 |
 +---- PaymentAttempt #1 -> Failed
 |
 +---- PaymentAttempt #2 -> Failed
 |
 +---- PaymentAttempt #3 -> Succeeded
```

This is more realistic than assuming:

```text
One Order = One Payment
```

---

# 9. Payment Attempt

For the more advanced version:

```text
PaymentAttempt
--------------------------------
Id
OrderId
Provider
ProviderPaymentId
Amount
Currency
Status
AttemptNumber
CreatedAt
CompletedAt
FailureReason
```

Example:

```text
Order #1001

Attempt #1
Stripe Session = abc
Status = Failed

Attempt #2
Stripe Session = xyz
Status = Succeeded
```

This gives you a proper payment lifecycle.

---

# 10. WebhookEvent Entity

This is one of the most important tables.

```text
WebhookEvent
--------------------------------
Id
Provider
ProviderEventId
EventType
ReceivedAt
ProcessedAt
Status
Error
Payload
```

Example:

```text
ProviderEventId:
evt_123456
```

Database constraint:

```text
UNIQUE(Provider, ProviderEventId)
```

This gives you your first major idempotency protection.

---

# 11. Why WebhookEvent Is Important

Suppose Stripe sends:

```text
evt_123
```

Your server receives:

```text
evt_123
```

You process it.

Then Stripe sends it again:

```text
evt_123
```

Your database already contains:

```text
Provider = Stripe
ProviderEventId = evt_123
```

Therefore:

```text
Already processed
       |
       v
Return 200
       |
       X
Don't process again
```

This is **event-level idempotency**.

---

# 12. Database Constraints

Do not rely only on application code.

Database constraints are extremely important.

For example:

```sql
CREATE UNIQUE INDEX UX_WebhookEvents_Provider_EventId
ON WebhookEvents(Provider, ProviderEventId);
```

Payment provider ID:

```sql
CREATE UNIQUE INDEX UX_Payments_Provider_PaymentId
ON Payments(Provider, ProviderPaymentId);
```

Order number:

```sql
CREATE UNIQUE INDEX UX_Orders_OrderNumber
ON Orders(OrderNumber);
```

This is important because:

```text
Application validation
```

and

```text
Database constraint
```

solve different problems.

The application can check:

```csharp
if (await repository.Exists(...))
{
    ...
}
```

But two requests can still do:

```text
Request A                 Request B

Check exists              Check exists
    |                          |
    v                          v
No                         No
    |                          |
    v                          v
Insert                     Insert
```

Both may see "No".

The unique database index prevents the final duplicate.

---

# 13. Create Checkout

Endpoint:

```http
POST /api/orders/{orderId}/checkout
```

Request:

```json
{
    "paymentMethod": "card"
}
```

Or:

```http
POST /api/checkout
```

```json
{
    "items": [
        {
            "courseId": "..."
        }
    ]
}
```

---

# 14. Checkout Flow

The checkout process:

```text
Client
  |
  v
Create Order
  |
  v
Validate courses
  |
  v
Calculate price
  |
  v
Create OrderItems
  |
  v
Create Payment
  |
  v
Commit database transaction
  |
  v
Create provider checkout session
  |
  v
Return checkout URL
```

Important design question:

### Should you call Stripe inside the database transaction?

Prefer not to hold a SQL transaction open while making an external network call.

Avoid:

```text
BEGIN TRANSACTION

Insert Order

Call Stripe
(wait 1 second)

Insert Payment

COMMIT
```

Instead:

```text
Create Order
Create Payment
COMMIT

Call Stripe
```

Then update the payment with the provider ID.

This introduces an important distributed-systems problem:

```text
Database
     |
     | success
     v
Commit
     |
     X
Application crashes
     |
     v
Stripe checkout was never created
```

You will later solve this with more advanced patterns such as:

```text
Outbox Pattern
Background Worker
Retry
```

Do not hide this problem. It is one of the reasons payment systems are excellent backend projects.

---

# 15. Checkout Idempotency

Your checkout endpoint should also support idempotency.

Client sends:

```http
Idempotency-Key: 7d8e...
```

Example:

```text
POST /api/checkout

Idempotency-Key: abc123
```

Client times out.

It doesn't know whether your server succeeded.

So it retries:

```text
POST /api/checkout

Idempotency-Key: abc123
```

Your backend should return the original result instead of creating:

```text
Order #100
Order #101
Order #102
```

for one logical request.

---

# 16. IdempotencyKey Table

Create:

```text
IdempotencyRequest
--------------------------------
Id
UserId
Key
RequestHash
ResponseStatus
ResponseBody
CreatedAt
ExpiresAt
```

Unique constraint:

```text
(UserId, Key)
```

Flow:

```text
Request
   |
   v
Find IdempotencyKey
   |
   +---- exists ----> Return stored response
   |
   |
   +---- doesn't exist
              |
              v
         Process request
              |
              v
         Store response
              |
              v
         Return response
```

---

# 17. Payment Provider Abstraction

Create:

```csharp
public interface IPaymentProvider
{
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken);

    PaymentWebhookEvent VerifyWebhook(
        string payload,
        string signature);

    Task<PaymentDetails> GetPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken);
}
```

Infrastructure:

```text
IPaymentProvider
      |
      v
StripePaymentProvider
```

Application doesn't know:

```text
Stripe.Checkout.Session
Stripe.Event
Stripe.PaymentIntent
```

Those belong in Infrastructure.

---

# 18. Webhook Endpoint

Create:

```http
POST /api/webhooks/stripe
```

The provider sends:

```json
{
    "id": "evt_123",
    "type": "payment_intent.succeeded",
    "data": {
        ...
    }
}
```

Your endpoint should NOT blindly trust the payload.

---

# 19. Webhook Verification

Flow:

```text
Webhook
   |
   v
Read raw request body
   |
   v
Read signature header
   |
   v
Verify provider signature
   |
   +---- invalid ---> 400
   |
   v
Parse event
   |
   v
Process event
```

Signature verification must happen against the **raw request body**, not an arbitrary re-serialized JSON object.

---

# 20. Webhook Idempotency

Suppose provider sends:

```text
evt_001
```

three times:

```text
Request 1 -> evt_001
Request 2 -> evt_001
Request 3 -> evt_001
```

Desired result:

```text
WebhookEvents
--------------------------
evt_001
```

not:

```text
evt_001
evt_001
evt_001
```

And:

```text
Payment
--------------------------
Succeeded
```

only once logically.

---

# 21. The Concurrency Problem

This is where the project becomes much more interesting.

Imagine:

```text
Request A                     Request B

evt_001                       evt_001
   |                              |
   v                              v
Check event                     Check event
   |                              |
Not found                       Not found
   |                              |
   v                              v
Process                         Process
```

Both requests can potentially process the same event.

Therefore:

```text
if (!exists)
{
    process();
}
```

is **not enough**.

---

# 22. Correct Idempotency Strategy

Use multiple layers.

### Layer 1 — Application check

```text
Does event already exist?
```

### Layer 2 — Database unique constraint

```text
UNIQUE(Provider, ProviderEventId)
```

### Layer 3 — SQL transaction

Process related state changes atomically.

### Layer 4 — State transition validation

Prevent:

```text
Succeeded -> Succeeded
```

from performing destructive side effects again.

### Layer 5 — Database concurrency control

Use:

```text
rowversion
```

where appropriate.

---

# 23. Webhook Transaction

A simplified transaction:

```text
BEGIN TRANSACTION

Insert WebhookEvent
        |
        v
Update Payment
        |
        v
Update Order
        |
        v
Insert domain/outbox events
        |
        v
COMMIT
```

If anything fails:

```text
ROLLBACK
```

Therefore you don't end up with:

```text
Payment = Succeeded
Order = Pending
```

because only half of the operation succeeded.

---

# 24. Important Failure Scenario

Imagine:

```text
Payment update succeeds

Order update succeeds

Application crashes

Transaction never commits
```

SQL Server rolls everything back.

Result:

```text
Payment = Pending
Order = Pending
WebhookEvent = not processed
```

Provider retries:

```text
evt_123
```

Your application processes it again.

This is exactly what you want.

---

# 25. State Machine

Implement explicit state transitions.

Example:

```text
Pending
   |
   +----> Processing
               |
               +----> Succeeded
               |
               +----> Failed
```

Invalid:

```text
Succeeded -> Pending
Succeeded -> Processing
```

For example:

```csharp
payment.MarkSucceeded();
```

Inside the domain:

```csharp
if (Status == PaymentStatus.Succeeded)
{
    return;
}
```

Or throw an invalid state transition exception depending on your design.

---

# 26. Don't Confuse Idempotency With State

These are different concepts.

### Idempotency

Means:

> Processing the same operation multiple times produces the same final result.

### State machine

Means:

> Only valid state transitions are allowed.

You want both.

Example:

```text
Webhook #1
payment.Pending -> Succeeded

Webhook #2
payment.Succeeded -> Succeeded

Webhook #3
payment.Succeeded -> Succeeded
```

The final state remains:

```text
Succeeded
```

---

# 27. Payment Webhook Handler

Use CQRS:

```text
StripeWebhookController
        |
        v
ProcessStripeWebhookCommand
        |
        v
Handler
        |
        +---- Verify event
        |
        +---- Check idempotency
        |
        +---- Begin transaction
        |
        +---- Update Payment
        |
        +---- Update Order
        |
        +---- Record WebhookEvent
        |
        +---- Commit
```

---

# 28. Supported Webhook Events

Start with:

```text
payment_intent.succeeded
payment_intent.payment_failed
checkout.session.completed
```

Later:

```text
payment_intent.processing
charge.refunded
charge.dispute.created
```

Your application should map provider events into your own domain concepts.

For example:

```text
Stripe:
payment_intent.succeeded

        ↓

Application:
PaymentSucceeded
```

Your Domain should not depend directly on Stripe terminology.

---

# 29. Payment Lifecycle

Design a complete lifecycle:

```text
Order Created
     |
     v
Payment Pending
     |
     v
Checkout Created
     |
     v
Customer Pays
     |
     v
Provider Processing
     |
     v
Webhook Received
     |
     v
Signature Verified
     |
     v
Event Deduplicated
     |
     v
Payment Succeeded
     |
     v
Order Paid
```

Failure:

```text
Customer Pays
     |
     v
Payment Failed
     |
     v
Order PaymentFailed
```

Refund:

```text
Paid
 |
 v
Refund Requested
 |
 v
Refund Processing
 |
 v
Refunded
```

---

# 30. Database Schema

Initial schema:

```text
Users
----------------
Id
Email
...


Courses
----------------
Id
Title
Price
...


Orders
----------------
Id
UserId
OrderNumber
Status
Currency
Subtotal
Total
CreatedAt
UpdatedAt


OrderItems
----------------
Id
OrderId
CourseId
UnitPrice
Quantity
TotalPrice


Payments
----------------
Id
OrderId
Provider
ProviderPaymentId
Amount
Currency
Status
CreatedAt
UpdatedAt


WebhookEvents
----------------
Id
Provider
ProviderEventId
EventType
Status
ReceivedAt
ProcessedAt
Payload
Error


IdempotencyRequests
----------------
Id
UserId
Key
RequestHash
ResponseStatus
ResponseBody
CreatedAt
ExpiresAt
```

---

# 31. EF Core Relationships

```text
User
 |
 +---- Orders
          |
          +---- OrderItems
          |
          +---- Payments
```

And:

```text
Course
   |
   +---- OrderItems
```

Webhook events are independent:

```text
WebhookEvent
```

because they represent external messages rather than your core domain relationship.

---

# 32. SQL Server Indexes

Important indexes:

```text
Orders
-----
IX_Orders_UserId
UX_Orders_OrderNumber


OrderItems
----------
IX_OrderItems_OrderId
IX_OrderItems_CourseId


Payments
--------
IX_Payments_OrderId
UX_Payments_Provider_ProviderPaymentId


WebhookEvents
-------------
UX_WebhookEvents_Provider_ProviderEventId
IX_WebhookEvents_Status
IX_WebhookEvents_ReceivedAt


IdempotencyRequests
-------------------
UX_IdempotencyRequests_UserId_Key
IX_IdempotencyRequests_ExpiresAt
```

---

# 33. SQL Server Concurrency

Learn:

```text
Transactions
Isolation levels
Locks
Deadlocks
rowversion
Optimistic concurrency
Unique indexes
UPDLOCK
HOLDLOCK
```

Especially understand SQL Server's default isolation level:

```text
READ COMMITTED
```

Then experiment with:

```text
READ COMMITTED
REPEATABLE READ
SERIALIZABLE
SNAPSHOT
```

Don't simply memorize them.

Create concurrent requests and observe the behavior.

---

# 34. Race Condition Experiment

Create two simultaneous webhook requests:

```text
Request A -> evt_123
Request B -> evt_123
```

Make both execute concurrently.

Test:

```csharp
Task.WhenAll(
    ProcessWebhook("evt_123"),
    ProcessWebhook("evt_123")
);
```

Observe:

```text
Without unique constraint:
    possible duplicate processing

With unique constraint:
    one insert succeeds
    another gets duplicate-key violation
```

Then make the handler correctly handle the duplicate.

---

# 35. Duplicate Webhook Handling

A duplicate event should normally produce:

```http
200 OK
```

not:

```http
500
```

Because from the provider's perspective:

```text
Your system already processed it.
```

Example:

```text
evt_123 already processed

        ↓

No-op

        ↓

200 OK
```

This prevents unnecessary webhook retries.

---

# 36. Webhook Ordering

Do not assume events always arrive in the order you expect.

You may receive:

```text
payment.succeeded
```

before another related event.

Or:

```text
event B
event A
```

instead of:

```text
event A
event B
```

Therefore your application should be resilient to event ordering.

Store:

```text
ProviderEventId
EventType
CreatedAt
ReceivedAt
```

and design state transitions carefully.

---

# 37. Outbox Pattern — Advanced Phase

Eventually introduce:

```text
OutboxMessages
-----------------------
Id
EventType
Payload
CreatedAt
ProcessedAt
RetryCount
Error
```

Webhook transaction:

```text
BEGIN

Payment = Succeeded
Order = Paid

OutboxMessage:
    OrderPaid

COMMIT
```

Then:

```text
Background Worker
       |
       v
Read OutboxMessages
       |
       v
Publish event
       |
       v
Mark processed
```

This solves the problem:

```text
Database transaction succeeded
but
message publishing failed
```

---

# 38. Event-Driven Architecture

After implementing Outbox:

```text
Webhook
   |
   v
Payment
   |
   v
Order Paid
   |
   v
Outbox
   |
   v
Message Broker
   |
   +------> Email Service
   |
   +------> Notification Service
   |
   +------> Analytics
   |
   +------> Course Access
```

You can later use:

```text
RabbitMQ
MassTransit
Kafka
```

But don't start with them.

First make the SQL Server transaction and idempotency correct.

---

# 39. Course Access

For a course platform:

```text
Order Paid
    |
    v
Create Enrollment
```

Important:

```text
Enrollment
----------------
Id
UserId
CourseId
OrderId
CreatedAt
```

Unique constraint:

```text
UNIQUE(UserId, CourseId)
```

Now duplicate webhook processing cannot create duplicate enrollment.

This gives you another layer of idempotency.

---

# 40. Important Invariant

Define business invariants.

For example:

```text
Paid Order
    =>
Successful Payment
```

and:

```text
Successful Payment
    =>
Order.Paid
```

and:

```text
Paid Course Order
    =>
Enrollment exists
```

and:

```text
One User + One Course
    =>
One Enrollment
```

These invariants make your architecture much easier to reason about.

---

# 41. Refund System

After payments work, implement:

```http
POST /api/orders/{orderId}/refund
```

Flow:

```text
Admin
 |
 v
Refund Request
 |
 v
Validate order
 |
 v
Validate payment
 |
 v
Call Payment Provider
 |
 v
Refund Webhook
 |
 v
Update Payment
 |
 v
Update Order
```

Statuses:

```text
Paid
 |
 v
RefundProcessing
 |
 v
Refunded
```

Do not simply mark the payment refunded before the provider confirms the refund.

---

# 42. Payment Security

Learn and implement:

### Never store:

```text
Card number
CVV
Full card details
```

Let the payment provider handle sensitive card data.

Your system should store identifiers such as:

```text
PaymentIntentId
CheckoutSessionId
CustomerId
```

Also:

```text
Verify webhook signatures
Use HTTPS
Validate amounts
Validate currency
Validate order ownership
Never trust client price
```

---

# 43. Never Trust the Client Price

Bad:

```json
{
    "courseId": "...",
    "price": 1
}
```

Your server should retrieve:

```text
Course.Price
```

from SQL Server.

Then:

```text
Server Price
      |
      v
OrderItem.UnitPrice
      |
      v
Order.Total
      |
      v
Payment Provider
```

The client should never decide the final payment amount.

---

# 44. Authorization

Implement:

```text
Student
Admin
Professor
```

Example:

```text
Student
    |
    +---- Create checkout
    +---- View own orders
    +---- View own payments

Admin
    |
    +---- View all orders
    +---- Refund
    +---- View payment history
```

Always verify ownership:

```text
User A cannot access User B's order.
```

---

# 45. API Endpoints

## Orders

```http
POST   /api/orders
GET    /api/orders
GET    /api/orders/{id}
POST   /api/orders/{id}/cancel
```

## Checkout

```http
POST /api/orders/{id}/checkout
```

Response:

```json
{
    "orderId": "...",
    "checkoutUrl": "...",
    "expiresAt": "..."
}
```

## Payments

```http
GET /api/orders/{id}/payment
GET /api/payments/{id}
```

## Webhooks

```http
POST /api/webhooks/stripe
```

## Refunds

```http
POST /api/payments/{id}/refund
```

---

# 46. CQRS Structure

Example:

```text
Features/
│
├── Orders/
│   ├── Commands/
│   │   ├── CreateOrder/
│   │   └── CancelOrder/
│   │
│   └── Queries/
│       ├── GetOrder/
│       └── GetOrders/
│
├── Payments/
│   ├── Commands/
│   │   ├── CreateCheckout/
│   │   └── RefundPayment/
│   │
│   └── Queries/
│       ├── GetPayment/
│       └── GetPaymentHistory/
│
└── Webhooks/
    └── Commands/
        └── ProcessStripeWebhook/
```

---

# 47. Validation

Use FluentValidation.

Example:

```text
CreateCheckoutCommandValidator

- OrderId required
- User must own order
- Order must be Pending
- Order must contain items
- Total must be > 0
- Currency must be supported
```

Webhook validation is different because it requires:

```text
Signature verification
Event parsing
Provider validation
```

---

# 48. Exception Handling

Create global exception middleware.

Handle:

```text
ValidationException
NotFoundException
UnauthorizedException
ForbiddenException
ConflictException
InvalidStateTransitionException
```

For duplicate idempotency:

```text
Do not expose SQL Server's raw exception.
```

Map it to a meaningful application result.

---

# 49. Logging

Log:

```text
OrderId
PaymentId
ProviderPaymentId
WebhookEventId
Provider
EventType
UserId
CorrelationId
```

Example:

```text
Payment webhook received

OrderId=123
PaymentId=456
ProviderEventId=evt_abc
EventType=payment_intent.succeeded
```

Never log:

```text
Card number
CVV
Secrets
Webhook signing secret
Access tokens
```

---

# 50. Correlation ID

Introduce:

```text
X-Correlation-ID
```

Example:

```text
HTTP Request
      |
      v
CorrelationId = abc-123
      |
      +---- logs
      |
      +---- payment
      |
      +---- webhook
      |
      +---- database logs
```

This becomes extremely useful when debugging payment failures.

---

# 51. Testing Strategy

This project should have a strong test suite.

## Unit Tests

Test:

```text
Payment state transitions
Order state transitions
Price calculations
Idempotency logic
Validation
Refund rules
```

Example:

```text
Pending -> Succeeded = valid

Succeeded -> Pending = invalid
```

---

# 52. Integration Tests

Use SQL Server test database/container.

Test:

```text
Create order
Create checkout
Process webhook
Update payment
Update order
```

Verify database state.

---

# 53. Concurrency Tests

This is one of the most valuable parts of the project.

Run:

```text
100 concurrent webhook requests
```

all with:

```text
ProviderEventId = evt_123
```

Expected:

```text
WebhookEvents = 1
Payment = Succeeded
Order = Paid
Enrollment = 1
```

Not:

```text
WebhookEvents = 100
Enrollment = 100
```

---

# 54. Failure Injection

Intentionally make the system fail.

For example:

```text
Update Payment
     |
     X
Throw Exception
```

Verify:

```text
Payment unchanged
Order unchanged
WebhookEvent not committed
```

Then retry the webhook.

It should succeed.

---

# 55. Crash Simulation

Simulate:

```text
Webhook received
       |
       v
Payment update
       |
       X
Application crashes
```

Restart application.

Provider sends webhook again.

Expected:

```text
Correct final state
```

This teaches you much more than simply testing the happy path.

---

# 56. Deadlock Experiment

Create two transactions:

```text
Transaction A

Lock Order
   |
   v
Lock Payment
```

and:

```text
Transaction B

Lock Payment
   |
   v
Lock Order
```

This can produce:

```text
Deadlock
```

Learn how SQL Server detects and resolves deadlocks.

Then establish a consistent locking/order strategy.

---

# 57. Transaction Isolation Experiments

Create tests for:

```text
READ COMMITTED
REPEATABLE READ
SERIALIZABLE
SNAPSHOT
```

Understand:

```text
Dirty reads
Non-repeatable reads
Phantom reads
Lost updates
```

This is extremely valuable backend knowledge.

---

# 58. Redis — Phase 2

After the SQL Server version is correct, introduce Redis.

Use Redis for:

```text
Idempotency cache
Distributed locks
Rate limiting
Payment status cache
```

But remember:

> Redis should not be your source of truth for financial state.

SQL Server remains authoritative.

```text
Redis
  |
  | cache
  v
Application
  |
  v
SQL Server
  |
  | source of truth
```

---

# 59. Background Worker — Phase 3

Introduce:

```csharp
BackgroundService
```

for:

```text
Webhook retry
Outbox processing
Expired checkout sessions
Expired idempotency records
Payment reconciliation
```

Example:

```text
Background Worker
      |
      v
Find failed outbox messages
      |
      v
Retry
      |
      v
Success
      |
      v
Mark processed
```

---

# 60. Payment Reconciliation

This is a very valuable advanced feature.

Sometimes:

```text
Stripe says:
Payment = succeeded

Your DB says:
Payment = pending
```

Create a reconciliation job:

```text
Every 10 minutes

Find:
Payments stuck in Pending/Processing

        |

Query payment provider

        |

Compare states

        |

Repair inconsistent state
```

This teaches you that distributed systems can temporarily become inconsistent.

---

# 61. Monitoring

Add:

```text
Health checks
Structured logging
Metrics
Tracing
```

Track metrics such as:

```text
checkout.success
checkout.failed
payment.success
payment.failed
webhook.received
webhook.duplicate
webhook.failed
refund.success
refund.failed
```

Later use:

```text
OpenTelemetry
Prometheus
Grafana
```

---

# 62. Docker

Containerize:

```text
Payment API
SQL Server
Redis
RabbitMQ
```

Eventually:

```text
docker-compose
```

with:

```text
api
sqlserver
redis
rabbitmq
```

---

# 63. Development Phases

## Phase 1 — Basic E-Commerce

Implement:

```text
Users
Courses
Orders
OrderItems
```

Features:

```text
Create Order
Get Orders
Get Order
Cancel Order
```

Goal:

> Understand order modeling.

---

## Phase 2 — Payment Domain

Implement:

```text
Payment
PaymentAttempt
PaymentStatus
OrderStatus
```

Implement state transitions.

Goal:

> Learn payment state machines.

---

## Phase 3 — Payment Provider

Integrate Stripe.

Implement:

```text
Create Checkout Session
Return Checkout URL
```

Goal:

> Understand external payment providers.

---

## Phase 4 — Webhooks

Implement:

```http
POST /api/webhooks/stripe
```

Add:

```text
Signature verification
Event parsing
Payment update
Order update
```

Goal:

> Understand asynchronous external events.

---

## Phase 5 — Idempotency

Implement:

```text
WebhookEvent
IdempotencyRequest
Unique indexes
```

Test:

```text
Same webhook 1x
Same webhook 3x
Same webhook 100x
```

Goal:

> Understand idempotent systems.

---

## Phase 6 — Concurrency

Run concurrent requests.

Test:

```text
10 requests
100 requests
1000 requests
```

Learn:

```text
Race conditions
Transactions
Locks
Isolation
Optimistic concurrency
Unique constraints
```

Goal:

> Understand why "check then insert" isn't sufficient.

---

## Phase 7 — Failure Handling

Simulate:

```text
Database failure
Stripe timeout
Stripe unavailable
Application crash
Webhook duplicate
Webhook out-of-order
Transaction rollback
Deadlock
```

Goal:

> Design for failure rather than only success.

---

## Phase 8 — Outbox

Add:

```text
OutboxMessages
BackgroundService
Retry policy
```

Goal:

> Understand reliable event publishing.

---

## Phase 9 — Refunds

Implement:

```text
Refund
Refund webhook
Refund state machine
```

Goal:

> Understand reverse financial flows.

---

## Phase 10 — Reconciliation

Implement:

```text
Payment reconciliation worker
```

Goal:

> Handle inconsistencies between your database and external provider.

---

## Phase 11 — Redis

Add:

```text
Caching
Distributed locking
Rate limiting
Idempotency optimization
```

Goal:

> Understand distributed infrastructure.

---

## Phase 12 — Messaging

Add:

```text
RabbitMQ
MassTransit
```

Architecture:

```text
Payment API
     |
     v
Outbox
     |
     v
RabbitMQ
     |
     +------> Notification
     |
     +------> Enrollment
     |
     +------> Analytics
```

Goal:

> Understand event-driven architecture.

---

# 64. Final Architecture

Your final version can look like:

```text
                         Client
                           |
                           v
                    ASP.NET Core API
                           |
              +------------+-------------+
              |                          |
              v                          v
           Orders                     Checkout
              |                          |
              |                          v
              |                    Payment Provider
              |                          |
              |                          v
              |                       Webhook
              |                          |
              +------------+-------------+
                           |
                           v
                    Payment Domain
                           |
                           v
                       SQL Server
                           |
                           v
                         Outbox
                           |
                           v
                       RabbitMQ
                           |
              +------------+-------------+
              |            |             |
              v            v             v
         Enrollment    Notification   Analytics
              |
              v
           Courses

                    Redis
                      |
          +-----------+-----------+
          |           |           |
        Cache      Rate Limit   Lock
```

---

# 65. Most Important Things to Learn

Don't measure the success of this project by:

```text
"Can I create a Stripe checkout?"
```

Measure it by whether you can answer these questions.

### Question 1

What happens if the webhook arrives twice?

```text
Idempotency
```

### Question 2

What happens if two webhook requests arrive simultaneously?

```text
Concurrency + unique constraints + transactions
```

### Question 3

What happens if the application crashes after updating the payment?

```text
Transactions + retry
```

### Question 4

What happens if SQL Server succeeds but RabbitMQ fails?

```text
Outbox pattern
```

### Question 5

What happens if Stripe says succeeded but your database says pending?

```text
Reconciliation
```

### Question 6

What happens if events arrive out of order?

```text
State machine + event handling strategy
```

### Question 7

What happens if the client sends the same checkout request twice?

```text
API idempotency key
```

### Question 8

What happens if the course price changes after an order is created?

```text
Snapshot OrderItem.UnitPrice
```

### Question 9

What happens if two requests attempt to enroll the same student?

```text
UNIQUE(UserId, CourseId)
```

### Question 10

Can the client tell your API that payment succeeded?

```text
No.
```

The provider's verified event is authoritative.

---

# 66. Final Learning Outcome

After completing this project properly, you should understand much more than payment APIs.

You will have practiced:

```text
ASP.NET Core
        |
        +-- Clean Architecture
        +-- CQRS
        +-- MediatR
        +-- EF Core
        +-- SQL Server
        +-- Transactions
        +-- Isolation levels
        +-- Locks
        +-- Concurrency
        +-- Optimistic concurrency
        +-- Database constraints
        +-- Idempotency
        +-- State machines
        +-- Webhooks
        +-- External APIs
        +-- Retry strategies
        +-- Outbox pattern
        +-- Background workers
        +-- Redis
        +-- Message brokers
        +-- Event-driven architecture
        +-- Observability
        +-- Distributed systems
```

The most valuable part of the project is **not the Stripe SDK**.

The most valuable part is making this guarantee:

```text
                    100 duplicate webhooks
                              |
                              v
                       ┌─────────────┐
                       │ SQL Server  │
                       └──────┬──────┘
                              |
              ┌───────────────┼───────────────┐
              v               v               v
          Payment          Order          Enrollment
          Succeeded         Paid               1
              |               |                |
              └───────────────┴────────────────┘
                              |
                              v
                     Correct final state
```

That is the part that turns this from a **CRUD project with Stripe** into a genuine **backend engineering project**.