using DotNetHttpFile.Secrets;

namespace DotNetHttpFile.Tests.Secrets;

public sealed class SecretRequestTests
{
    // The generated record text would print the definition, which can hold an encrypted value.
    [Fact]
    public void ToString_Always_ReturnsOnlyVariableName()
    {
        SecretRequest request = SecretRequestFactory.Create(
            "dpapiValue",
            """{ "provider": "Encrypted", "value": "AQAAANCMnd8BFdERjHoAwE" }""",
            "folder");

        string text = request.ToString();

        Assert.Equal("dpapiValue", text);
    }
}
