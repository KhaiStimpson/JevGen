# Troubleshooting

## No client is generated

**Symptom:** `JevGenException: No generated JevGen client is registered for 'ITicketAI'`.

Work through these in order:

1. **Is `[JevClient]` on the interface?** [JEV018](diagnostics.md#jev018) warns when an
   interface declares questions without it.
2. **Does the declaring assembly reference the `JevGen` package?** The generator only runs over
   assemblies that reference it. A contract in a project that references only your own shared
   library will not be generated.
3. **Are there generator errors?** A contract with a `JEV0xx` error produces no client, by
   design — emitting code on top of a broken contract would bury the real error.
4. **Referencing JevGen by project reference?** Analyzer assets flow through a NuGet package but
   **not** through a `ProjectReference` chain. A project that reaches JevGen indirectly gets no
   generator. Attach it explicitly:

   ```xml
   <ProjectReference Include="path/to/JevGen.Generator/JevGen.Generator.csproj"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```

To see what was generated:

```xml
<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
```

Output lands in `obj/<config>/<tfm>/generated/JevGen.Generator/`.

## Serialization fails at run time

**Symptom:** `EvaluationSerializationException: No JSON serialization metadata is available`.

The state type is not covered by a registered serializer context, and reflection-based
serialization is unavailable — normal in a trimmed or Native AOT application.

```csharp
[JsonSerializable(typeof(Ticket))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

[assembly: JevJsonContext(typeof(AppJsonContext))]
```

Include every state type and every `[Context]` parameter type. See
[native-aot.md](native-aot.md).

## Capability exception at start-up

**Symptom:** `EvaluationCapabilityException` listing what a contract requires and what the
provider supports.

The contract needs semantics the provider does not have. This is a real incompatibility, not a
spurious check — a contract returning `ChoiceResult<T>` promises a distribution its caller can
read.

Three honest resolutions:

```csharp
// Use a provider that supports it.
services.AddJevClient<ITicketAI>().UseTypeSafe();

// Fall back to one that does.
services.AddJevClient<ITicketAI>().UseProvider("legacy").FallbackTo("typesafe");

// Accept degraded semantics, deliberately, for this contract.
services.AddJevClient<ITicketAI>().AllowDegradedCapabilities();
```

For a chat-model provider, this usually means `AllowApproximateProbabilities` is off. That
default is deliberate; see
[provider-configuration.md](provider-configuration.md#chat-model-providers).

## The provider returned no distribution

**Symptom:** `Question 'route' requires a probability distribution, but provider 'x' returned
only a selected option.`

JevGen refuses rather than handing back an empty distribution behind a type that promises one.

Either use a provider that reports probabilities, or say the contract does not need them:

```csharp
[JevChoice("Which department should handle this?", RequireProbabilities = false)]
```

## An unknown option came back

**Symptom:** `The provider answered question 'route' with option 'billing_dept', which is not a
member of Department.`

Mapping tries the declared identifier, then a case-insensitive match against the identifier and
the member name. Anything else raises rather than defaulting to the first member — silently
routing everything to `Billing` would be much worse than failing.

Usually a drifted `[JevOption]` identifier, or a model inventing an option. Improving the
criteria often fixes the second case.

## Evaluations time out

Check which budget is being hit:

- `JevGenOptions.Timeout` — the whole evaluation, retries and fallbacks included
- `JevResilienceOptions.Timeout` — one attempt
- `JevOptions.Timeout` — the HTTP transport

The outer budget must exceed the inner one times the attempt count, or the outer one cuts
retries short and you never see the retry behaviour you configured.

## Fallback is not happening

Fallback triggers on transient failures, timeouts, rate limits and capability mismatches. It
deliberately does **not** trigger on:

- `EvaluationAuthenticationException` — rejected everywhere
- `EvaluationResponseException` — deterministic
- `EvaluationSerializationException` — an application bug
- 400 and 422 — the request is invalid as sent

Check the exception type before concluding fallback is broken. Confidence-based fallback needs
`FallbackWhenConfidenceBelow` *and* at least one `FallbackTo`.

## Warnings about missing criteria

[JEV007](diagnostics.md#jev007) is a warning because criteria measurably improve routing quality.
Add them, or silence the rule if you have genuinely decided against:

```ini
dotnet_diagnostic.JEV007.severity = none
```

## The generator seems slow, or the IDE is sluggish

Generation is incremental and keyed on contract shape; editing an unrelated file should not
regenerate anything. If it does, the usual cause is a mutable type leaking into the pipeline in
a fork of the generator.

Measure it:

```bash
dotnet build -p:ReportAnalyzer=true -v d | grep -A 20 "Total analyzer execution time"
```

## Two contracts with the same name

Supported. Generated types are namespaced by the contract's own namespace, so
`A.ITicketAI` and `B.ITicketAI` generate into `JevGen.Generated.A` and `JevGen.Generated.B`.

## Fixing questions that changed identifier

Renaming a method or property changes the wire identifier, which silently invalidates fixtures
and any stored data keyed on it. Pin identifiers on anything long-lived:

```csharp
[JevChoice("Which department should handle this?", Id = "department")]
```
