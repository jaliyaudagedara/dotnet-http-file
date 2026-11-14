using System.Globalization;
using System.Resources;

namespace DotNetHttpFile.Tests;

public sealed class ResourcesTests
{
    [Fact]
    public void ResourceManager_InvariantCulture_LoadsEmbeddedResources()
    {
        ResourceSet? resourceSet = Resources.ResourceManager.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false);

        Assert.NotNull(resourceSet);
    }
}
