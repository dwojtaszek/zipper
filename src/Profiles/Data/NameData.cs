namespace Zipper.Profiles;

/// <summary>
/// Name data for generation.
/// </summary>
internal static class Names
{
    /// <summary>
    /// First names for generation.
    /// </summary>
    public static readonly string[] FirstNames =
    {
        "James", "Mary", "John", "Patricia", "Robert", "Jennifer", "Michael", "Linda",
        "William", "Elizabeth", "David", "Barbara", "Richard", "Susan", "Joseph", "Jessica",
        "Thomas", "Sarah", "Charles", "Karen", "Christopher", "Nancy", "Daniel", "Lisa",
        "Matthew", "Betty", "Anthony", "Margaret", "Mark", "Sandra", "Donald", "Ashley",
    };

    /// <summary>
    /// Lowercased first names for generation.
    /// </summary>
    internal static readonly string[] FirstNamesLower;

    /// <summary>
    /// Last names for generation.
    /// </summary>
    public static readonly string[] LastNames =
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
        "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson",
        "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson",
        "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson", "Walker",
    };

    /// <summary>
    /// Lowercased last names for generation.
    /// </summary>
    internal static readonly string[] LastNamesLower;

    /// <summary>
    /// Initializes static members of the <see cref="Names"/> class.
    /// </summary>
    static Names()
    {
        FirstNamesLower = FirstNames.Select(n => n.ToLowerInvariant()).ToArray();
        LastNamesLower = LastNames.Select(n => n.ToLowerInvariant()).ToArray();
    }
}
