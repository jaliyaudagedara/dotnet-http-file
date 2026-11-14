namespace DotNetHttpFile.Tests;

public sealed class ExitCodesTests
{
    [Fact]
    public void ExitCodes_Values_MatchSpec()
    {
        int[] expected = [0, 1, 2, 130];

        int[] actual = [ExitCodes.Success, ExitCodes.ErrorStatus, ExitCodes.NotSent, ExitCodes.Cancelled];

        Assert.Equal(expected, actual);
    }
}
