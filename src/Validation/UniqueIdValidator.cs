namespace Zipper.Validation;

public sealed class UniqueIdValidator
{
    public void ValidateIds(IEnumerable<string> ids, string idType, string filePath, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(result);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "UniqueId",
                    $"Duplicate {idType}: '{id}'",
                    filePath));
            }
        }
    }
}
