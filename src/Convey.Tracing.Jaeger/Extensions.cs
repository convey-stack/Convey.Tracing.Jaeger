using System;
using Convey.Tracing.Jaeger.Builders;
using Convey.Tracing.Jaeger.Tracers;
using Jaeger;
using Jaeger.Reporters;
using Jaeger.Samplers;
using Jaeger.Senders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTracing;
using OpenTracing.Util;

namespace Convey.Tracing.Jaeger
{
    public static class Extensions
    {
        private static bool _initialized;
        private const string SectionName = "jaeger";
        private const string RegistryName = "tracing.jaeger";

        public static IConveyBuilder AddJaeger(this IConveyBuilder builder, string sectionName = SectionName)
        {
            var options = builder.GetOptions<JaegerOptions>(sectionName);
            return builder.AddJaeger(options);
        }
        
        public static IConveyBuilder AddJaeger(this IConveyBuilder builder, Func<IJaegerOptionsBuilder, IJaegerOptionsBuilder> buildOptions)
        {
            var options = buildOptions(new JaegerOptionsBuilder()).Build();
            return builder.AddJaeger(options);
        }

        public static IConveyBuilder AddJaeger(this IConveyBuilder builder, JaegerOptions options)
        {
            if (!builder.TryRegister(RegistryName) || _initialized)
            {
                return builder;
            }

            _initialized = true;

            if (!options.Enabled)
            {
                var defaultTracer = ConveyDefaultTracer.Create();
                builder.Services.AddSingleton(defaultTracer);
                return builder;
            }

            builder.Services.AddSingleton<ITracer>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

                var reporter = new RemoteReporter
                        .Builder()
                    .WithSender(new UdpSender(options.UdpHost, options.UdpPort, options.MaxPacketSize))
                    .WithLoggerFactory(loggerFactory)
                    .Build();

                var sampler = GetSampler(options);

                var tracer = new Tracer
                        .Builder(options.ServiceName)
                    .WithReporter(reporter)
                    .WithSampler(sampler)
                    .Build();

                GlobalTracer.Register(tracer);

                return tracer;
            });

            return builder;
        }

        private static ISampler GetSampler(JaegerOptions options)
        {
            switch (options.Sampler)
            {
                case "const": return new ConstSampler(true);
                case "rate": return new RateLimitingSampler(options.MaxTracesPerSecond);
                case "probabilistic": return new ProbabilisticSampler(options.SamplingRate);
                default: return new ConstSampler(true);
            }
        }
    }
}