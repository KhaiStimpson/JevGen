# Compiler diagnostics

JevGen reports problems at compile time rather than letting a malformed contract fail in
production. Every diagnostic below has a stable identifier you can suppress or escalate through
`.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.JEV007.severity = error
dotnet_diagnostic.JEV019.severity = none
```

| ID | Severity | Summary |
|---|---|---|
| [JEV001](#jev001) | Error | `[JevClient]` target must be an interface |
| [JEV002](#jev002) | Error | Unsupported method return type |
| [JEV003](#jev003) | Error | Missing state parameter |
| [JEV004](#jev004) | Error | Multiple state parameters |
| [JEV005](#jev005) | Error | Unsupported Choice type |
| [JEV006](#jev006) | Error | Invalid score definition |
| [JEV007](#jev007) | Warning | Missing enum option criteria |
| [JEV008](#jev008) | Error | Duplicate question ID |
| [JEV009](#jev009) | Error | Unsupported property result type |
| [JEV010](#jev010) | Error | CancellationToken duplicated |
| [JEV011](#jev011) | Warning | CancellationToken position |
| [JEV012](#jev012) | Warning | State type cannot be serialized |
| [JEV013](#jev013) | Error | Unsupported generic client |
| [JEV014](#jev014) | Error | Question attribute missing |
| [JEV015](#jev015) | Error | Duplicate question attributes |
| [JEV016](#jev016) | Error | Invalid confidence threshold |
| [JEV017](#jev017) | Warning | Provider capability unsupported |
| [JEV018](#jev018) | Warning | Interface declares questions but is not a JevGen client |
| [JEV019](#jev019) | Info | Method takes no CancellationToken |
| [JEV020](#jev020) | Info | No serializer context declared |

---

## JEV001

**`[JevClient]` target must be an interface.**

JevGen generates an implementation *of* your type. A class already has one.

---

## JEV002

**Unsupported method return type.**

A contract method must return `Task<T>` or `ValueTask<T>` where `T` is one of `NoulResult`,
`ChoiceResult<TEnum>`, `ScoreResult`, `Decision<TEnum>`, or a result type whose properties
declare questions.

```csharp
// Wrong: nothing to map onto.
[JevChoice("Route it.")]
Task<string> RouteAsync(Ticket ticket);

// Right.
[JevChoice("Route it.")]
Task<ChoiceResult<Department>> RouteAsync(Ticket ticket);
```

Returning a bare primitive discards the confidence and distribution the model produced. If that
information genuinely has no use in your case, opt in explicitly:

```csharp
[JevNoul("Is it urgent?", AllowPrimitiveResult = true)]
Task<bool> IsUrgentAsync(Ticket ticket);
```

**Code fix:** none. Choosing the right result type is a decision about what your application
needs.

---

## JEV003

**Missing state parameter.**

Every question is evaluated against some state. A method with no non-cancellation parameter has
nothing to reason about.

---

## JEV004

**Multiple state parameters.**

Exactly one parameter is the state. Additional values are context:

```csharp
[JevChoice("Route it.")]
Task<ChoiceResult<Department>> RouteAsync(
    [State] Ticket ticket,
    [Context("region")] string region,
    [Context("customerTier")] CustomerTier tier,
    CancellationToken cancellationToken = default);
```

This also fires when one parameter is marked `[State]` and another is left unmarked, because
the intent is ambiguous.

**Code fix:** "Add [State] to '<parameter>'", offered once per candidate so you choose.

---

## JEV005

**Unsupported Choice type.**

`ChoiceResult<T>` and `Decision<T>` require `T` to be an enum. The option set has to be known at
compile time for the generator to emit the mapping.

---

## JEV006

**Invalid score definition.**

A score question needs a scale. Supply bounds, or rubric labels:

```csharp
[JevScore("Rate the severity.", Min = 1, Max = 5)]
[JevScore("Rate severity.", "Low", "Medium", "High", "Critical")]
```

Reported when only one bound is given, when `Min >= Max`, or when neither bounds nor labels are
present.

---

## JEV007

**Missing enum option criteria.**

```
JEV007: Choice enum member Department.Billing has no criteria.
Add [JevOption] so the model is told when to select it.
```

Without criteria the model sees only a member name. Describing when an option applies measurably
improves routing accuracy, which is why this is a warning rather than a suggestion.

```csharp
public enum Department
{
    [JevOption("billing", "Invoices, payments, subscriptions and refunds")]
    Billing,
}
```

**Code fix:** "Add [JevOption]", which inserts the identifier and a `TODO` placeholder for the
criteria. The placeholder is deliberate: an empty description would look intentional.

---

## JEV008

**Duplicate question ID.**

Question identifiers must be unique *within one evaluation*, because they key the answers. Two
different methods may reuse an identifier, since each builds its own request.

```csharp
[JevNoul("Is it urgent?", Id = "urgency")]
public required NoulResult Urgent { get; init; }
```

---

## JEV009

**Unsupported property result type.**

A `required` property on an aggregate result type must either carry a question attribute or be a
nested result type. It is also reported when the result type has no accessible parameterless
constructor, since the generated mapper builds it with an object initializer.

---

## JEV010

**CancellationToken duplicated.**

---

## JEV011

**CancellationToken position.**

By convention the token is last. This is a warning, not an error.

**Code fix:** "Move the CancellationToken to the end".

---

## JEV012

**State type cannot be serialized.**

The state is serialized to JSON before it reaches a provider. Interfaces, abstract classes, open
generic parameters, delegates and `object` have no concrete shape to serialize.

---

## JEV013

**Unsupported generic client.**

A generic interface has no single closed shape to generate an implementation for.

---

## JEV014

**Question attribute missing.**

Apply `[JevNoul]`, `[JevChoice]`, `[JevScore]` or `[JevEvaluate]`.

---

## JEV015

**Duplicate question attributes.**

One member declares exactly one question. For several questions, use `[JevEvaluate]` and declare
them on the result type's properties.

---

## JEV016

**Invalid confidence threshold.**

Thresholds must lie in `[0, 1]`, and `AcceptAbove` must be greater than `ReviewAbove` — otherwise
the review band is empty or inverted.

---

## JEV017

**Provider capability unsupported.**

A question asks for something its return type cannot carry. Most commonly, a choice question
requires a probability distribution while the method returns a bare enum. JevGen turns the
requirement off and warns, rather than silently promising a distribution that is discarded.

The same identifier is used by the runtime's start-up validation, when a contract requires
semantics the configured provider does not support. See
[provider-configuration.md](provider-configuration.md#capability-validation).

---

## JEV018

**Interface declares questions but is not a JevGen client.**

The interface has question attributes but no `[JevClient]`, so no client is generated for it —
a silent no-op that is almost never what was meant.

**Code fix:** "Add [JevClient]".

---

## JEV019

**Method takes no CancellationToken.**

Informational. An evaluation is a network call; callers usually want to be able to abandon it.

**Code fix:** "Add a CancellationToken parameter".

---

## JEV020

**No serializer context declared.**

Informational, and only reported for assemblies that declare a contract.

Source generators cannot see each other's output, so JevGen cannot emit `[JsonSerializable]`
declarations and have `System.Text.Json` process them. Declare a context yourself and name it:

```csharp
[JsonSerializable(typeof(Ticket))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

[assembly: JevJsonContext(typeof(AppJsonContext))]
```

Without it, state serialization falls back to reflection. That works on a normal runtime but is
neither trim- nor Native-AOT-safe. See [native-aot.md](native-aot.md).
