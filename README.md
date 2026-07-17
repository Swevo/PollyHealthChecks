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

## Related Packages

| Package | Downloads | Description |
|---|---|---|
| [PollyOpenTelemetry](https://www.nuget.org/packages/PollyOpenTelemetry) | [![Downloads](https://img.shields.io/nuget/dt/PollyOpenTelemetry.svg)](https://www.nuget.org/packages/PollyOpenTelemetry) | OpenTelemetry instrumentation for Polly v8 resilience pipelines |
| [PollyBackoff](https://www.nuget.org/packages/PollyBackoff) | [![Downloads](https://img.shields.io/nuget/dt/PollyBackoff.svg)](https://www.nuget.org/packages/PollyBackoff) | Backoff delay strategies for Polly v8 resilience pipelines |
| [PollyGrpc](https://www.nuget.org/packages/PollyGrpc) | [![Downloads](https://img.shields.io/nuget/dt/PollyGrpc.svg)](https://www.nuget.org/packages/PollyGrpc) | Polly v8 resilience interceptor for gRPC |
| [PollyEFCore](https://www.nuget.org/packages/PollyEFCore) | [![Downloads](https://img.shields.io/nuget/dt/PollyEFCore.svg)](https://www.nuget.org/packages/PollyEFCore) | Polly v8 resilience pipelines for Entity Framework Core — wrap every EF Core query and SaveChanges with retry, timeout and circuit-breaker via a single AddPollyResilience() call |
| [PollyMailKit](https://www.nuget.org/packages/PollyMailKit) | [![Downloads](https://img.shields.io/nuget/dt/PollyMailKit.svg)](https://www.nuget.org/packages/PollyMailKit) | Polly v8 resilience pipelines for MailKit — retry, timeout, and circuit-breaker for SmtpClient.SendAsync and any MailKit SMTP operation |
| [PollyMassTransit](https://www.nuget.org/packages/PollyMassTransit) | [![Downloads](https://img.shields.io/nuget/dt/PollyMassTransit.svg)](https://www.nuget.org/packages/PollyMassTransit) | Polly v8 resilience pipelines for MassTransit — retry, timeout, and circuit-breaker for IBus.Publish and ISendEndpointProvider.Send |
| [PollyOpenAI](https://www.nuget.org/packages/PollyOpenAI) | [![Downloads](https://img.shields.io/nuget/dt/PollyOpenAI.svg)](https://www.nuget.org/packages/PollyOpenAI) | Polly v8 resilience for OpenAI and Azure OpenAI API calls |
| [PollyAzureEventHub](https://www.nuget.org/packages/PollyAzureEventHub) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureEventHub.svg)](https://www.nuget.org/packages/PollyAzureEventHub) | Polly v8 resilience pipelines for Azure Event Hubs — retry, timeout, and circuit-breaker for EventHubProducerClient and EventHubConsumerClient |
| [PollySignalR](https://www.nuget.org/packages/PollySignalR) | [![Downloads](https://img.shields.io/nuget/dt/PollySignalR.svg)](https://www.nuget.org/packages/PollySignalR) | Polly v8 reconnect policy for SignalR |
| [PollyElasticsearch](https://www.nuget.org/packages/PollyElasticsearch) | [![Downloads](https://img.shields.io/nuget/dt/PollyElasticsearch.svg)](https://www.nuget.org/packages/PollyElasticsearch) | Polly v8 resilience pipelines for Elastic.Clients.Elasticsearch 8+ — retry, timeout, and circuit-breaker for any Elasticsearch operation, plus a built-in ElasticTransientErrors predicate covering rate limiting (429), service unavailability (503), gateway timeouts (504), and connection failures |
| [PollyHangfire](https://www.nuget.org/packages/PollyHangfire) | [![Downloads](https://img.shields.io/nuget/dt/PollyHangfire.svg)](https://www.nuget.org/packages/PollyHangfire) | Polly v8 resilience pipelines for Hangfire — retry, timeout, and circuit-breaker for IBackgroundJobClient.Enqueue and Schedule |
| [PollySendGrid](https://www.nuget.org/packages/PollySendGrid) | [![Downloads](https://img.shields.io/nuget/dt/PollySendGrid.svg)](https://www.nuget.org/packages/PollySendGrid) | Polly v8 resilience pipelines for SendGrid — retry, timeout, and circuit-breaker for ISendGridClient.SendEmailAsync |
| [PollyMediatR](https://www.nuget.org/packages/PollyMediatR) | [![Downloads](https://img.shields.io/nuget/dt/PollyMediatR.svg)](https://www.nuget.org/packages/PollyMediatR) | Polly v8 resilience pipelines for MediatR — add retry, timeout, circuit-breaker, rate-limiting, hedging, and chaos engineering to any MediatR request handler with a single line of DI registration |
| [PollyAzureKeyVault](https://www.nuget.org/packages/PollyAzureKeyVault) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureKeyVault.svg)](https://www.nuget.org/packages/PollyAzureKeyVault) | Polly v8 resilience pipelines for Azure Key Vault — retry, timeout, and circuit-breaker for SecretClient, KeyClient, and CertificateClient |
| [PollyAzureQueueStorage](https://www.nuget.org/packages/PollyAzureQueueStorage) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureQueueStorage.svg)](https://www.nuget.org/packages/PollyAzureQueueStorage) | Polly v8 resilience pipelines for Azure Queue Storage — retry, timeout, and circuit-breaker for Azure.Storage.Queues QueueClient |
| [PollyRedis](https://www.nuget.org/packages/PollyRedis) | [![Downloads](https://img.shields.io/nuget/dt/PollyRedis.svg)](https://www.nuget.org/packages/PollyRedis) | Polly v8 resilience for StackExchange.Redis |
| [PollyAzureServiceBus](https://www.nuget.org/packages/PollyAzureServiceBus) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureServiceBus.svg)](https://www.nuget.org/packages/PollyAzureServiceBus) | Polly v8 resilience for Azure Service Bus — retry, circuit breaker, and timeout for sending and receiving messages |
| [PollyKafka](https://www.nuget.org/packages/PollyKafka) | [![Downloads](https://img.shields.io/nuget/dt/PollyKafka.svg)](https://www.nuget.org/packages/PollyKafka) | Polly v8 resilience for Confluent.Kafka — retry, circuit breaker, and timeout for producers and consumers |
| [PollyAzureTableStorage](https://www.nuget.org/packages/PollyAzureTableStorage) | [![Downloads](https://img.shields.io/nuget/dt/PollyAzureTableStorage.svg)](https://www.nuget.org/packages/PollyAzureTableStorage) | Polly v8 resilience pipelines for Azure Table Storage — retry, timeout, and circuit-breaker for Azure.Data.Tables TableClient |
| [PollyCaching](https://www.nuget.org/packages/PollyCaching) | [![Downloads](https://img.shields.io/nuget/dt/PollyCaching.svg)](https://www.nuget.org/packages/PollyCaching) | A caching resilience strategy for Polly v8 pipelines |
| [PollyChaos](https://www.nuget.org/packages/PollyChaos) | [![Downloads](https://img.shields.io/nuget/dt/PollyChaos.svg)](https://www.nuget.org/packages/PollyChaos) | Chaos engineering and fault-injection resilience strategies for Polly v8 pipelines |
| [PollyBulkhead](https://www.nuget.org/packages/PollyBulkhead) | [![Downloads](https://img.shields.io/nuget/dt/PollyBulkhead.svg)](https://www.nuget.org/packages/PollyBulkhead) | Bulkhead isolation strategy for Polly v8 resilience pipelines |

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
