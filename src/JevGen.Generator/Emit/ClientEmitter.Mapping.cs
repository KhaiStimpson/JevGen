using System;
using System.Collections.Generic;
using System.Linq;

namespace JevGen.Generator;

internal static partial class ClientEmitter
{
    // ------------------------------------------------------- result mapping

    /// <summary>
    /// The name of the mapper for one question.
    /// </summary>
    /// <remarks>
    /// Scoped to the declaring method: two methods on the same contract may legitimately declare
    /// the same question identifier, because each builds its own request.
    /// </remarks>
    private static string MapperName(MethodModel method, QuestionModel question)
        => "MapQuestion_" + method.Name + "_" + SourceBuilder.Identifier(question.Id);

    private static string EnumMapperName(string enumTypeName)
        => "MapOption_" + SourceBuilder.Identifier(enumTypeName.Replace("global::", string.Empty));

    private static string EnumDistributionName(string enumTypeName)
        => "MapDistribution_" + SourceBuilder.Identifier(enumTypeName.Replace("global::", string.Empty));

    private static string ResultTypeName(QuestionModel question) => question.Shape switch
    {
        ResultShape.Noul => Jg + "NoulResult",
        ResultShape.Score => Jg + "ScoreResult",
        ResultShape.Choice => $"{Jg}ChoiceResult<{question.ChoiceTypeName}>",
        ResultShape.Decision => $"{Jg}Decision<{question.ChoiceTypeName}>",
        ResultShape.PrimitiveBoolean => "bool",
        ResultShape.PrimitiveEnum => question.ChoiceTypeName!,
        ResultShape.PrimitiveScore => "double",
        _ => throw new InvalidOperationException($"Question '{question.Id}' has no scalar result shape."),
    };

    private static void EmitQuestionMapper(SourceBuilder source, MethodModel method, QuestionModel question)
    {
        var resultType = ResultTypeName(question);
        var id = SourceBuilder.Literal(question.Id);

        source.AppendLine($"/// <summary>Maps the answer to question <c>{question.Id}</c>.</summary>");

        using (source.Block($"internal static {resultType} {MapperName(method, question)}({Jg}EvaluationResponse response)"))
        {
            switch (question.Shape)
            {
                case ResultShape.Noul:
                    source.AppendLine($"var answer = response.Require<{Jg}NoulQuestionResult>({id});");
                    source.AppendLine($"return new {Jg}NoulResult(answer.Probability) {{ Metadata = response.Metadata }};");
                    break;

                case ResultShape.PrimitiveBoolean:
                    source.AppendLine($"return response.Require<{Jg}NoulQuestionResult>({id}).Probability >= 0.5d;");
                    break;

                case ResultShape.Score:
                    source.AppendLine($"var answer = response.Require<{Jg}ScoreQuestionResult>({id});");
                    source.AppendLine($"return new {Jg}ScoreResult(answer.Value, answer.Confidence) {{ Metadata = response.Metadata }};");
                    break;

                case ResultShape.PrimitiveScore:
                    source.AppendLine($"return response.Require<{Jg}ScoreQuestionResult>({id}).Value;");
                    break;

                case ResultShape.Choice:
                    source.AppendLine($"var answer = response.Require<{Jg}ChoiceQuestionResult>({id});");
                    source.AppendLine($"return new {Jg}ChoiceResult<{question.ChoiceTypeName}>(");
                    source.AppendLine($"    {EnumMapperName(question.ChoiceTypeName!)}(answer.SelectedOptionId, {id}),");
                    source.AppendLine("    answer.Confidence,");
                    source.AppendLine($"    {EnumDistributionName(question.ChoiceTypeName!)}(answer.Probabilities))");
                    source.AppendLine("{ Metadata = response.Metadata };");
                    break;

                case ResultShape.PrimitiveEnum:
                    source.AppendLine($"var answer = response.Require<{Jg}ChoiceQuestionResult>({id});");
                    source.AppendLine($"return {EnumMapperName(question.ChoiceTypeName!)}(answer.SelectedOptionId, {id});");
                    break;

                case ResultShape.Decision:
                    var policy = question.Policy ?? new DecisionPolicyModel(0.9d, 0.65d);
                    source.AppendLine($"var answer = response.Require<{Jg}ChoiceQuestionResult>({id});");
                    source.AppendLine($"var value = {EnumMapperName(question.ChoiceTypeName!)}(answer.SelectedOptionId, {id});");
                    source.AppendLine();
                    source.AppendLine("// The policy classifies the result. Acting on the classification is the");
                    source.AppendLine("// application's decision, never JevGen's.");
                    source.AppendLine($"var action = answer.Confidence >= {SourceBuilder.Number(policy.AcceptAbove)}");
                    source.AppendLine($"    ? {Jg}DecisionAction.Accept");
                    source.AppendLine($"    : answer.Confidence >= {SourceBuilder.Number(policy.ReviewAbove)}");
                    source.AppendLine($"        ? {Jg}DecisionAction.Review");
                    source.AppendLine($"        : {Jg}DecisionAction.Reject;");
                    source.AppendLine();
                    source.AppendLine($"return new {Jg}Decision<{question.ChoiceTypeName}>(value, answer.Confidence, action)");
                    source.AppendLine("{ Metadata = response.Metadata };");
                    break;

                default:
                    source.AppendLine("throw new global::System.NotSupportedException();");
                    break;
            }
        }

        source.AppendLine();
    }

    private static void EmitEnumMappers(SourceBuilder source, string enumTypeName, EquatableArray<ChoiceOptionModel> options)
    {
        var mapper = EnumMapperName(enumTypeName);
        var distribution = EnumDistributionName(enumTypeName);

        source.AppendLine($"/// <summary>Maps a wire option identifier onto <c>{enumTypeName}</c>.</summary>");

        using (source.Block($"internal static {enumTypeName} {mapper}(string optionId, string questionId)"))
        {
            using (source.Block("switch (optionId)"))
            {
                foreach (var option in options)
                {
                    source.AppendLine($"case {SourceBuilder.Literal(option.Id)}:");
                    source.AppendLine($"    return {enumTypeName}.{option.MemberName};");
                }
            }

            source.AppendLine();
            source.AppendLine("// Providers are not required to echo casing exactly, and some answer with the");
            source.AppendLine("// member name rather than the declared identifier.");

            using (source.Block("foreach (var candidate in " + distribution + "Keys)"))
            {
                using (source.Block("if (string.Equals(candidate.Key, optionId, global::System.StringComparison.OrdinalIgnoreCase))"))
                {
                    source.AppendLine("return candidate.Value;");
                }
            }

            source.AppendLine();
            source.AppendLine($"throw new {Jg}EvaluationResponseException(");
            source.AppendLine($"    \"The provider answered question '\" + questionId + \"' with option '\" + optionId +");
            source.AppendLine($"    \"', which is not a member of {enumTypeName.Replace("global::", string.Empty)}.\");");
        }

        source.AppendLine();

        source.AppendLine($"private static readonly {Generic}KeyValuePair<string, {enumTypeName}>[] {distribution}Keys =");
        source.AppendLine("{");

        foreach (var option in options)
        {
            source.AppendLine(
                $"    new {Generic}KeyValuePair<string, {enumTypeName}>({SourceBuilder.Literal(option.Id)}, {enumTypeName}.{option.MemberName}),");

            if (!string.Equals(option.Id, option.MemberName, StringComparison.OrdinalIgnoreCase))
            {
                source.AppendLine(
                    $"    new {Generic}KeyValuePair<string, {enumTypeName}>({SourceBuilder.Literal(option.MemberName)}, {enumTypeName}.{option.MemberName}),");
            }
        }

        source.AppendLine("};");
        source.AppendLine();

        source.AppendLine($"/// <summary>Projects a wire probability distribution onto <c>{enumTypeName}</c>.</summary>");

        using (source.Block(
            $"internal static {Generic}IReadOnlyDictionary<{enumTypeName}, double> {distribution}(" +
            $"{Generic}IReadOnlyDictionary<string, double> probabilities)"))
        {
            source.AppendLine(
                $"var mapped = new {Generic}Dictionary<{enumTypeName}, double>(probabilities.Count);");
            source.AppendLine();

            using (source.Block("foreach (var entry in probabilities)"))
            {
                using (source.Block($"foreach (var candidate in {distribution}Keys)"))
                {
                    using (source.Block("if (string.Equals(candidate.Key, entry.Key, global::System.StringComparison.OrdinalIgnoreCase))"))
                    {
                        source.AppendLine("mapped[candidate.Value] = entry.Value;");
                        source.AppendLine("break;");
                    }
                }
            }

            source.AppendLine();
            source.AppendLine("return mapped;");
        }

        source.AppendLine();
    }

    private static void EmitResponseMapper(SourceBuilder source, MethodModel method)
    {
        source.AppendLine($"/// <summary>Maps the evaluation response for <c>{method.Name}</c>.</summary>");

        using (source.Block(
            $"internal static {method.ReturnTypeName} Map{method.Name}Response({Jg}EvaluationResponse response)"))
        {
            if (method.Shape == ResultShape.Aggregate && method.Aggregate is { } aggregate)
            {
                EmitAggregate(source, method, aggregate, "return ", ";");
            }
            else
            {
                source.AppendLine($"return {MapperName(method, method.Questions[0])}(response);");
            }
        }

        source.AppendLine();
    }

    private static void EmitAggregate(
        SourceBuilder source,
        MethodModel method,
        AggregateModel aggregate,
        string prefix,
        string suffix)
    {
        source.AppendLine($"{prefix}new {aggregate.TypeName}");

        using (source.Braces())
        {
            foreach (var property in aggregate.Properties)
            {
                if (property.Question is { } question)
                {
                    source.AppendLine($"{property.PropertyName} = {MapperName(method, question)}(response),");
                }
                else
                {
                    EmitAggregate(source, method, property.Nested!, property.PropertyName + " = ", ",");
                }
            }
        }

        // Close the initializer with the statement terminator the caller needs.
        source.Replace("}\n", "}" + suffix + "\n");
    }
}
