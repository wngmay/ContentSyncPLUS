using ClassIsland.RemoteContentSync.Utils;
using Xunit;

namespace ClassIsland.RemoteContentSync.Tests;

public class AtomicFileTests
{
    [Fact]
    public void Sanitize_ReplacesInvalidFileNameChars()
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(c, AtomicFile.Sanitize("a" + c + "b"));
        }
    }

    [Fact]
    public void Sanitize_TrimsWhitespace()
    {
        Assert.Equal("a b", AtomicFile.Sanitize("  a b  "));
    }

    [Fact]
    public void Write_CreatesFile_WithContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cis-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dest = Path.Combine(dir, "out.json");
            AtomicFile.Write(dest, temp => File.WriteAllText(temp, "hello"));

            Assert.True(File.Exists(dest));
            Assert.Equal("hello", File.ReadAllText(dest));
            Assert.False(File.Exists(dest + ".tmp")); // 临时文件已清理
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cis-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var dest = Path.Combine(dir, "out.json");
            File.WriteAllText(dest, "old");
            AtomicFile.Write(dest, temp => File.WriteAllText(temp, "new"));

            Assert.Equal("new", File.ReadAllText(dest));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
