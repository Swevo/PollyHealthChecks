// <copyright file="HealthChecksBuilderExtensions.cs" company="Justin Bannister">
// Copyright (c) Justin Bannister. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly.CircuitBreaker;

namespace PollyHealthChecks;

/// <summary>
/// Extension methods for registering Polly circuit breaker health checks.
/// </summary>
public static class HealthChecksBuilderExtensions
{
    /// <summary>
    /// Adds a health check for a Polly v8 circuit breaker using its <see cref="CircuitBreakerStateProvider"/>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The name of the health check entry.</param>
    /// <param name="stateProvider">The <see cref="CircuitBreakerStateProvider"/> from <see cref="Polly.Retry.RetryStrategyOptions{TResult}"/>.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> to report when the circuit is open or isolated.
    /// Defaults to <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Optional tags to assign to the health check.</param>
    /// <returns>The <see cref="IHealthChecksBuilder"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// var stateProvider = new CircuitBreakerStateProvider();
    ///
    /// services.AddResiliencePipeline("downstream-api", builder =>
    ///     builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions
    ///     {
    ///         StateProvider = stateProvider
    ///     }));
    ///
    /// services.AddHealthChecks()
    ///     .AddPollyCircuitBreaker("downstream-api", stateProvider);
    /// </code>
    /// </example>
    public static IHealthChecksBuilder AddPollyCircuitBreaker(
        this IHealthChecksBuilder builder,
        string name,
        CircuitBreakerStateProvider stateProvider,
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(name));
        ArgumentNullException.ThrowIfNull(stateProvider);

        return builder.Add(new HealthCheckRegistration(
            name,
            _ => new PollyCircuitBreakerHealthCheck(stateProvider, failureStatus),
            failureStatus,
            tags));
    }
}
