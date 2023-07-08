using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace SwaggerEditor.Services
{
    public interface IBlobService
    {
        Task<Tuple<string, string>> UploadBlob(string localFilePath, string content);
        Task DownloadToStream(string localFilePath, MemoryStream memoryStream);
        Task<Stream?> DownloadStream(string localFilePath);
    }

    public class BlobService : IBlobService
    {
        private readonly BlobServiceClient blobServiceClient;
        private readonly BlobContainerClient blobContainerClient;
        private const string containerName = "eswplayground";

        public BlobService(string connectionString)
        {
           blobServiceClient = new BlobServiceClient(connectionString);
           blobContainerClient = CreateContainerClientAsync().GetAwaiter().GetResult();
        }

        private async Task<BlobContainerClient> CreateContainerClientAsync()
        {
            // Name the sample container based on new GUID to ensure uniqueness.
            // The container name must be lowercase.
            

            try
            {
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);

                await blobContainerClient.CreateIfNotExistsAsync();

                return blobContainerClient;
                //// Create the container
                //BlobContainerClient container = await blobServiceClient.CreateBlobContainerAsync(containerName);

                //if (await container.ExistsAsync())
                //{
                //    Console.WriteLine("Created container {0}", container.Name);
                //    return container;
                //}
            }
            catch (RequestFailedException e)
            {
                Console.WriteLine("HTTP error code {0}: {1}", e.Status, e.ErrorCode);
                Console.WriteLine(e.Message);
            }

            return null;
        }

        public async Task<Tuple<string,string>> UploadBlob(string localFilePath, string content)
        {
            
            string fileName = Path.GetFileName(localFilePath);
            BlobClient blobClient = blobContainerClient.GetBlobClient(fileName);

            //FileStream fileStream = File.OpenRead(localFilePath);
            var res = await blobClient.UploadAsync(BinaryData.FromString(content), true);

            var sasUri = GetServiceSasUriForBlob(blobClient);


            return new Tuple<string, string>($"{this.blobServiceClient.Uri}{containerName}/{localFilePath}", sasUri.ToString());

            //fileStream.Close();

        }

        public async Task<Stream?> DownloadStream(string localFilePath)
        {
            BlobClient blobClient = blobContainerClient.GetBlobClient(localFilePath);

            if (await blobClient.ExistsAsync())
            {
                var stream = await blobClient.OpenReadAsync();
                return stream; // Set the appropriate Content-Type for your data
            }

            return null;
        }

        public async Task DownloadToStream(string localFilePath, MemoryStream memoryStream)
        {
            try
            {
                string fileName = Path.GetFileName(localFilePath);

                //FileStream fileStream = File.OpenWrite(localFilePath);
                BlobClient blobClient = blobContainerClient.GetBlobClient(fileName);
                await blobClient.DownloadToAsync(memoryStream);

                //fileStream.Close();
            }
            catch (DirectoryNotFoundException ex)
            {
                // Let the user know that the directory does not exist
                Console.WriteLine($"Directory not found: {ex.Message}");
            }
        }

        private Uri GetServiceSasUriForBlob(BlobClient blobContainerClient, string storedPolicyName = null)
        {
            // Check whether this BlobClient object has been authorized with Shared Key.
            if (this.blobContainerClient.CanGenerateSasUri)
            {
                // Create a SAS token that's valid for one hour.
                BlobSasBuilder sasBuilder = new BlobSasBuilder()
                {
                    BlobContainerName = containerName,
                    BlobName = blobContainerClient.Name,
                    Resource = "b"
                };

                if (storedPolicyName == null)
                {
                    sasBuilder.ExpiresOn = DateTimeOffset.UtcNow.AddHours(1);
                    sasBuilder.SetPermissions(BlobSasPermissions.Read);
                }
                else
                {
                    sasBuilder.Identifier = storedPolicyName;
                }

                Uri sasUri = blobContainerClient.GenerateSasUri(sasBuilder);
                Console.WriteLine("SAS URI for blob is: {0}", sasUri);
                Console.WriteLine();

                return sasUri;
            }
            else
            {
                Console.WriteLine(@"BlobClient must be authorized with Shared Key 
                          credentials to create a service SAS.");
                return null;
            }
        }
    }
}
