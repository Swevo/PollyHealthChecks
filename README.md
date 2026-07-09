# PollyHealthChecks

<img src="icon.png" width="100" align="right" />

[![NuGet](https://img.shields.io/nuget/v/PollyHealthChecks.svg)](https://www.nuget.org/packages/PollyHealthChecks)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PollyHealthChecks.svg)](https://www.nuget.org/packages/PollyHealthChecks)
[![CI](https://github.com/Swevo/PollyHealthChecks/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/PollyHealthChecks/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**ASP.NET Core health checks for Polly v8 circuit breakers** — expose circuit-breaker state as `/health` endpoint responses so Kubernetes probes, load balancers, and monitoring dashboards can automatically react to your resilience state.

```csharp
var stateProvider = new CircuitBreakerStateProvider();

services.AddResiliencePipeline("payments-api", builder =>
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        StateProvider = stateProvider,
        FailureRatio = 0.5,
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30),
    }));

services.AddHealthChecks()
    .AddPollyCircuitBreaker("payments-api", stateProvider);  // ← one line
```

When the circuit opens, `/health` returns `Unhealthy` — Kubernetes stops routing traffic, zero manual intervention required.

---

## Why PollyHealthChecks?

"How do I expose my circuit breaker state in the ASP.NET Core health endpoint?" is one of the most-asked Polly questions. Without this package you must write your own `IHealthCheck`, wire up `CircuitBreakerStateProvider`, and map the four circuit states manually. PollyHealthChecks does all of that in a single method call.

| Without PollyHealthChecks | With PollyHealthChecks |
|---|---|
| Write a custom `IHealthCheck` per circuit | One `AddPollyCircuitBreaker()` call |
| Manually map all 4 circuit states | Built-in Closed→Healthy, HalfOpen→Degraded, Open→Unhealthy |
| Re-implement for every microservice | Shared package, consistent behaviour |
| Forget to update when you add circuits | Register alongside the pipeline |

---

## Installation

```bash
dotnet add package PollyHealthChecks
```

Targets **net6.0**, **net8.0**, and **net9.0**.

Dependencies: `Polly.Core 8.*`, `Microsoft.Extensions.Diagnostics.HealthChecks 8.*`

---

## Quick start

### 1. Attach a `CircuitBreakerStateProvider` to your pipeline

```csharp
using Polly.CircuitBreaker;
using PollyHealthChecks;

var stateProvider = new CircuitBreakerStateProvider();

services.AddResiliencePipeline("downstream-api", builder =>
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        StateProvider  = stateProvider,
        FailureRatio   = 0.5,
        SamplingDuration  = TimeSpan.FromSeconds(10),
        MinimumThroughput = 5,
        BreakDuration  = TimeSpan.FromSeconds(30),
    }));
```

### 2. Register the health check

```csharp
services.AddHealthChecks()
    .AddPollyCircuitBreaker("downstream-api", stateProvider);
```

### 3. Map the health endpoint

```csharp
app.MapHealthChecks("/health");
```

---

## State mapping

| Circuit state | Health status | Meaning |
|---|---|---|
| `Closed` | `Healthy` | Normal operation |
| `HalfOpen` | `Degraded` | Testing recovery — partial traffic |
| `Open` | `Unhealthy` (configurable) | Calls rejected — dependency down |
| `Isolated` | `Unhealthy` (configurable) | Manually isolated |

---

## Kubernetes liveness & readiness probes

Use **tags** to split circuit breaker health into separate liveness and readiness probes:

```csharp
services.AddHealthChecks()
    .AddPollyCircuitBreaker("payments-api",   paymentsStateProvider,  tags: ["ready"])
    .AddPollyCircuitBreaker("inventory-api",  inventoryStateProvider, tags: ["ready"])
    .AddPollyCircuitBreaker("auth-api",       authStateProvider,      tags: ["live", "ready"]);

// Liveness — just the critical auth circuit
app.MapHealthChecks("/health/live",  new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("live"),
});

// Readiness — all dependency circuits
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready"),
});
```

Kubernetes deployment:

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 10

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 5
  periodSeconds: 5
```

---

## Multiple circuit breakers

Monitor every downstream dependency independently:

```csharp
services.AddHealthChecks()
    .AddPollyCircuitBreaker("payments-api",   paymentsStateProvider)
    .AddPollyCircuitBreaker("inventory-api",  inventoryStateProvider, failureStatus: HealthStatus.Degraded)
    .AddPollyCircuitBreaker("auth-api",       authStateProvider,      tags: ["ready", "live"])
    .AddPollyCircuitBreaker("email-service",  emailStateProvider,     failureStatus: HealthStatus.Degraded);
```

---

## Custom failure status

Demote a non-critical circuit to `Degraded` so a single open circuit doesn't fail the entire readiness check:

```csharp
services.AddHealthChecks()
    // Critical — Unhealthy when open (default)
    .AddPollyCircuitBreaker("payments-api", paymentsStateProvider)
    // Non-critical — Degraded when open (app still serves traffic)
    .AddPollyCircuitBreaker("analytics-api", analyticsStateProvider,
        failureStatus: HealthStatus.Degraded);
```

---

## HealthChecks UI integration

Works out-of-the-box with [AspNetCore.HealthChecks.UI](https://github.com/Xabaril/AspNetCore.HealthChecks.UI):

```csharp
services.AddHealthChecksUI(opts =>
    opts.AddHealthCheckEndpoint("App", "/health"))
    .AddInMemoryStorage();

services.AddHealthChecks()
    .AddPollyCircuitBreaker("payments-api", paymentsStateProvider)
    .AddPollyCircuitBreaker("inventory-api", inventoryStateProvider);

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});
app.MapHealthChecksUI();
```

---

## Full ASP.NET Core example

```csharp
var builder = WebApplication.CreateBuilder(args);

var paymentsStateProvider  = new CircuitBreakerStateProvider();
var inventoryStateProvider = new CircuitBreakerStateProvider();

builder.Services.AddResiliencePipeline("payments-api", b =>
    b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        StateProvider     = paymentsStateProvider,
        FailureRatio      = 0.5,
        MinimumThroughput = 5,
        BreakDuration     = TimeSpan.FromSeconds(30),
    }));

builder.Services.AddResiliencePipeline("inventory-api", b =>
    b.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        StateProvider     = inventoryStateProvider,
        FailureRatio      = 0.5,
        MinimumThroughput = 5,
        BreakDuration     = TimeSpan.FromSeconds(30),
    }));

builder.Services.AddHealthChecks()
    .AddPollyCircuitBreaker("payments-api",   paymentsStateProvider,  tags: ["ready", "live"])
    .AddPollyCircuitBreaker("inventory-api",  inventoryStateProvider, tags: ["ready"]);

var app = builder.Build();
app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });
app.Run();
```

---

## Related packages

| Package | Downloads | Description |
|---|---|---|
| [PollyEFCore](https://www.nuget.org/packages/PollyEFCore) | [![Downloads](https://img.shields.io/nuget/dt/PollyEFCore.svg)](https://www.nuget.org/packages/PollyEFCore) | Polly v8 resilience for EF Core queries and SaveChanges |
| [PollyOpenAI](https://www.nuget.org/packages/PollyOpenAI) | [![Downloads](https://img.shields.io/nuget/dt/PollyOpenAI.svg)](https://www.nuget.org/packages/PollyOpenAI) | Polly v8 resilience for OpenAI and Azure OpenAI — retry on 429, Retry-After, circuit breaker |
| [PollyRedis](https://www.nuget.org/packages/PollyRedis) | [![Downloads](https://img.shields.io/nuget/dt/PollyRedis.svg)](https://www.nuget.org/packages/PollyRedis) | Polly v8 resilience for StackExchange.Redis — retry, circuit breaker, timeout |
| [PollySignalR](https://www.nuget.org/packages/PollySignalR) | [![Downloads](https://img.shields.io/nuget/dt/PollySignalR.svg)](https://www.nuget.org/packages/PollySignalR) | Polly v8 exponential back-off reconnect policy for SignalR HubConnection |
| [PollyGrpc](https://www.nuget.org/packages/PollyGrpc) | Polly v8 resilience (retry, CB, timeout) for gRPC .NET clients via Interceptor |
| [PollyKafka](https://www.nuget.org/packages/PollyKafka) | Polly v8 resilience (retry, CB, timeout) for Confluent.Kafka producers and consumers |
| [PollyAzureEventHub](https://github.com/Swevo/PollyAzureEventHub) | Polly v8 for Azure Event Hubs |
| [PollyAzureServiceBus](https://www.nuget.org/packages/PollyAzureServiceBus) | Polly v8 resilience (retry, CB, timeout) for Azure Service Bus senders and receivers |
| [PollyMediatR](https://www.nuget.org/packages/PollyMediatR) | [![Downloads](https://img.shields.io/nuget/dt/PollyMediatR.svg)](https://www.nuget.org/packages/PollyMediatR) | Polly v8 pipelines for MediatR request handlers |
| [PollyElasticsearch](https://github.com/Swevo/PollyElasticsearch) | Polly v8 for Elastic.Clients.Elasticsearch |
| [PollyAzureKeyVault](https://github.com/Swevo/PollyAzureKeyVault) | Polly v8 for Azure Key Vault |
| [PollySendGrid](https://github.com/Swevo/PollySendGrid) | Polly v8 for SendGrid |
| [PollyMassTransit](https://github.com/Swevo/PollyMassTransit) | Polly v8 for MassTransit |
| [PollyAzureTableStorage](https://github.com/Swevo/PollyAzureTableStorage) | Polly v8 for Azure Table Storage |
| [PollyMailKit](https://github.com/Swevo/PollyMailKit) | MailKit SMTP email client |
| [PollyAzureQueueStorage](https://github.com/Swevo/PollyAzureQueueStorage) | Azure Queue Storage QueueClient |
| [PollyHangfire](https://github.com/Swevo/PollyHangfire) | Hangfire IBackgroundJobClient |
| [PollyBackoff](https://www.nuget.org/packages/PollyBackoff) | [![Downloads](https://img.shields.io/nuget/dt/PollyBackoff.svg)](https://www.nuget.org/packages/PollyBackoff) | Jitter, linear & custom backoff for Polly v8 retry |
| [PollyChaos](https://www.nuget.org/packages/PollyChaos) | [![Downloads](https://img.shields.io/nuget/dt/PollyChaos.svg)](https://www.nuget.org/packages/PollyChaos) | Fault & latency injection (Simmy for Polly v8) |
| [PollyCaching](https://www.nuget.org/packages/PollyCaching) | [![Downloads](https://img.shields.io/nuget/dt/PollyCaching.svg)](https://www.nuget.org/packages/PollyCaching) | Cache-aside resilience strategy for Polly v8 |
| [PollyBulkhead](https://www.nuget.org/packages/PollyBulkhead) | [![Downloads](https://img.shields.io/nuget/dt/PollyBulkhead.svg)](https://www.nuget.org/packages/PollyBulkhead) | Bulkhead / concurrency limiter for Polly v8 |
| [PollyOpenTelemetry](https://www.nuget.org/packages/PollyOpenTelemetry) | [![Downloads](https://img.shields.io/nuget/dt/PollyOpenTelemetry.svg)](https://www.nuget.org/packages/PollyOpenTelemetry) | OpenTelemetry metrics & tracing for Polly v8 |

---

## Support

If PollyHealthChecks is useful in your Kubernetes or monitoring setup, consider supporting the project:

[![Sponsor](https://img.shields.io/badge/Sponsor-%E2%9D%A4-pink?logo=github)](https://github.com/sponsors/Swevo)

> 💼 **Need .NET / cloud-native help?** Visit [solidqualitysolutions.com](https://solidqualitysolutions.com/) for consulting and architecture services.

| [PollyRabbitMQ](https://www.nuget.org/packages/PollyRabbitMQ) | Polly v8 resilience for RabbitMQ.Client channels |


## Also by the same author

> 🌐 **[swevo.github.io](https://swevo.github.io/)**

| Package | Description |
|---|---|
| [**AutoLog.Generator**](https://github.com/Swevo/AutoLog.Generator) | Compile-time high-performance logging — `[Log(Level, Message)]` generates `LoggerMessage.Define`. AOT-safe. |
| [**AutoHttpClient.Generator**](https://github.com/Swevo/AutoHttpClient.Generator) | Compile-time typed HTTP client — `[HttpClient]` on an interface generates a strongly-typed client. AOT-safe Refit alternative. |
| [**AutoDispatch.Generator**](https://github.com/Swevo/AutoDispatch.Generator) | Compile-time CQRS dispatcher — `[Handler]` generates a strongly-typed `IDispatcher`. MediatR alternative. |
| [**AutoWire**](https://github.com/Swevo/AutoWire) | Compile-time DI auto-registration — `[Scoped]`/`[Singleton]`/`[Transient]` generates `IServiceCollection` registration code. |
| [**AutoMap.Generator**](https://github.com/Swevo/AutoMap.Generator) | Compile-time object mapping — `[Map(typeof(Dto))]` generates `ToDto()` extension methods. AutoMapper alternative. |
## License

MIT
