namespace JevGen;

/// <summary>Shared surface for the attributes that declare a Jev question.</summary>
public abstract class JevQuestionAttribute : Attribute
{
    /// <summary>Creates a question attribute.</summary>
    /// <param name="prompt">The natural-language question put to the model.</param>
    protected JevQuestionAttribute(string prompt) => Prompt = prompt;

    /// <summary>The natural-language question put to the model.</summary>
    public string Prompt { get; }

    /// <summary>
    /// A stable identifier for the question. Defaults to the camel-cased member name.
    /// Set it explicitly to keep wire identifiers stable across refactors.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>A model override for this question.</summary>
    public string? Model { get; set; }

    /// <summary>A registered provider name to answer this question.</summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Opts into returning a bare primitive — <see cref="bool"/>, an enum or a
    /// <see cref="double"/> — instead of a result type that preserves confidence.
    /// </summary>
    /// <remarks>
    /// Off by default, and deliberately so: collapsing a probabilistic answer into a primitive
    /// throws away the confidence and distribution an application needs to decide how much to
    /// trust it. Turn it on only where that information genuinely has no use.
    /// </remarks>
    public bool AllowPrimitiveResult { get; set; }
}

/// <summary>
/// Declares a noul question: a proposition the model answers with a probability.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class JevNoulAttribute : JevQuestionAttribute
{
    /// <summary>Declares a noul question.</summary>
    /// <param name="prompt">The proposition to evaluate.</param>
    public JevNoulAttribute(string prompt) : base(prompt) { }
}

/// <summary>
/// Declares a choice question: a selection from the members of an enum.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class JevChoiceAttribute : JevQuestionAttribute
{
    /// <summary>Declares a choice question.</summary>
    /// <param name="prompt">The question to answer.</param>
    public JevChoiceAttribute(string prompt) : base(prompt) { }

    /// <summary>
    /// Whether the provider must return a probability for every option. Defaults to
    /// <see langword="true"/>: a contract that returns <see cref="ChoiceResult{T}"/> exposes a
    /// distribution, so a provider that cannot supply one is rejected rather than silently
    /// answering with an empty distribution.
    /// </summary>
    public bool RequireProbabilities { get; set; } = true;
}

/// <summary>
/// Declares a score question: a numeric rating on a declared scale.
/// </summary>
/// <remarks>
/// The scale can be given either as <see cref="Min"/> and <see cref="Max"/> bounds or as an
/// ordered list of rubric labels passed to the constructor.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class JevScoreAttribute : JevQuestionAttribute
{
    /// <summary>Declares a score question.</summary>
    /// <param name="prompt">The rating instruction.</param>
    /// <param name="criteria">
    /// Optional rubric labels, ordered from lowest to highest. When supplied and no explicit
    /// bounds are given, the scale runs from 1 to the number of labels.
    /// </param>
    public JevScoreAttribute(string prompt, params string[] criteria) : base(prompt)
        => Criteria = criteria ?? [];

    /// <summary>The rubric labels, ordered from lowest to highest.</summary>
    public string[] Criteria { get; }

    /// <summary>The inclusive lower bound of the scale.</summary>
    public double Min { get; set; } = double.NaN;

    /// <summary>The inclusive upper bound of the scale.</summary>
    public double Max { get; set; } = double.NaN;
}

/// <summary>
/// Declares a method that evaluates several questions against one state in a single request.
/// The questions are declared on the properties of the method's result type.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class JevEvaluateAttribute : Attribute
{
    /// <summary>A model override for every question in this evaluation.</summary>
    public string? Model { get; set; }

    /// <summary>A registered provider name to answer this evaluation.</summary>
    public string? Provider { get; set; }
}
