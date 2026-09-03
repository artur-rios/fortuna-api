using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using ArturRios.Fortuna.Integration.Storage;
using ArturRios.Util.Test.Attributes;

namespace ArturRios.Fortuna.Integration.Tests;

public sealed class S3AttachmentStoreTests
{
    [UnitFact]
    public async Task GivenAttachment_WhenWrittenReadAndDeleted_ThenRequestsUseConfiguredBucketAndKey()
    {
        using var client = new StubS3Client();
        var store = new S3AttachmentStore(client, "receipts");
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("receipt"));

        await store.WriteAsync("user/receipt.txt", content, CancellationToken.None);
        await using var result = await store.OpenReadAsync("user/receipt.txt", CancellationToken.None);
        await store.DeleteAsync("user/receipt.txt", CancellationToken.None);

        using var reader = new StreamReader(result);
        Assert.Equal("stored", await reader.ReadToEndAsync(CancellationToken.None));
        Assert.Equal("receipts", client.PutRequest?.BucketName);
        Assert.Equal("user/receipt.txt", client.PutRequest?.Key);
        Assert.False(client.PutRequest!.AutoCloseStream);
        Assert.Equal(("receipts", "user/receipt.txt"), client.GetRequest!.Value);
        Assert.Equal(("receipts", "user/receipt.txt"), client.DeleteRequest!.Value);
    }

    [UnitFact]
    public async Task GivenReachableBucket_WhenHealthIsChecked_ThenItIsHealthy()
    {
        using var client = new StubS3Client();
        var store = new S3AttachmentStore(client, "receipts");

        Assert.True(await store.IsHealthyAsync(CancellationToken.None));
        Assert.Equal("receipts", client.HealthBucket);
    }

    [UnitFact]
    public async Task GivenUnavailableBucket_WhenHealthIsChecked_ThenItIsUnhealthy()
    {
        using var client = new StubS3Client { FailHealth = true };
        var store = new S3AttachmentStore(client, "receipts");

        Assert.False(await store.IsHealthyAsync(CancellationToken.None));
    }

    [UnitTheory]
    [InlineData("")]
    [InlineData("/absolute")]
    [InlineData("../parent")]
    public async Task GivenUnsafeKey_WhenWriting_ThenInputIsRejected(string key)
    {
        using var client = new StubS3Client();
        var store = new S3AttachmentStore(client, "receipts");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.WriteAsync(key, new MemoryStream([1]), CancellationToken.None));
    }

    private sealed class StubS3Client()
        : AmazonS3Client(new AnonymousAWSCredentials(), RegionEndpoint.USEast1)
    {
        public PutObjectRequest? PutRequest { get; private set; }
        public (string Bucket, string Key)? GetRequest { get; private set; }
        public (string Bucket, string Key)? DeleteRequest { get; private set; }
        public string? HealthBucket { get; private set; }
        public bool FailHealth { get; init; }

        public override Task<PutObjectResponse> PutObjectAsync(
            PutObjectRequest request,
            CancellationToken cancellationToken = default)
        {
            PutRequest = request;
            return Task.FromResult(new PutObjectResponse());
        }

        public override Task<GetObjectResponse> GetObjectAsync(
            string bucketName,
            string key,
            CancellationToken cancellationToken = default)
        {
            GetRequest = (bucketName, key);
            return Task.FromResult(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("stored"))
            });
        }

        public override Task<DeleteObjectResponse> DeleteObjectAsync(
            string bucketName,
            string key,
            CancellationToken cancellationToken = default)
        {
            DeleteRequest = (bucketName, key);
            return Task.FromResult(new DeleteObjectResponse());
        }

        public override Task<GetBucketAclResponse> GetBucketAclAsync(
            GetBucketAclRequest request,
            CancellationToken cancellationToken = default)
        {
            HealthBucket = request.BucketName;
            return FailHealth
                ? Task.FromException<GetBucketAclResponse>(new AmazonS3Exception("offline"))
                : Task.FromResult(new GetBucketAclResponse());
        }
    }
}
