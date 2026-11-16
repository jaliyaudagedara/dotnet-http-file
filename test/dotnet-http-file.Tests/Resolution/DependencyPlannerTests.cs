using DotNetHttpFile.Parsing;
using DotNetHttpFile.Resolution;

namespace DotNetHttpFile.Tests.Resolution;

public sealed class DependencyPlannerTests
{
    private const string FilePath = "test.http";

    [Fact]
    public void Plan_NoReferences_ReturnsTargetOnly()
    {
        HttpFile file = Parse(
            "# @name other",
            "GET https://localhost/other",
            "",
            "###",
            "GET https://localhost/items");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, file.Requests[1]);

        Assert.Equal([file.Requests[1]], plan);
    }

    [Fact]
    public void Plan_TargetUsesReferenceViaFileVariable_IncludesDependency()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "@token = {{login.response.body.$.token}}",
            "# @name items",
            "GET https://localhost/items",
            "Authorization: Bearer {{token}}");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "items"));

        Assert.Equal(["login", "items"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_ReferenceThroughNestedFileVariables_IncludesDependency()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "@token = {{login.response.body.$.token}}",
            "@authorization = Bearer {{token}}",
            "# @name items",
            "GET https://localhost/items",
            "Authorization: {{authorization}}");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "items"));

        Assert.Equal(["login", "items"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_ChainedDependencies_OrderedDependenciesFirst()
    {
        HttpFile file = Parse(
            "# @name c",
            "GET https://localhost/c/{{b.response.body.$.id}}?again={{b.response.headers.ETag}}",
            "",
            "###",
            "# @name b",
            "GET https://localhost/b/{{a.response.body.$.id}}",
            "",
            "###",
            "# @name a",
            "GET https://localhost/a");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "c"));

        Assert.Equal(["a", "b", "c"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_DependencyUsesReferenceViaFileVariable_FollowsIt()
    {
        HttpFile file = Parse(
            "# @name a",
            "GET https://localhost/a",
            "",
            "###",
            "@id = {{a.response.body.$.id}}",
            "# @name b",
            "GET https://localhost/b/{{id}}",
            "",
            "###",
            "# @name c",
            "GET https://localhost/c/{{b.response.body.$.id}}");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "c"));

        Assert.Equal(["a", "b", "c"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_SharedDependency_IncludedOnce()
    {
        HttpFile file = Parse(
            "# @name a",
            "GET https://localhost/a",
            "",
            "###",
            "# @name b",
            "GET https://localhost/b/{{a.response.body.$.id}}",
            "",
            "###",
            "# @name c",
            "GET https://localhost/c/{{a.response.body.$.id}}",
            "",
            "###",
            "# @name d",
            "GET https://localhost/d/{{b.response.body.$.id}}/{{c.response.body.$.id}}");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "d"));

        Assert.Equal(["a", "b", "c", "d"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_ReferencesInUrlHeadersAndBody_OrderedByFirstReference()
    {
        HttpFile file = Parse(
            "# @name inBody",
            "GET https://localhost/1",
            "",
            "###",
            "# @name inHeader",
            "GET https://localhost/2",
            "",
            "###",
            "# @name inUrl",
            "GET https://localhost/3",
            "",
            "###",
            "POST https://localhost/{{inUrl.response.body.*}}",
            "X-Value: {{inHeader.request.headers.Accept}}",
            "X-Again: {{inUrl.response.body.*}}",
            "",
            "{{inBody.response.body.*}}");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, file.Requests[3]);

        Assert.Equal(["inUrl", "inHeader", "inBody", null], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_VariableDefinedAfterTarget_IsNotFollowed()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "# @name items",
            "GET https://localhost/items/{{token}}",
            "",
            "###",
            "@token = {{login.response.body.$.token}}",
            "GET https://localhost/other");

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "items"));

        Assert.Equal(["items"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_Cycle_Throws()
    {
        HttpFile file = Parse(
            "# @name a",
            "GET https://localhost/{{b.response.body.$.id}}",
            "",
            "###",
            "# @name b",
            "GET https://localhost/{{a.response.body.$.id}}");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => DependencyPlanner.Plan(file, Request(file, "a")));

        Assert.Equal("Request dependency cycle: a -> b -> a.", exception.Message);
    }

    [Fact]
    public void Plan_SelfReference_Throws()
    {
        HttpFile file = Parse(
            "# @name a",
            "GET https://localhost/{{a.response.body.$.id}}");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => DependencyPlanner.Plan(file, Request(file, "a")));

        Assert.Equal("Request dependency cycle: a -> a.", exception.Message);
    }

    [Fact]
    public void Plan_CycleAmongDependencies_NamesOnlyTheCycle()
    {
        HttpFile file = Parse(
            "@fromA = {{a.response.body.$.id}}",
            "# @name a",
            "GET https://localhost/{{b.response.body.$.id}}",
            "",
            "###",
            "# @name b",
            "GET https://localhost/{{fromA}}",
            "",
            "###",
            "GET https://localhost/{{b.response.body.$.id}}");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => DependencyPlanner.Plan(file, file.Requests[2]));

        Assert.Equal("Request dependency cycle: b -> a -> b.", exception.Message);
    }

    [Fact]
    public void Plan_UnknownRequestName_Throws()
    {
        HttpFile file = Parse(
            "# @name items",
            "GET https://localhost/items",
            "Authorization: Bearer {{x.response.body.$.token}}");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => DependencyPlanner.Plan(file, Request(file, "items")));

        Assert.Equal("Request 'x' referenced on line 3 does not exist.", exception.Message);
    }

    [Fact]
    public void Plan_UnknownRequestNameInFileVariable_ReportsVariableLine()
    {
        HttpFile file = Parse(
            "@token = {{x.response.body.$.token}}",
            "",
            "GET https://localhost/items/{{token}}");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => DependencyPlanner.Plan(file, file.Requests[0]));

        Assert.Equal("Request 'x' referenced on line 1 does not exist.", exception.Message);
    }

    [Fact]
    public void Plan_UnknownRequestNameInBody_ReportsExpressionLine()
    {
        HttpFile file = Parse(
            "POST https://localhost/items",
            "Accept: */*",
            "",
            "{",
            "  \"id\": \"{{x.response.body.$.id}}\"",
            "}");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => DependencyPlanner.Plan(file, file.Requests[0]));

        Assert.Equal("Request 'x' referenced on line 5 does not exist.", exception.Message);
    }

    [Fact]
    public void Plan_RequestNameInDifferentCase_Throws()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "GET https://localhost/{{Login.response.body.$.id}}");

        HttpFileException exception = Assert.Throws<HttpFileException>(
            () => DependencyPlanner.Plan(file, file.Requests[1]));

        Assert.Equal("Request 'Login' referenced on line 5 does not exist.", exception.Message);
    }

    [Fact]
    public void Plan_OverriddenFileVariable_LeavesOutTheRequestOnlyItsDefinitionRefersTo()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "@token = {{login.response.body.$.token}}",
            "# @name items",
            "GET https://localhost/items",
            "Authorization: Bearer {{token}}");
        Dictionary<string, string> overrides = new() { ["token"] = "given" };

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "items"), overrides);

        Assert.Equal(["items"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_OverriddenNestedFileVariable_LeavesOutTheRequestOnlyItsDefinitionRefersTo()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "@token = {{login.response.body.$.token}}",
            "@authorization = Bearer {{token}}",
            "# @name items",
            "GET https://localhost/items",
            "Authorization: {{authorization}}");
        Dictionary<string, string> overrides = new() { ["token"] = "given" };

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "items"), overrides);

        Assert.Equal(["items"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_OverriddenFileVariableAndDirectReference_KeepsTheDependency()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "@token = {{login.response.body.$.token}}",
            "# @name items",
            "GET https://localhost/items?user={{login.response.body.$.user}}",
            "Authorization: Bearer {{token}}");
        Dictionary<string, string> overrides = new() { ["token"] = "given" };

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "items"), overrides);

        Assert.Equal(["login", "items"], plan.Select(request => request.Name));
    }

    [Fact]
    public void Plan_OverrideOfAnotherName_StillFollowsTheFileVariable()
    {
        HttpFile file = Parse(
            "# @name login",
            "POST https://localhost/login",
            "",
            "###",
            "@token = {{login.response.body.$.token}}",
            "# @name items",
            "GET https://localhost/items",
            "Authorization: Bearer {{token}}");
        Dictionary<string, string> overrides = new() { ["Token"] = "given" };

        IReadOnlyList<RequestDefinition> plan = DependencyPlanner.Plan(file, Request(file, "items"), overrides);

        Assert.Equal(["login", "items"], plan.Select(request => request.Name));
    }

    private static HttpFile Parse(params string[] lines) =>
        HttpFileParser.Parse(FilePath, string.Join("\r\n", lines) + "\r\n");

    private static RequestDefinition Request(HttpFile file, string name) =>
        file.Requests.Single(request => request.Name == name);
}
