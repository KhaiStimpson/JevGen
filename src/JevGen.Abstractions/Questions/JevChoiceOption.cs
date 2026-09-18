namespace JevGen;

/// <summary>A single candidate answer for a <see cref="JevQuestionKind.Choice"/> question.</summary>
/// <param name="Id">The wire identifier the provider sees and returns.</param>
/// <param name="Criteria">Optional guidance describing when this option applies.</param>
public sealed record JevChoiceOption(string Id, string? Criteria = null);
