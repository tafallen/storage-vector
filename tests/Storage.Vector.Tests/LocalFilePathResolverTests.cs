using System;
using System.IO;
using Xunit;

namespace Storage.Vector.Tests;

public class LocalFilePathResolverTests
{
    private readonly string _rootPath;

    public LocalFilePathResolverTests()
    {
        _rootPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "resolver-tests-" + Guid.NewGuid()));
    }

    [Fact]
    public void ResolveContained_ValidContainerAndKey_ReturnsAbsolutePath()
    {
        var resolved = LocalFilePathResolver.ResolveContained(_rootPath, "media", "images/pic.png");

        var expected = Path.GetFullPath(Path.Combine(_rootPath, "media", "images", "pic.png"));
        Assert.Equal(expected, Path.GetFullPath(resolved));
    }

    [Fact]
    public void ResolveContained_RootPathWithTrailingSeparator_ReturnsAbsolutePath()
    {
        var rootWithSep = _rootPath + Path.DirectorySeparatorChar;
        var resolved = LocalFilePathResolver.ResolveContained(rootWithSep, "media", "file.txt");

        var expected = Path.GetFullPath(Path.Combine(_rootPath, "media", "file.txt"));
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(null, "key.txt")]
    [InlineData("", "key.txt")]
    [InlineData("   ", "key.txt")]
    [InlineData("container", null)]
    [InlineData("container", "")]
    [InlineData("container", "   ")]
    public void ResolveContained_NullOrWhitespace_ThrowsArgumentException(string? container, string? key)
    {
        Assert.Throws<ArgumentException>(() =>
            LocalFilePathResolver.ResolveContained(_rootPath, container!, key!));
    }

    [Fact]
    public void ResolveContained_PathTraversalAttempt_ThrowsStorageExceptionAccessDenied()
    {
        var ex = Assert.Throws<StorageException>(() =>
            LocalFilePathResolver.ResolveContained(_rootPath, "media", "../../secret.txt"));

        Assert.Equal(StorageErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public void ResolveContained_DriveLetterTraversalAttempt_ThrowsStorageExceptionAccessDenied()
    {
        var ex = Assert.Throws<StorageException>(() =>
            LocalFilePathResolver.ResolveContained(_rootPath, "media", "C:/Windows/System32/cmd.exe"));

        Assert.Equal(StorageErrorKind.AccessDenied, ex.Kind);
    }

    [Fact]
    public void ResolveContained_RootRelativeTraversalAttempt_ThrowsStorageExceptionAccessDenied()
    {
        var ex1 = Assert.Throws<StorageException>(() =>
            LocalFilePathResolver.ResolveContained(_rootPath, "/media", "file.txt"));
        Assert.Equal(StorageErrorKind.AccessDenied, ex1.Kind);

        var ex2 = Assert.Throws<StorageException>(() =>
            LocalFilePathResolver.ResolveContained(_rootPath, "media", "/file.txt"));
        Assert.Equal(StorageErrorKind.AccessDenied, ex2.Kind);

        var ex3 = Assert.Throws<StorageException>(() =>
            LocalFilePathResolver.ResolveContained(_rootPath, "\\media", "file.txt"));
        Assert.Equal(StorageErrorKind.AccessDenied, ex3.Kind);

        var ex4 = Assert.Throws<StorageException>(() =>
            LocalFilePathResolver.ResolveContained(_rootPath, "media", "\\file.txt"));
        Assert.Equal(StorageErrorKind.AccessDenied, ex4.Kind);
    }
}
