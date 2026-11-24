using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;

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

    [Fact]
    public void ResourceStrings_InvariantCulture_AreValidCompositeFormats()
    {
        ResourceSet? resourceSet = Resources.ResourceManager.GetResourceSet(
            CultureInfo.InvariantCulture,
            createIfNotExists: true,
            tryParents: false);
        Assert.NotNull(resourceSet);

        List<string> invalidNames = [];

        foreach (DictionaryEntry entry in resourceSet)
        {
            if (entry.Value is string value && !IsValidCompositeFormat(value))
            {
                invalidNames.Add((string)entry.Key);
            }
        }

        Assert.Empty(invalidNames);
    }

    private static bool IsValidCompositeFormat(string value)
    {
        try
        {
            CompositeFormat.Parse(value);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
