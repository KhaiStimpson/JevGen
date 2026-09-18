using System.Collections.Generic;
using System.Linq;

namespace JevGen.Generator;

internal static partial class ClientEmitter
{
    // ------------------------------------------------------------ descriptor

    private static void EmitDescriptor(SourceBuilder source, ClientModel client)
    {
        source.AppendLine("/// <summary>Compile-time metadata describing this contract.</summary>");
        source.AppendLine($"internal static readonly {Jg}JevClientDescriptor Descriptor = BuildDescriptor();");
        source.AppendLine();

        using (source.Block($"private static {Jg}JevClientDescriptor BuildDescriptor()"))
        {
            source.AppendLine($"return new {Jg}JevClientDescriptor");
            source.AppendLine("{");
            source.AppendLine($"    ContractType = typeof({client.FullyQualifiedInterfaceName}),");
            source.AppendLine($"    ImplementationType = typeof({client.ClientTypeName}),");
            source.AppendLine($"    Name = {SourceBuilder.Literal(client.DisplayName)},");
            source.AppendLine($"    ContractVersion = {SourceBuilder.Literal(client.ContractVersion)},");
            source.AppendLine($"    Provider = {SourceBuilder.Literal(client.Provider)},");
            source.AppendLine($"    Model = {SourceBuilder.Literal(client.Model)},");
            source.AppendLine("    Factory = static runtime => new " + client.ClientTypeName + "(runtime),");
            source.AppendLine($"    Methods = {Immutable}ImmutableArray.Create<{Jg}JevMethodDescriptor>(");

            var methods = client.Methods.Values;

            for (var index = 0; index < methods.Length; index++)
            {
                var method = methods[index];
                source.AppendLine($"        new {Jg}JevMethodDescriptor");
                source.AppendLine("        {");
                source.AppendLine($"            Name = {SourceBuilder.Literal(method.Name)},");
                source.AppendLine($"            Questions = {method.Name}_Questions,");
                source.AppendLine($"            StateTypeName = {SourceBuilder.Literal(Display(method.StateTypeName))},");
                source.AppendLine($"            ResultTypeName = {SourceBuilder.Literal(Display(method.ReturnTypeName))},");
                source.AppendLine($"            Model = {SourceBuilder.Literal(method.Model ?? client.Model)},");
                source.AppendLine($"            Provider = {SourceBuilder.Literal(method.Provider ?? client.Provider)},");
                source.AppendLine("        }" + (index == methods.Length - 1 ? string.Empty : ","));
            }

            source.AppendLine("    ),");
            source.AppendLine("};");
        }
    }

    private static string Display(string fullyQualified) => fullyQualified.Replace("global::", string.Empty);

    // ---------------------------------------------------------------- client

    private static void EmitClient(SourceBuilder source, ClientModel client)
    {
        source.AppendLine($"/// <summary>The generated implementation of <see cref=\"{Display(client.FullyQualifiedInterfaceName)}\"/>.</summary>");
        EmitGeneratedCodeAttribute(source);

        using (source.Block(
            $"internal sealed class {client.ClientTypeName} : {client.FullyQualifiedInterfaceName}, {Jg}IJevClient"))
        {
            source.AppendLine($"private readonly {Jg}IEvaluationRuntime _runtime;");
            source.AppendLine();

            using (source.Block($"public {client.ClientTypeName}({Jg}IEvaluationRuntime runtime)"))
            {
                source.AppendLine("_runtime = runtime ?? throw new global::System.ArgumentNullException(nameof(runtime));");
            }

            source.AppendLine();
            source.AppendLine($"public {Jg}JevClientDescriptor Descriptor => {client.SchemaTypeName}.Descriptor;");

            foreach (var method in client.Methods)
            {
                source.AppendLine();
                EmitClientMethod(source, client, method);
            }
        }
    }

    private static void EmitClientMethod(SourceBuilder source, ClientModel client, MethodModel method)
    {
        var parameters = new List<string> { $"{method.StateTypeName} {method.StateParameterName}" };

        parameters.AddRange(
            method.ContextParameters.Values.Select(context => $"{context.TypeName} {context.ParameterName}"));

        var cancellation = method.CancellationTokenParameterName;

        if (cancellation is not null)
        {
            parameters.Add($"global::System.Threading.CancellationToken {cancellation}");
        }

        var returnType = method.IsTask
            ? $"global::System.Threading.Tasks.Task<{method.ReturnTypeName}>"
            : $"global::System.Threading.Tasks.ValueTask<{method.ReturnTypeName}>";

        var arguments = new List<string> { method.StateParameterName };
        arguments.AddRange(method.ContextParameters.Values.Select(context => context.ParameterName));

        source.AppendLine("/// <inheritdoc />");

        using (source.Block($"public async {returnType} {method.Name}({string.Join(", ", parameters)})"))
        {
            source.AppendLine(
                $"var request = {client.SchemaTypeName}.Create{method.Name}Request({string.Join(", ", arguments)});");
            source.AppendLine();
            source.AppendLine("var response = await _runtime");
            source.AppendLine($"    .EvaluateAsync(request, {cancellation ?? "global::System.Threading.CancellationToken.None"})");
            source.AppendLine("    .ConfigureAwait(false);");
            source.AppendLine();
            source.AppendLine($"return {client.SchemaTypeName}.Map{method.Name}Response(response);");
        }
    }

    // ---------------------------------------------------------- registration

    private static void EmitRegistration(SourceBuilder source, ClientModel client)
    {
        source.AppendLine("/// <summary>");
        source.AppendLine($"/// Registers <see cref=\"{Display(client.FullyQualifiedInterfaceName)}\"/> with the JevGen client registry when this");
        source.AppendLine("/// module loads, so AddJevClient&lt;T&gt;() resolves it without assembly scanning or reflection.");
        source.AppendLine("/// </summary>");
        EmitGeneratedCodeAttribute(source);

        using (source.Block($"internal static class {client.RegistrationTypeName}"))
        {
            source.AppendLine("[global::System.Runtime.CompilerServices.ModuleInitializer]");

            using (source.Block("internal static void Initialize()"))
            {
                source.AppendLine($"{Jg}JevClientRegistry.Register({client.SchemaTypeName}.Descriptor);");
            }
        }
    }
}
