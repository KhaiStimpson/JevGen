namespace JevGen.Testing;

/// <summary>
/// Creates real generated clients backed by a scripted runtime.
/// </summary>
/// <remarks>
/// This is not a mock of the contract. It is the generated implementation, wired to a runtime
/// that answers from a script, so tests cover request construction, option mapping and result
/// mapping exactly as production does.
/// </remarks>
public static class JevFake
{
    /// <summary>Creates a fake client and its runtime.</summary>
    public static JevFakeClient<TContract> Create<TContract>()
        where TContract : class
    {
        var runtime = new FakeEvaluationRuntime();
        return new JevFakeClient<TContract>(JevClientRegistry.Create<TContract>(runtime), runtime);
    }

    /// <summary>Creates a fake client, configuring its scripted answers.</summary>
    public static JevFakeClient<TContract> Create<TContract>(Action<FakeEvaluationRuntime> configure)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(configure);

        var fake = Create<TContract>();
        configure(fake.Runtime);
        return fake;
    }
}

/// <summary>A generated client together with the runtime that scripts its answers.</summary>
/// <typeparam name="TContract">The contract interface.</typeparam>
public sealed class JevFakeClient<TContract>
    where TContract : class
{
    internal JevFakeClient(TContract client, FakeEvaluationRuntime runtime)
    {
        Client = client;
        Runtime = runtime;
    }

    /// <summary>The generated client under test.</summary>
    public TContract Client { get; }

    /// <summary>The runtime that answers its evaluations.</summary>
    public FakeEvaluationRuntime Runtime { get; }

    /// <summary>The compile-time metadata for the contract, for asserting on questions.</summary>
    public JevClientDescriptor Descriptor => JevClientRegistry.Get<TContract>();

    /// <summary>Scripts an answer on the underlying runtime.</summary>
    public JevFakeClient<TContract> When(string questionId, JevQuestionResult result)
    {
        Runtime.Answer(questionId, result);
        return this;
    }

    /// <summary>Scripts a choice answer using a strongly typed enum member.</summary>
    /// <remarks>
    /// The enum member is translated into the wire identifier the contract declared, so a test
    /// never has to restate an option's <c>[JevOption]</c> id.
    /// </remarks>
    public JevFakeClient<TContract> Returns<TEnum>(
        string questionId,
        TEnum value,
        double confidence,
        IReadOnlyDictionary<TEnum, double>? probabilities = null)
        where TEnum : struct, Enum
    {
        var question = FindQuestion(questionId);
        var selected = ToOptionId(question, value);

        var wire = probabilities?.ToDictionary(
                       pair => ToOptionId(question, pair.Key),
                       pair => pair.Value,
                       StringComparer.Ordinal)
                   ?? new Dictionary<string, double> { [selected] = confidence };

        Runtime.Choice(questionId, selected, confidence, wire);
        return this;
    }

    /// <summary>Scripts a noul answer.</summary>
    public JevFakeClient<TContract> ReturnsProbability(string questionId, double probability)
    {
        Runtime.Noul(questionId, probability);
        return this;
    }

    /// <summary>Scripts a score answer.</summary>
    public JevFakeClient<TContract> ReturnsScore(string questionId, double value, double? confidence = null)
    {
        Runtime.Score(questionId, value, confidence);
        return this;
    }

    /// <summary>Loads scripted answers from a fixture file.</summary>
    public JevFakeClient<TContract> LoadFixture(string path)
    {
        JevFixture.Load(path).ApplyTo(Runtime);
        return this;
    }

    /// <summary>Loads scripted answers from fixture JSON.</summary>
    public JevFakeClient<TContract> LoadFixtureJson(string json)
    {
        JevFixture.Parse(json).ApplyTo(Runtime);
        return this;
    }

    /// <summary>Implicitly exposes the client, so a fake can be passed wherever the contract is expected.</summary>
    public static implicit operator TContract(JevFakeClient<TContract> fake)
    {
        ArgumentNullException.ThrowIfNull(fake);
        return fake.Client;
    }

    /// <summary>Returns the client.</summary>
    public TContract ToContract() => Client;

    private JevQuestionDefinition FindQuestion(string questionId)
    {
        foreach (var method in Descriptor.Methods)
        {
            foreach (var question in method.Questions)
            {
                if (string.Equals(question.Id, questionId, StringComparison.OrdinalIgnoreCase))
                {
                    return question;
                }
            }
        }

        throw new JevGenException(
            $"{typeof(TContract).Name} declares no question '{questionId}'. Declared questions: " +
            string.Join(", ", Descriptor.Methods.SelectMany(m => m.Questions).Select(q => "'" + q.Id + "'")));
    }

    private static string ToOptionId<TEnum>(JevQuestionDefinition question, TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();

        foreach (var option in question.Options)
        {
            if (string.Equals(option.Id, name, StringComparison.OrdinalIgnoreCase))
            {
                return option.Id;
            }
        }

        // Fall back to positional matching, which covers options renamed by [JevOption].
        var names = Enum.GetNames<TEnum>();
        var index = Array.IndexOf(names, name);

        if (index >= 0 && index < question.Options.Length)
        {
            return question.Options[index].Id;
        }

        throw new JevGenException(
            $"'{name}' does not correspond to any option of question '{question.Id}'. Options: " +
            string.Join(", ", question.Options.Select(option => "'" + option.Id + "'")));
    }
}
