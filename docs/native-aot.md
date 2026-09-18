# Native AOT

Native AOT is a first-class requirement, not a best-effort accommodation. JevGen's own AOT test
publishes a native binary and *executes* it in CI, because the failures worth catching here only
appear at run time.

## What JevGen avoids

- Reflection-based activation — generated factories construct clients directly
- Runtime generic type discovery — every type is known at compile time
- Dynamic assemblies and expression compilation
- Reflection-based serialization — when you supply a serializer context
- Assembly scanning — registration comes from generated module initializers

## The one thing you supply

```csharp
[JsonSerializable(typeof(Ticket))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

[assembly: JevJsonContext(typeof(AppJsonContext))]
```

Source generators cannot see each other's output. JevGen could emit `[JsonSerializable]`
declarations, but `System.Text.Json`'s generator would never process them, so the metadata would
not exist. Declaring the context in your own source is what closes that gap.

Without it JevGen falls back to reflection, which works on a normal runtime and does not in a
native binary. [JEV020](diagnostics.md#jev020) points this out at compile time.

Include every state type, and every context parameter type used with `[Context]`.

## Publishing

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <TrimmerSingleWarn>false</TrimmerSingleWarn>
</PropertyGroup>
```

```bash
dotnet publish -c Release -r linux-x64
```

`TrimmerSingleWarn=false` surfaces every trim warning individually instead of collapsing them
into one, which is what you want when tracking a problem down.

A clean publish produces **no trim or AOT warnings** from JevGen or any of its packages.

## Verifying

Publishing is necessary but not sufficient — run the binary:

```bash
./bin/Release/net10.0/linux-x64/publish/MyApp
```

JevGen's own [AOT test](../tests/JevGen.AotTests) does exactly this. With
reflection-based JSON disabled it checks that:

- the contract registered itself at module load;
- question metadata and enum option criteria survived trimming;
- the debug view works without credentials;
- state serializes through the declared context;
- a full evaluation through DI, the runtime and a provider maps back onto the typed result;
- a decision policy classifies correctly.

```text
JevGen Native AOT smoke test
  Reflection-based JSON is disabled

  PASS  the contract registered itself at module load
  PASS  contract metadata survived trimming
  PASS  enum option identifiers and criteria survived trimming
  PASS  state serializes without reflection
  ...
All Native AOT checks passed.
```

## Trimming without AOT

```xml
<PublishTrimmed>true</PublishTrimmed>
```

The same rules apply: reflection-based serialization is off, so the serializer context is
required.

## Diagnosing a failure

**`EvaluationSerializationException` mentioning missing metadata** — a type is not covered by
your context. Add `[JsonSerializable(typeof(T))]` for it.

**`JevGenException: No generated JevGen client is registered`** — the assembly declaring the
contract does not reference the JevGen package, so the generator never ran over it.

**Trim warnings from your own code** — JevGen's packages are annotated and produce none; a
warning pointing into your own state types usually means a property whose type cannot be
statically described.

## Size

The AOT test binary is around 3 MB, and the full sample application with an HTTP provider around
7 MB.
