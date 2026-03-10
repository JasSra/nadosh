using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Nadosh.Core.Telemetry;

public static class TelemetryExtensions
{
    public static IHostBuilder AddNadoshTelemetry(this IHostBuilder builder, string serviceName)
    {
        builder.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.OpenTelemetry(opts =>
            {
                opts.Endpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
                opts.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName
                };
            }));

        builder.ConfigureServices((context, services) =>
        {
            var otelEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

            services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(serviceName))
                .WithTracing(tracing =>
                {
                    tracing
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddEntityFrameworkCoreInstrumentation()
                        .AddOtlpExporter(opts => opts.Endpoint = new Uri(otelEndpoint));
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddOtlpExporter(opts => opts.Endpoint = new Uri(otelEndpoint));
                });
        });

        return builder;
    }
}
