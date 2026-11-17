using DotNetHttpFile.Parsing;

namespace DotNetHttpFile.Tests.Parsing;

public sealed class HttpFileTests
{
    private const string FilePath = "test.http";

    [Fact]
    public void ReferencedVariableNames_UrlHeaderAndBody_ReturnsTheirVariables()
    {
        HttpFile file = HttpFileParser.Parse(
            FilePath,
            "POST {{host}}/x\nAuthorization: Bearer {{token}}\n\n{ \"id\": \"{{id}}\" }\n");

        Assert.Equal(["host", "id", "token"], file.ReferencedVariableNames().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ReferencedVariableNames_FileVariableValue_ReturnsItsVariables()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "@host = {{scheme}}://localhost\n\nGET http://localhost/x\n");

        Assert.Equal(["scheme"], file.ReferencedVariableNames());
    }

    [Fact]
    public void ReferencedVariableNames_DefinedButNeverReferenced_IsNotReturned()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "@Limit = 3\n\nGET http://localhost/x\n");

        Assert.Empty(file.ReferencedVariableNames());
    }

    [Fact]
    public void ReferencedVariableNames_RequestReferenceAndDynamicVariable_AreNotReturned()
    {
        HttpFile file = HttpFileParser.Parse(
            FilePath,
            "# @name first\nGET http://localhost/a\n\n###\n\nGET http://localhost/{{first.response.body.$.id}}/{{$guid}}\n");

        Assert.Empty(file.ReferencedVariableNames());
    }

    [Fact]
    public void ReferencedVariableNames_DifferentCase_IsADifferentName()
    {
        HttpFile file = HttpFileParser.Parse(FilePath, "GET http://localhost/{{Limit}}\n");

        Assert.DoesNotContain("limit", file.ReferencedVariableNames());
    }

    [Fact]
    public void ReferencedVariableNames_AcrossRequests_ReturnsTheNamesOfAll()
    {
        HttpFile file = HttpFileParser.Parse(
            FilePath,
            "GET http://localhost/{{a}}\n\n###\n\nGET http://localhost/{{b}}\n");

        Assert.Equal(["a", "b"], file.ReferencedVariableNames().Order(StringComparer.Ordinal));
    }
}
