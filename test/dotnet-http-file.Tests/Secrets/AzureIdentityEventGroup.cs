namespace DotNetHttpFile.Tests.Secrets;

/// <summary>
/// The tests that raise or listen for Azure.Identity events. The event source and its listeners are process-wide,
/// so these tests run alone: an event one test raises would otherwise reach another test's log.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AzureIdentityEventGroup
{
    public const string Name = "Azure.Identity events";
}
