# 6. Authentication is deferred, but the seams exist

Status: Accepted

## Context

Authentication was explicitly out of scope for this template. Retrofitting it later is
expensive if the pipeline and the consuming code have to be rewritten to accommodate it.

## Decision

The template is **auth-ready, not auth-implemented**:

- `Domain.Interfaces.Identity.ICurrentUser` is defined and consumed today by the auditing
  interceptor, the logging behaviour and the idempotency behaviour.
- `CrossCutting.Services.HttpContextCurrentUser` implements it by reading
  `IHttpContextAccessor.HttpContext.User`. Nothing populates that principal yet, so it
  reports an unauthenticated caller - and starts returning real values the moment an
  authentication handler is registered, with no edit.
- `app.UseAuthentication()` and `app.UseAuthorization()` sit in the correct pipeline
  position in `Program.cs`, after `UseRouting`/`UseCors`/`UseRateLimiter` and before the
  endpoint mappings.
- `AddSwaggerSetup` already declares the `Bearer` security scheme, so the UI has an
  Authorize button ready.

`ICurrentUser` is deliberately expressed without `ClaimsPrincipal`, so `Domain` needs no
reference to the ASP.NET Core shared framework.

## Intended direction when it is picked up

A **resource server** consuming tokens issued by `API-IdentityAuthorizationHub` - not a
second token issuer:

1. `AddJwtBearer` validating the Hub's tokens.
2. Optional cached introspection against the Hub for real revocation, since a stateless JWT
   cannot be revoked before expiry.
3. Permission-based policies with requirements and handlers. The Hub has
   `AddAuthorizationBuilder()` with zero policies registered, so authorisation there is
   effectively authentication only.
4. `IClaimsTransformation` to map Hub claims onto local permissions.

## Lessons from the Hub worth carrying

Genuinely good and worth reusing: app-scoped roles and claims with a `ReservedClaimTypes`
allow-list blocking claim-based privilege escalation; hashed and rotated refresh tokens; a
server-side session table enabling real revocation.

Do **not** carry: lockout configured but never enforced (`CheckPasswordAsync` bypasses
`SignInManager`, so the lockout counter never increments); HS256 with a committed signing
secret and no key rotation; no refresh-token reuse detection; IP-bound tokens combined with
`ForwardedHeaders.All` and `KnownProxies.Clear()`, which makes the binding spoofable by any
caller.
