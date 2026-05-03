namespace Zipper.Profiles;

/// <summary>
/// Review notes generator.
/// </summary>
internal static class ReviewNotes
{
    private static readonly string[] Notes =
    {
        "Reviewed and marked responsive.",
        "Contains privileged attorney-client communication.",
        "Not relevant to the matter.",
        "Contains trade secret information - needs redaction.",
        "Duplicate of document DOC000001.",
        "Foreign language content detected - needs translation.",
        "Reviewed by reviewer on date.",
        "Escalated for senior review.",
        "Contains PII - redaction required.",
        "Key document - flag for production.",
    };

    /// <summary>
    /// Gets a random review note.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Review note.</returns>
    public static string GetRandomNote(Random random)
    {
        return Notes[random.Next(Notes.Length)];
    }
}
