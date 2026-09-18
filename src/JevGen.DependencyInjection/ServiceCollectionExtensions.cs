using JevGen.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace JevGen;

/// <summary>Registers JevGen with the dependency-injection container.</summary>
public static class JevGenServiceCollectionExtensions
{
    /// <summary>Adds the JevGen runtime.</summary>
    public static JevGenBuilder AddJevGen(this IServiceCollection services)
        => services.AddJevGen(static _ => { });

    /// <summary>Adds the JevGen runtime and configures it.</summary>
    public static JevGenBuilder AddJevGen(this IServiceCollection services, Action<JevGenOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<JevGenOptions>().Configure(configure);

        // Logging is optional: EvaluationRuntime takes a nullable logger with a default, so a
        // container without logging registered still resolves it.

        services.TryAddSingleton<IJevProviderResolver>(provider => new ProviderResolver(
            provider.GetServices<IJevProvider>(),
            provider.GetRequiredService<IOptions<JevGenOptions>>().Value.DefaultProvider));

        services.TryAddSingleton<IEvaluationRuntime, EvaluationRuntime>();

        // Validate contracts against their providers when the application starts, so an
        // incompatible pairing fails the deployment rather than the first request.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, JevGenStartupValidator>());

        return new JevGenBuilder(services);
    }

    /// <summary>Binds <see cref="JevGenOptions"/> from configuration.</summary>
    /// <remarks>
    /// Configuration binding walks the options type reflectively. Trimmed and Native AOT
    /// applications should configure JevGen with the delegate overload, or use the configuration
    /// binding source generator.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Binding JevGenOptions from configuration uses reflection over its members. Use the Action<JevGenOptions> overload in a trimmed application.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "Binding JevGenOptions from configuration may require dynamic code. Use the Action<JevGenOptions> overload in a Native AOT application.")]
    public static JevGenBuilder AddJevGen(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = services.AddJevGen();
        services.AddOptions<JevGenOptions>().Bind(configuration);
        return builder;
    }

    /// <summary>
    /// Registers the generated client for <typeparamref name="TContract"/>.
    /// </summary>
    /// <remarks>
    /// The implementation is resolved from the registry the source generator populates at
    /// module load. There is no runtime proxy, no assembly scanning and no reflection over
    /// attributes, which is what keeps this Native-AOT- and trim-safe.
    /// </remarks>
    /// <exception cref="JevGenException">
    /// No client was generated for the contract, normally because <c>[JevClient]</c> is missing
    /// or the declaring assembly does not reference the JevGen package.
    /// </exception>
    public static JevClientBuilder<TContract> AddJevClient<TContract>(this IServiceCollection services)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddJevGen();

        var descriptor = JevClientRegistry.Get<TContract>();

        services.TryAddTransient<TContract>(provider =>
            (TContract)descriptor.Factory(provider.GetRequiredService<IEvaluationRuntime>()));

        return new JevClientBuilder<TContract>(services);
    }

    /// <summary>Registers the generated client and configures it.</summary>
    public static JevClientBuilder<TContract> AddJevClient<TContract>(
        this IServiceCollection services,
        Action<JevClientConfiguration> configure)
        where TContract : class
        => services.AddJevClient<TContract>().Configure(configure);

    /// <summary>
    /// Registers every generated client in the process.
    /// </summary>
    /// <remarks>
    /// This reads the generated registry, not the assembly. No types are discovered by
    /// scanning, so the call stays AOT-safe and cannot pick up contracts the compiler did not
    /// see.
    /// </remarks>
    public static JevGenBuilder AddAllJevClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = services.AddJevGen();

        foreach (var descriptor in JevClientRegistry.All)
        {
            var factory = descriptor.Factory;

            services.TryAddTransient(
                descriptor.ContractType,
                provider => factory(provider.GetRequiredService<IEvaluationRuntime>()));
        }

        return builder;
    }

    /// <summary>Adds an evaluation filter to the runtime pipeline.</summary>
    public static JevGenBuilder AddEvaluationFilter<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter>(
        this JevGenBuilder builder)
        where TFilter : class, IEvaluationFilter
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IEvaluationFilter, TFilter>();
        return builder;
    }

    /// <summary>Adds an evaluation filter instance to the runtime pipeline.</summary>
    public static JevGenBuilder AddEvaluationFilter(this JevGenBuilder builder, IEvaluationFilter filter)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(filter);

        builder.Services.AddSingleton(filter);
        return builder;
    }
}
