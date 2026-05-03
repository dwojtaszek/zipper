namespace Zipper.Profiles;

/// <summary>
/// Email subject generator.
/// </summary>
internal static class EmailSubjects
{
    private static readonly string[] Prefixes = { string.Empty, "Re: ", "Fwd: ", "RE: ", "FW: " };

    private static readonly string[] Subjects =
    {
        "Meeting Tomorrow", "Project Update", "Quick Question", "Follow Up",
        "Action Required", "Review Needed", "Status Report", "Weekly Summary",
        "Important Notice", "Reminder", "Schedule Change", "Document Review",
        "Contract Discussion", "Budget Update", "Team Meeting", "Client Call",
    };

    /// <summary>
    /// Gets a random email subject.
    /// </summary>
    /// <param name="random">Random instance.</param>
    /// <returns>Email subject.</returns>
    public static string GetRandom(Random random)
    {
        var prefix = Prefixes[random.Next(Prefixes.Length)];
        var subject = Subjects[random.Next(Subjects.Length)];
        return prefix + subject;
    }
}
