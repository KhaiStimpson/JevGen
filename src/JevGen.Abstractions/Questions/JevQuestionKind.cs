namespace JevGen;

/// <summary>The Jev question shapes JevGen can express.</summary>
public enum JevQuestionKind
{
    /// <summary>A probabilistic proposition ("noul").</summary>
    Noul = 0,

    /// <summary>A selection from a fixed set of options.</summary>
    Choice = 1,

    /// <summary>A numeric rating on a declared scale.</summary>
    Score = 2,
}
