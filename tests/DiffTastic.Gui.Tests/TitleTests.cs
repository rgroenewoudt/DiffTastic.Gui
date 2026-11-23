using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Tests;

public class TitleTests
{
    [Theory]
    [InlineData(@"C:\src\app\a.cs", @"C:\src\app\b.cs", "a.cs")]
    [InlineData(@"C:\src\app\a.cs", @"C:\src\lib\a.cs", @"app\a.cs")]
    [InlineData(@"C:\src\app\a.cs", @"c:\SRC\app\b.cs", "a.cs")]
    [InlineData(@"C:\src\app\a.cs", @"C:\src\app\a.cs", "a.cs")]
    [InlineData(@"C:\src\app1\a.cs", @"C:\src\app2\a.cs", @"app1\a.cs")]
    [InlineData(@"C:\a.cs", @"D:\a.cs", @"C:\a.cs")]
    [InlineData(@"C:\src\a.cs", null, @"C:\src\a.cs")]
    public void WithoutCommonDirectory_keeps_the_differing_part(string path, string? other, string expected) =>
        Assert.Equal(expected, MainWindowViewModel.WithoutCommonDirectory(path, other));
}
