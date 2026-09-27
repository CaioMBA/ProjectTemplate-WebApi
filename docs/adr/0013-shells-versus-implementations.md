# 13. Shells versus implementations

Status: Accepted

Amended by ADR-0017: there are no module switches and no unkeyed `IDistributedCache` any more; the fallback discussed below was removed with them.

## Context

An audit of this repository found several components that were structurally complete and
functionally empty. Every architecture test passed on all of them.

The clearest case was `Data.GrpcApi`. It had a project, a module, a `Setup` folder, a
registered `IGrpcApiClient`, package references and a `Protobuf` glob. `ConventionTests`,
`LayerDependencyTests` and `ProjectReferenceTests` were all green. But `GetClient<T>` called
`IServiceProvider.GetService<TClient>()` against a container where no `TClient` was ever
registered, so the method had two outcomes: throw on a protocol mismatch, or throw because
nothing resolved. There was no success path, the `Protos` folder was empty, and no file in
the project referenced a single `Grpc.*` type.

The template's architecture tests verify *shape*: that a project sits in the right folder,
that its namespaces follow the convention, that it does not reference a layer it should not.
An empty shell satisfies all of that. Shape tests cannot distinguish scaffolding from
working code, and reading the folder listing made the module look finished.

Three further defects had the same signature - present, registered, and never executed:

- `ITransactionalRequest` had no implementation, so the `switch` arm in `TransactionBehavior`
  that branches on it was unreachable.
- `CacheConnectionModel.DefaultTtlMinutes` was bound from configuration and never read;
  `DistributedCacheExtensions` hard-coded 60 minutes, which happened to equal the default,
  so changing the setting appeared to work and did nothing.
- Disabling `Modules:Cache` removed the only `IDistributedCache` registration while
  `CachingBehavior`, `IdempotencyBehavior` and `CachedPageStreamer` still required it, so a
  cache toggle broke every dispatched request.

And two were only found by calling the code rather than compiling it:

- `Result<T>` has no parameterless constructor. `CachingBehavior` serialises a response on a
  cache miss and deserialises it on a hit, so the *second* identical request to any cacheable
  query threw `NotSupportedException`, which `GlobalExceptionHandler` maps to 501. Every
  earlier test created a fresh entity, so no test ever requested the same key twice.
- `GraphQL.Client`'s `SystemTextJsonSerializer` mutates the `JsonSerializerOptions` it is
  given. `JsonDefaults.Standard` is read-only, so constructing the GraphQL client threw on
  every real call.

## Decision

Shape tests are kept, and behaviour gates are added alongside them.

1. **Container resolution tests** (`Tests/UnitTests/Modules/ContainerResolutionTests.cs`)
   build the real container per module combination and resolve the services that must work.
   This is the gate that catches an unwired dependency, and it fails on the pre-fix code.

2. **Dead code tests** (`Tests/ArchitectureTests/DeadCodeTests.cs`) assert that every
   interface under `Domain.Interfaces` has an implementation, that every pipeline marker
   interface is applied to at least one request, and that enums remain bindable.

3. **A module is not complete until an integration test drives it against something real.**
   `Data.GrpcApi` is now covered by a test that starts a Kestrel gRPC server on a loopback
   port and asserts a round-trip, the propagated authorization header, an applied deadline,
   and a `DeadlineExceeded` status on a slow call.

4. **Round-trip anything that crosses a serialisation boundary.** `ResultJsonConverter`
   exists because `Result<T>` is cached as JSON; it is covered by explicit round-trip tests
   rather than being assumed to work.

## Consequences

A new infrastructure module is not finished when it compiles and the architecture tests
pass. It is finished when something calls it.

The cheapest single gate is the container resolution test: it would have caught the cache
dependency break, the gRPC shell and an earlier duplicate-registration bug on its own.

`CachingBehavior` now also evicts keys for requests implementing `ICacheInvalidatingRequest`.
A read cache with no write invalidation is a correctness bug, not a performance tradeoff, and
the template should not teach it.
