using ArturRios.Fortuna.Data.Exports;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Data.Tests;

public sealed class PersonalDataArchiveFileNameTests
{
    [UnitTheory]
    [InlineData("receipt.pdf", "receipt.pdf")]
    [InlineData("../../evil.bat", "evil.bat")]
    [InlineData("..\\..\\evil.bat", "evil.bat")]
    [InlineData("C:\\Windows\\evil.bat", "evil.bat")]
    [InlineData("..", "attachment")]
    [InlineData(".", "attachment")]
    [InlineData("   ", "attachment")]
    public void GivenAttachmentFileName_WhenUsedAsArchiveEntry_ThenItCannotLeaveItsFolder(
        string fileName,
        string expected)
    {
        Assert.Equal(expected, EfPersonalDataArchiveBuilder.SafeFileName(fileName));
    }
}
