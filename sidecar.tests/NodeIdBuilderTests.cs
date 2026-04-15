using VbNetSidecar.Extraction;

namespace VbNetSidecar.Tests;

public class NodeIdBuilderTests
{
    // MakeId — basic single-part
    [Fact]
    public void MakeId_SinglePart_ReturnsLowercased()
    {
        Assert.Equal("dbclasses", NodeIdBuilder.MakeId("dbclasses"));
    }

    [Fact]
    public void MakeId_TwoParts_JoinsWithUnderscore()
    {
        Assert.Equal("dbclasses_dbconnection", NodeIdBuilder.MakeId("dbclasses", "DBConnection"));
    }

    [Fact]
    public void MakeId_ThreeParts_JoinsAll()
    {
        Assert.Equal("dbclasses_dbconnection_logonid", NodeIdBuilder.MakeId("dbclasses", "DBConnection", "LogonID"));
    }

    [Fact]
    public void MakeId_DesignerStem_ProducesCorrectId()
    {
        Assert.Equal("frmoverview_designer_frmoverview", NodeIdBuilder.MakeId("frmoverview_designer", "frmOverview"));
    }

    [Fact]
    public void MakeId_MdiCpr_ProducesCorrectId()
    {
        Assert.Equal("mdicpr_mdicpr", NodeIdBuilder.MakeId("mdicpr", "mdiCPR"));
    }

    [Fact]
    public void MakeId_ConstructorCase_ProducesCorrectId()
    {
        Assert.Equal("frmabout_frmabout_new", NodeIdBuilder.MakeId("frmabout", "frmAbout", "New"));
    }

    [Fact]
    public void MakeId_ModuleCase_ProducesCorrectId()
    {
        Assert.Equal("modglobal_modglobal", NodeIdBuilder.MakeId("modglobal", "modGlobal"));
    }

    [Fact]
    public void MakeId_EmptyPartsAreSkipped()
    {
        Assert.Equal("a_b", NodeIdBuilder.MakeId("a", "", "b"));
    }

    [Fact]
    public void MakeId_NullEquivalentEmptyPartsAreSkipped()
    {
        Assert.Equal("hello_world", NodeIdBuilder.MakeId("hello", "", "", "world"));
    }

    [Fact]
    public void MakeId_LeadingTrailingDotsAndUnderscoresStripped()
    {
        Assert.Equal("foo_bar", NodeIdBuilder.MakeId(".foo.", "_bar_"));
    }

    [Fact]
    public void MakeId_BracketEscapedIdentifier_FileStemCase()
    {
        // ValueText already strips brackets — so "File" from [File] becomes "file"
        Assert.Equal("test_file", NodeIdBuilder.MakeId("test", "File"));
    }

    [Fact]
    public void MakeId_AllPartsEmpty_ReturnsEmptyString()
    {
        Assert.Equal("", NodeIdBuilder.MakeId("", ""));
    }

    // GetFileStemId — file path -> stem ID
    [Fact]
    public void GetFileStemId_SimpleFilename_ReturnsLowercaseStem()
    {
        Assert.Equal("dbclasses", NodeIdBuilder.GetFileStemId("Source/CPR/DBClasses.vb"));
    }

    [Fact]
    public void GetFileStemId_DesignerFile_PreservesDesignerSuffix()
    {
        Assert.Equal("frmoverview_designer", NodeIdBuilder.GetFileStemId("Source/CPR/frmOverview.Designer.vb"));
    }

    [Fact]
    public void GetFileStemId_MdiFile_Lowercased()
    {
        Assert.Equal("mdicpr", NodeIdBuilder.GetFileStemId("Source/CPR/mdiCPR.vb"));
    }

    [Fact]
    public void GetFileStemId_AssemblyInfo_Lowercased()
    {
        Assert.Equal("assemblyinfo", NodeIdBuilder.GetFileStemId("Source/CPR/AssemblyInfo.vb"));
    }

    [Fact]
    public void GetFileStemId_WindowsPath_DesignerFile()
    {
        Assert.Equal("frmmonthendfilter_designer", NodeIdBuilder.GetFileStemId(@"C:\Users\test\FrmMonthEndFilter.Designer.vb"));
    }
}
