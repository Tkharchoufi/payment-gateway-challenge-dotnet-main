# Payment Gateway — solution notes

## API

**`POST /api/Payments`**

```json
{
  "cardNumber": "2222405343248877",
  "expiryMonth": 4,
  "expiryYear": 2030,
  "currency": "GBP",
  "amount": 100,
  "cvv": "123"
}
```

| Result | Status | Body |
| --- | --- | --- |
| Authorized | `200` | payment, `"status": "Authorized"` |
| Declined | `200` | payment, `"status": "Declined"` |
| Rejected | `400` | `"status": "Rejected"` and every validation error |
| Bank gave no answer | `502` | problem details |

**`GET /api/Payments/{id}`** — `200` with the payment, or `404`.

## Decisions

The brief documents the simulator's behaviour precisely: odd-ending card numbers
authorize, even-ending ones decline, and ones ending in `0` return `503 Service
Unavailable`. What it does not say is which *payment status* that 503 becomes. The
response table lists only `Authorized` and `Declined`, and `Rejected` is defined as
invalid information that never reached the bank. The 503 is none of the three, and
deciding what to do with it is the most consequential choice here.

It is **not** `Declined`. A decline is an answer — the bank was asked and said no. A 503,
a timeout, or an unreadable body is the *absence* of an answer. Telling a merchant
"declined" when the bank never declined anything is a false statement about the shopper's
money, and if the request did reach the bank, the gateway has now denied a payment that
may have been taken.

So the client draws a hard line. `AcquiringBankResponse` carries a decline; everything
else — unreachable, timed out, non-2xx, malformed body, missing `authorized` field —
raises `AcquiringBankUnavailableException`. That surfaces as `502` with an explicit
statement that no payment was recorded and that retrying may duplicate the authorization.

Two consequences fall out of this:

- **Nothing is stored until the bank has answered.** The `Add` call sits after the
  `await`, so a failed call cannot leave a half-finished payment for a merchant to find
  later. `PaymentsServiceTests.RecordsNothingWhenTheBankGivesNoAnswer` asserts it.
- **A 200 with a body we cannot parse is a failure, not a decline.** Deserialising to
  `default(bool)` would silently turn "we don't know" into "no". `required` members on
  the response record make it a hard failure instead.
- **The bank's own `400` is treated the same way.** The simulator returns it when a
  required field is missing, which validation should make impossible — so receiving one
  means the gateway built a bad request. That is a defect on our side, not a merchant
  error, so it is not passed through as a `400`; it becomes a `502` and is logged.

## Card data

The full card number exists only for the duration of the bank call. `Payment` — the unit
of storage — holds `CardNumberLastFour` and has no field capable of holding a PAN, so the
repository cannot become a leak even by accident. Two tests assert against the raw
response body rather than a deserialised object, because a typed assertion cannot see an
extra field that shouldn't be on the wire.

Card numbers are never logged. The bank-failure log records the exception, whose message
carries a status code, not a request.

## Observability

The question this has to answer is "what happened to *this* payment", so every
line written for one attempt shares a `PaymentId` logging scope.

The id is allocated **before** the bank is called, not after it answers. That is the
detail that makes the failure path traceable: when the bank gives no answer, no payment
is ever stored, and without an id assigned up front there would be nothing tying the
merchant's `502` to what the gateway actually attempted.

```
info: Authorizing 100 GBP with the acquiring bank.        PaymentId=3f2a…
info: Payment Authorized by the acquiring bank in 42ms.   PaymentId=3f2a…
```

- **Bank latency is recorded on both paths.** The success line carries it, and so does
  the failure line — "the bank timed out after 10,000ms" and "the bank refused the
  connection in 2ms" are different incidents with different causes, and a log that
  omits the duration cannot tell them apart.
- **Failures are logged once, in the frame that has the context.** `PaymentsService`
  logs the error with its latency and rethrows; the controller maps it to `502` without
  logging again. One incident, one error line.
- **Message templates, not interpolation**, so `PaymentId`, `PaymentStatus` and
  `ElapsedMilliseconds` survive as queryable fields in a structured sink rather than
  being flattened into prose.
- **Rejections are logged with their reasons.** Validator messages state the rule that
  failed and never echo the value that failed it, so this cannot become a side channel
  for card data.

**Card data is never logged, and that is a test rather than a comment.**
`NeverLogsCardDataWhenAPaymentSucceeds` and `NeverLogsCardDataWhenTheBankFails` capture
everything written — including scope state and the full exception — and assert the card
number, CVV and expiry are absent. The failure path is covered separately because an
exception is where secrets usually leak in. Both start with a positive control asserting
that something *was* logged, since otherwise they would pass against a logger that
silently writes nothing.

## Other decisions worth naming

**Validation is FluentValidation, and runs before anything outbound.** `Rejected` means
the bank was never called, so validation cannot live behind the network call. Each field
uses `Cascade(Stop)` so an omitted card number reports "is required" and not also the
digits rule — noise a merchant can't act on. Expiry spans two fields, so it's a
model-level rule reported against `ExpiryYear`, guarded to run only once month and year
are individually in range.

**Request fields are nullable.** This is the reason a missing `amount` can be rejected as
"Amount is required" rather than "must be greater than zero". The cost is that
`PaymentsService` reads them as non-null, documented as a precondition on the method. The
alternative — a second, validated model — is a lot of type for six fields.

**The repository is a `ConcurrentDictionary` behind an interface.** It's a singleton, so
concurrent requests genuinely race and `List<T>.Add` can lose writes or throw; losing the
record of an authorized payment is not a defect a gateway can carry. The interface exists
because a failed-bank-call test needs to assert that *nothing* was stored, and with a
concrete class there is no id to look up and nothing to observe.

**Typed `HttpClient` via `AddHttpClient`,** so handler lifetime and DNS refresh are
managed. The base URL is read at startup and throws there if absent — a misconfigured
gateway should fail on boot, not on a merchant's first payment. Timeout is 10s.

**Status serialises as a string.** The brief asks for `Authorized`, not `0`.

## Assumptions

- **Currencies are GBP, USD and EUR**, matched case-sensitively. The brief caps the set at
  three; ISO 4217 defines the codes in upper case.
- **A card is valid through the last day of its expiry month**, which is how card schemes
  treat it. A card expiring this month is accepted.
- **Amount must be greater than zero.** A zero-value authorization isn't something this
  gateway has a reason to support.
- **No merchant identity.** There is no authentication in the brief, so there is no
  ownership check on retrieval: any caller who knows an id can read that payment. In
  production this is the first thing that would need adding, and `GET` would be scoped to
  the authenticated merchant.
- **Storage is in-memory and does not survive a restart**, per the brief's note that the
  test double repository is sufficient.

  ## Deliberately not built

- **Idempotency keys.** The 502 path admits that a retry may duplicate an authorization.
  Fixing that properly means an idempotency key on the request and dedup on replay —
  worth doing, but it is a feature with its own design, not a detail of this one.
- **Retries or a circuit breaker.** Retrying a payment without idempotency risks charging
  twice, so this would be the wrong order to build them in.
- **A global exception handler.** With one endpoint, a `try`/`catch` in the action is
  clearer than the indirection. At a handful of endpoints, `IExceptionHandler` earns its
  place.
- **Metrics and distributed tracing.** Logging answers "what happened to this payment".
  Authorization rate and bank latency percentiles answer "is the gateway healthy", which
  is a different question and wants OpenTelemetry rather than more log lines.

## Notes on the starting template

Two things in the scaffold were wrong rather than merely incomplete, and both are fixed in
their own commits:

- `GET` wrapped a null lookup in `200 OK`, so the template's own
  `Returns404IfPaymentNotFound` test was **red on a fresh clone**.
- `PostPaymentRequest` asked the merchant for `CardNumberLastFour` rather than the card
  number the bank needs, and typed it and the CVV as `int`, which drops leading zeros —
  a CVV of `0123` binds as `123` and then fails a 3–4 digit rule.