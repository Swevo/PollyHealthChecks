// <copyright file="HealthChecksBuilderExtensionsTests.cs" company="Justin Bannister">
// Copyright (c) Justin Bannister. All rights reserved.
// </copyright>

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;
using Polly;
using Polly.CircuitBreaker;

namespace PollyHealthChecks.Tests;

[TestFixture]
public class HealthChecksBuilderExtensionsTests
{
    private CircuitBreakerStateProvider _stateProvider = null!;
    private CircuitBreakerManualControl _manualControl = null!;

    [SetUp]
    public void SetUp()
    {
        _stateProvider = new CircuitBreakerStateProvider();
        _manualControl = new CircuitBreakerManualControl();
        new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                StateProvider = _stateProvider,
                ManualControl = _manualControl
            })
            .Build();
    }

    [TearDown]
    public async Task TearDown() => await _manualControl.CloseAsync();

    [Test]
    public void AddPollyCircuitBreaker_RegistersHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddPollyCircuitBreaker("my-api", _stateProvider);

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        healthCheckService.Should().NotBeNull();
    }

    [Test]
    public async Task AddPollyCircuitBreaker_ReturnsHealthy_WhenClosed()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddPollyCircuitBreaker("my-api", _stateProvider);

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();

        var report = await healthCheckService.CheckHealthAsync();

        report.Entries["my-api"].Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task AddPollyCircuitBreaker_ReturnsUnhealthy_WhenIsolated()
    {
        await _manualControl.IsolateAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddPollyCircuitBreaker("my-api", _stateProvider);

        var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["my-api"].Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task AddPollyCircuitBreaker_CustomFailureStatus_IsUsed()
    {
        await _manualControl.IsolateAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddPollyCircuitBreaker("my-api", _stateProvider, failureStatus: HealthStatus.Degraded);

        var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries["my-api"].Status.Should().Be(HealthStatus.Degraded);
    }

    [Test]
    public async Task AddPollyCircuitBreaker_WithTags_FiltersCorrectly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddPollyCircuitBreaker("my-api", _stateProvider, tags: ["ready", "live"]);

        var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Tags.Contains("ready"));

        report.Entries.Should().ContainKey("my-api");
    }

    [Test]
    public void AddPollyCircuitBreaker_NullBuilder_Throws()
    {
        IHealthChecksBuilder builder = null!;
        var act = () => builder.AddPollyCircuitBreaker("test", _stateProvider);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddPollyCircuitBreaker_NullStateProvider_Throws()
    {
        var builder = new ServiceCollection().AddHealthChecks();
        var act = () => builder.AddPollyCircuitBreaker("test", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddPollyCircuitBreaker_EmptyName_Throws()
    {
        var builder = new ServiceCollection().AddHealthChecks();
        var act = () => builder.AddPollyCircuitBreaker("", _stateProvider);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task AddPollyCircuitBreaker_MultipleCircuitBreakers_AllRegistered()
    {
        var stateProvider2 = new CircuitBreakerStateProvider();
        var manualControl2 = new CircuitBreakerManualControl();
        new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                StateProvider = stateProvider2,
                ManualControl = manualControl2
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks()
            .AddPollyCircuitBreaker("api-1", _stateProvider)
            .AddPollyCircuitBreaker("api-2", stateProvider2);

        var provider = services.BuildServiceProvider();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        report.Entries.Should().ContainKey("api-1");
        report.Entries.Should().ContainKey("api-2");

        await manualControl2.CloseAsync();
    }
}
