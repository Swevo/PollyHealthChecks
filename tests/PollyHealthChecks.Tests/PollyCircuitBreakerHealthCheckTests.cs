// <copyright file="PollyCircuitBreakerHealthCheckTests.cs" company="Justin Bannister">
// Copyright (c) Justin Bannister. All rights reserved.
// </copyright>

using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;
using Polly;
using Polly.CircuitBreaker;

namespace PollyHealthChecks.Tests;

[TestFixture]
public class PollyCircuitBreakerHealthCheckTests
{
    private CircuitBreakerStateProvider _stateProvider = null!;
    private CircuitBreakerManualControl _manualControl = null!;
    private HealthCheckContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _stateProvider = new CircuitBreakerStateProvider();
        _manualControl = new CircuitBreakerManualControl();
        _context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", _ => null!, null, null)
        };

        new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                StateProvider = _stateProvider,
                ManualControl = _manualControl,
                SamplingDuration = TimeSpan.FromSeconds(10),
                MinimumThroughput = 2,
                FailureRatio = 0.5,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .Build();
    }

    [TearDown]
    public async Task TearDown() => await _manualControl.CloseAsync();

    [Test]
    public async Task ClosedState_ReturnsHealthy()
    {
        var check = new PollyCircuitBreakerHealthCheck(_stateProvider);

        var result = await check.CheckHealthAsync(_context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("closed");
    }

    [Test]
    public async Task IsolatedState_ReturnsUnhealthy_ByDefault()
    {
        await _manualControl.IsolateAsync();
        var check = new PollyCircuitBreakerHealthCheck(_stateProvider);

        var result = await check.CheckHealthAsync(_context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("isolated");
    }

    [Test]
    public async Task IsolatedState_ReturnsConfiguredFailureStatus()
    {
        await _manualControl.IsolateAsync();
        var check = new PollyCircuitBreakerHealthCheck(_stateProvider, HealthStatus.Degraded);

        var result = await check.CheckHealthAsync(_context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Test]
    public async Task ClosedState_DescriptionIsNotEmpty()
    {
        var check = new PollyCircuitBreakerHealthCheck(_stateProvider);

        var result = await check.CheckHealthAsync(_context);

        result.Description.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task CheckHealthAsync_SupportsCancellation()
    {
        var check = new PollyCircuitBreakerHealthCheck(_stateProvider);
        using var cts = new CancellationTokenSource();

        var act = async () => await check.CheckHealthAsync(_context, cts.Token);

        await act.Should().NotThrowAsync();
    }
}
