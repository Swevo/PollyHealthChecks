// <copyright file="PollyCircuitBreakerHealthCheck.cs" company="Justin Bannister">
// Copyright (c) Justin Bannister. All rights reserved.
// </copyright>

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.CircuitBreaker;

namespace PollyHealthChecks;

/// <summary>
/// An <see cref="IHealthCheck"/> that reports the state of a Polly v8 circuit breaker.
/// </summary>
/// <remarks>
/// Maps circuit breaker states to health statuses:
/// <list type="bullet">
/// <item><description><see cref="CircuitState.Closed"/> → <see cref="HealthStatus.Healthy"/></description></item>
/// <item><description><see cref="CircuitState.HalfOpen"/> → <see cref="HealthStatus.Degraded"/></description></item>
/// <item><description><see cref="CircuitState.Open"/> → <c>failureStatus</c> (default <see cref="HealthStatus.Unhealthy"/>)</description></item>
/// <item><description><see cref="CircuitState.Isolated"/> → <c>failureStatus</c> (default <see cref="HealthStatus.Unhealthy"/>)</description></item>
/// </list>
/// </remarks>
public sealed class PollyCircuitBreakerHealthCheck(
    CircuitBreakerStateProvider stateProvider,
    HealthStatus failureStatus = HealthStatus.Unhealthy) : IHealthCheck
{
    /// <inheritdoc/>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var state = stateProvider.CircuitState;

        var result = state switch
        {
            CircuitState.Closed => HealthCheckResult.Healthy($"Circuit breaker is closed."),
            CircuitState.HalfOpen => HealthCheckResult.Degraded($"Circuit breaker is half-open (testing recovery)."),
            CircuitState.Open => new HealthCheckResult(failureStatus, "Circuit breaker is open."),
            CircuitState.Isolated => new HealthCheckResult(failureStatus, "Circuit breaker is manually isolated."),
            _ => new HealthCheckResult(failureStatus, $"Circuit breaker is in unknown state: {state}.")
        };

        return Task.FromResult(result);
    }
}
