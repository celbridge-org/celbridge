using Celbridge.Console;
using Celbridge.Console.Helpers;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleSessionTypeValidatorTests
{
    private static ConsoleSessionType SessionType(string typeId)
    {
        return new ConsoleSessionType(typeId, Array.Empty<string>(), Array.Empty<ConsoleRunner>());
    }

    private static Result Validate(params string[] typeIds)
    {
        return ConsoleSessionTypeValidator.Validate(typeIds.Select(SessionType).ToList());
    }

    [Test]
    public void Validate_TheBuiltInTypes_Passes()
    {
        Validate("shell", "python").IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Validate_NoTypes_Passes()
    {
        ConsoleSessionTypeValidator.Validate(Array.Empty<ConsoleSessionType>()).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void Validate_AHyphenatedId_Passes()
    {
        Validate("python-notebook").IsSuccess.Should().BeTrue();
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Validate_ABlankId_Fails(string typeId)
    {
        var result = Validate(typeId);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("empty type id");
    }

    // A dot would nest the table ([session.my.type]), and the others are not bare TOML keys at all.
    [TestCase("my.type")]
    [TestCase("my type")]
    [TestCase("My-Type")]
    [TestCase("my_type")]
    [TestCase("2fast")]
    public void Validate_AnIdTheFormatCannotName_Fails(string typeId)
    {
        var result = Validate(typeId);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("not a valid type id");
    }

    // Each of these is already a key or table under [session], so a type's table would be read as it.
    [TestCase("type")]
    [TestCase("environment")]
    [TestCase("runner")]
    [TestCase("trigger")]
    [TestCase("shortcut")]
    public void Validate_AnIdTheFormatHasTaken_Fails(string typeId)
    {
        var result = Validate(typeId);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain($"'{typeId}' takes a name the [session] table already defines");
    }

    [Test]
    public void Validate_ADuplicateId_Fails()
    {
        var result = Validate("shell", "python", "shell");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("registered more than once");
    }
}
