using System.Net;
using Amazon.S3;
using Azure;
using Xunit;

namespace Storage.Vector.Tests;

public class StorageExceptionTests
{
    [Fact]
    public void StorageException_Constructors_SetsPropertiesCorrectly()
    {
        var ex1 = new StorageException();
        Assert.Equal(StorageErrorKind.Unknown, ex1.Kind);

        var ex2 = new StorageException("custom message");
        Assert.Equal("custom message", ex2.Message);
        Assert.Equal(StorageErrorKind.Unknown, ex2.Kind);

        var inner = new InvalidOperationException("inner error");
        var ex3 = new StorageException("msg with inner", inner);
        Assert.Equal("msg with inner", ex3.Message);
        Assert.Same(inner, ex3.InnerException);
        Assert.Equal(StorageErrorKind.Unknown, ex3.Kind);

        var ex4 = new StorageException(StorageErrorKind.AccessDenied, "access denied msg", inner);
        Assert.Equal(StorageErrorKind.AccessDenied, ex4.Kind);
        Assert.Equal("access denied msg", ex4.Message);
        Assert.Same(inner, ex4.InnerException);
    }

    [Theory]
    [InlineData(404, StorageErrorKind.NotFound)]
    [InlineData(401, StorageErrorKind.AccessDenied)]
    [InlineData(403, StorageErrorKind.AccessDenied)]
    [InlineData(500, StorageErrorKind.Unavailable)]
    [InlineData(503, StorageErrorKind.Unavailable)]
    [InlineData(400, StorageErrorKind.Unknown)]
    public void FromAzureException_MapsStatusCodesToErrorKind(int status, StorageErrorKind expectedKind)
    {
        var azureEx = new RequestFailedException(status, "Azure error");
        var mapped = StorageException.FromAzureException(azureEx);

        Assert.Equal(expectedKind, mapped.Kind);
        Assert.Same(azureEx, mapped.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, StorageErrorKind.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, StorageErrorKind.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, StorageErrorKind.AccessDenied)]
    [InlineData(HttpStatusCode.InternalServerError, StorageErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, StorageErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, StorageErrorKind.Unknown)]
    public void FromAwsException_MapsStatusCodesToErrorKind(HttpStatusCode statusCode, StorageErrorKind expectedKind)
    {
        var awsEx = new AmazonS3Exception("AWS error")
        {
            StatusCode = statusCode
        };
        var mapped = StorageException.FromAwsException(awsEx);

        Assert.Equal(expectedKind, mapped.Kind);
        Assert.Same(awsEx, mapped.InnerException);
    }
}
