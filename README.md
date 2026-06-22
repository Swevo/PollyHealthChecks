# PollyHealthChecks

<img src="icon.png" width="100" align="right" />

[![NuGet](https://img.shields.io/nuget/v/PollyHealthChecks.svg)](https://www.nuget.org/packages/PollyHealthChecks)
[![CI](https://github.com/Swevo/PollyHealthChecks/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/PollyHealthChecks/actions/workflows/build.yml)

ASP.NET Core health checks for **Polly v8** circuit breakers.

Expose circuit breaker state as standard `/health` endpoint responses so load balancers, Kubernetes probes, and monitoring dashboards can act on the resilience state of your dependencies.

## Install

```
dotnet add package PollyHealthChecks
```

## Usage

### 1. Set up your circuit breaker with a `CircuitBreakerStateProvider`

```csharp
using Polly.CircuitBreaker;

var stateProvider = new CircuitBreakerStateProvider();
var manualControl = new CircuitBreakerManualControl(); // optional

services.AddResiliencePipeline("downstream-api", builder =>
    builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        StateProvider = stateProvider,
        ManualControl = manualControl,  // optional
        FailureRatio = 0.5,
        SamplingDuration = TimeSpan.FromSeconds(10),
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30)
    }));
```

### 2. Register the health check

```csharp
using PollyHealthChecks;

services.AddHealthChecks()
    .AddPollyCircuitBreaker("downstream-api", stateProvider);
```

### 3. Map the health endpoint

```csharp
app.MapHealthChecks("/health");
```

### Example response

When the circuit is **closed** (normal operation):
```json
{ "status": "Healthy", "entries": { "downstream-api": { "status": "Healthy", "description": "Circuit breaker is closed." } } }
```

When the circuit is **open** (tripping):
```json
{ "status": "Unhealthy", "entries": { "downstream-api": { "status": "Unhealthy", "description": "Circuit breaker is open." } } }
```

## State mapping

| Circuit breaker state | Health status |
|-----------------------|---------------|
| `Closed` | `Healthy` |
| `HalfOpen` | `Degraded` |
| `Open` | `failureStatus` (default `Unhealthy`) |
| `Isolated` | `failureStatus` (default `Unhealthy`) |

## Options

```csharp
services.AddHealthChecks()
    .AddPollyCircuitBreaker(
        name: "downstream-api",
        stateProvider: stateProvider,
        failureStatus: HealthStatus.Degraded,  // override open/isolated status
        tags: ["ready", "live"]);              // for filtered health checks
```

## Multiple circuit breakers

```csharp
services.AddHealthChecks()
    .AddPollyCircuitBreaker("payments-api", paymentsStateProvider)
    .AddPollyCircuitBreaker("inventory-api", inventoryStateProvider, failureStatus: HealthStatus.Degraded)
    .AddPollyCircuitBreaker("auth-api", authStateProvider, tags: ["ready"]);
```

## License

MIT
