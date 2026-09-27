# 2. Mapster replaces AutoMapper

Status: Accepted

## Context

`RestAPI-ProjectBase` references **AutoMapper 15**, which has required a commercial licence
above a revenue threshold since v15. It registers two mapping profiles, injects
`IMapper` - and both profiles are completely empty. The dependency is pure liability: paid
for, and doing nothing.

A template seeds many projects at once, so a licensing landmine multiplies across every one
of them.

## Decision

**Mapster** (MIT) is the only mapper. Configurations live in `Domain/Mappings` as
`IRegister` implementations and are registered by `Domain/Setup/AddMapsterSetup.cs`.

The same reasoning excludes, throughout this template:

| Rejected | Reason | Replacement |
| --- | --- | --- |
| AutoMapper v15+ | Commercial licence | Mapster |
| MediatR v13+ | Commercial licence | Hand-rolled dispatcher (ADR 3) |
| MassTransit v9+ | Commercial licence | `RabbitMQ.Client` / `Confluent.Kafka` directly |
| FluentAssertions v8+ | Commercial licence | Shouldly |

`ArchitectureTests` assert that no project references AutoMapper or MediatR, so the
decision cannot quietly erode.

## Notes

Mapster's version numbers track the .NET release train, so **10.0.x is correct for .NET 10**
and is not a typo. `AddMapsterSetup` compiles every mapping eagerly at startup, so a
misconfigured mapping fails at boot rather than on a live request; a unit test asserts that
compilation succeeds.
