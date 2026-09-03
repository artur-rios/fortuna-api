using Amazon.S3;
using Amazon.S3.Model;

namespace ArturRios.Fortuna.Integration.Storage;

public sealed class S3AttachmentStore(IAmazonS3 client, string bucket) : IAttachmentStore
{
    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        Validate(key);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false
        }, cancellationToken);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        Validate(key);
        var response = await client.GetObjectAsync(bucket, key, cancellationToken);
        var copy = new MemoryStream();
        await response.ResponseStream.CopyToAsync(copy, cancellationToken);
        copy.Position = 0;
        response.Dispose();
        return copy;
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Validate(key);
        await client.DeleteObjectAsync(bucket, key, cancellationToken);
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.GetBucketAclAsync(new GetBucketAclRequest { BucketName = bucket }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception)
        {
            return false;
        }
    }

    private static void Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('/') || key.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("An attachment key must be a safe relative key.", nameof(key));
        }
    }
}
