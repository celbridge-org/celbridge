namespace Celbridge.Validators;

/// <summary>
/// isValid is true if the input string is valid.
/// The errors list contains the validation errors if the string is not valid.
/// </summary>
public record ValidationResult(bool IsValid, List<string> Errors);
