# 4. No cache abstraction - `IDistributedCache` directly

Status: Accepted

## Context

The usual pattern is a custom `ICacheService` wrapping whichever cache is in use. It looks
like good design and is almost always a net loss: it reimplements an interface the
framework already provides, and it hides which provider is actually running.

## Decision

Consumers inject **`Microsoft.Extensions.Caching.Distributed.IDistributedCache`** directly.
There is no `ICacheService` in this template.

`Data.Cache` registers one of two providers behind that single framework interface:

| `Settings:Caches[].Type` | Registration | Scope |
| --- | --- | --- |
| `Memory` | `AddDistributedMemoryCache()` | Per instance |
| `Redis` | `AddStackExchangeRedisCache()` | Shared across replicas |

Switching providers changes one configuration value and no code: the `Type` of the `Settings:Caches` entry a request names (`ICacheRequest.CacheId`) picks the store, and that entry's `DefaultTtlMinutes` is the default TTL (ADR-0016, ADR-0017). Every cache entry is a keyed `IDistributedCache`; there is no unkeyed one.

## The two gaps, and how they are handled

`IDistributedCache` is byte-array based and has no stampede protection.

**Typed access** - `Domain/Extensions/DistributedCacheExtensions.cs` provides
`GetValueAsync<T>`, `SetValueAsync<T>`, `GetOrCreateAsync<T>` and `BuildKey`. These are
**extension methods on the framework interface**, not a wrapper type, so nothing new enters
the dependency graph. They serialise through the single shared `JsonDefaults.Standard`
instance rather than allocating `JsonSerializerOptions` per call, which both reference
repositories did.

**Stampede protection** - `Application/Behaviors/CachingBehavior.cs` holds a
`ConcurrentDictionary<string, SemaphoreSlim>`. Concurrent misses on the same key serialise;
the winner populates the cache and the rest read through. Semaphores are removed once their
wait count reaches zero, so a high-cardinality key space cannot grow the map without bound.

**Documented limit:** this is per-instance, not cross-replica. Two pods cold-starting can
each run the same query once. With `replicas: 1` in the deployment convention that is
currently moot; scaling out is the trigger to revisit.

## Alternative considered

`HybridCache` (`Microsoft.Extensions.Caching.Hybrid`) is native .NET and provides L1+L2,
stampede protection and typed get-or-create out of the box. It was rejected for this
template because the requirement was explicitly "no custom cache abstractions, use native
`IDistributedCache`", and `HybridCache` introduces a second cache interface alongside it.
It remains the better answer for a service with heavy read amplification.
