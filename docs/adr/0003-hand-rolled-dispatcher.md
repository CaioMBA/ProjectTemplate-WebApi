# 3. The CQRS dispatcher is hand-rolled

Status: Accepted

## Context

MediatR is the default choice for a CQRS dispatcher in .NET, but has required a commercial
licence above a revenue threshold since v13 - the same problem as AutoMapper (ADR 2).

The parts of MediatR this template actually needs are a request-to-handler map and a
composable behaviour pipeline. That is roughly 150 lines.

## Decision

`Application/Dispatching/Sender.cs` implements `Domain.Interfaces.Messaging.ISender`:

- Resolves `IRequestHandler<TRequest, TResponse>` from the container.
- Resolves every registered `IPipelineBehavior<TRequest, TResponse>`.
- Folds the behaviours around the handler in **registration order**.
- Caches the reflection needed to close the open generics, once per request type.

## Behaviour order

Registration order is execution order. The outermost behaviour runs first:

```
Telemetry -> Logging -> Validation -> Idempotency -> Caching -> Transaction -> Handler
```

The ordering is load-bearing, not incidental:

- **Validation before Transaction** - a rejected request never opens a database
  transaction or takes a connection from the pool.
- **Idempotency before Transaction** - a replayed request short-circuits without touching
  the database.
- **Caching before Transaction** - a cache hit never opens a transaction.
- **Transaction innermost** - the narrowest possible boundary, held for the shortest time.

A unit test pins this order. Folding the behaviours in the wrong direction would put the
transaction *outside* validation, and nothing else would fail visibly.

## Consequences

- No licence risk, and no dependency on a library's upgrade cadence.
- The pipeline is debuggable: stepping into `Send` reaches real code, not a library
  internal.
- New behaviours are added by implementing one interface and registering it.
- Features MediatR offers that this does not (notifications, streaming, request
  pre/post-processors) must be added if they are ever needed.
