namespace Celbridge.BaseLibrary.Validators;

/// <summary>
/// Applies a set of validation rules to an input string.
/// </summary>
public interface IValidator
{
    /// <summary>
    /// Returns true if the input string is valid.
    /// The errors list contains the validation errors if the string is not valid.
    /// </summary>
    ValidationResult Validate(string input);
}
