namespace Zipper.Validation;

/// <summary>
/// Exception thrown when post-generation validation fails.
/// </summary>
internal sealed class ValidationFailedException : Exception
{
    public ValidationFailedException()
    {
    }

    public ValidationFailedException(string message) : base(message)
    {
    }

    public ValidationFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
