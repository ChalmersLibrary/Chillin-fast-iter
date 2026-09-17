using System;
using System.Collections.Generic;
using System.IO;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Chalmers.ILL.Models;
using Chalmers.ILL.Configuration;

namespace Chalmers.ILL.MediaItems
{
    // Was Microsoft.WindowsAzure.Storage (legacy SDK) - .NET Framework-only, its dependency
    // chain (Microsoft.Data.OData etc.) can't target net10.0. Rewritten against Azure.Storage.Blobs
    // (fas 8, pulled forward - the old package physically couldn't restore for the TFM switch).
    public class BlobStorageMediaItemManager : IMediaItemManager
    {
        private const string containerName = "chillinmedia";

        private IChillinConfiguration _configuration;

        public BlobStorageMediaItemManager(IChillinConfiguration configuration)
        {
            _configuration = configuration;
        }

        private BlobContainerClient GetContainer()
        {
            var container = new BlobContainerClient(_configuration.StorageConnectionString, containerName);
            container.CreateIfNotExists();
            return container;
        }

        public MediaItemModel CreateMediaItem(string name, int orderItemNodeId, string orderId, Stream data, string contentType)
        {
            // Generate a UUID which we will use as identifier for the stored object.
            var id = Guid.NewGuid();

            var container = GetContainer();
            var blob = container.GetBlobClient(id.ToString());

            // Store the object.
            data.Position = 0;
            blob.Upload(data, overwrite: true);

            // Store the metadata.
            var createDate = DateTime.Now;
            var metadata = new Dictionary<string, string>
            {
                ["name"] = Uri.EscapeDataString(name),
                ["orderItemNodeId"] = orderItemNodeId.ToString(),
                ["createDate"] = createDate.ToString("o")
            };
            blob.SetMetadata(metadata);
            blob.SetHttpHeaders(new BlobHttpHeaders { ContentType = contentType });

            // Create the stored object which we will return.
            var storedMediaItem = new MediaItemModel();
            PopulateStoredMediaItemFromBlob(storedMediaItem, blob);

            return storedMediaItem;
        }

        public IList<MediaItemIdAndOrderItemId> DeleteOlderThan(DateTime date)
        {
            var container = GetContainer();

            var ret = new List<MediaItemIdAndOrderItemId>();
            foreach (var oldMediaItem in container.GetBlobs(BlobTraits.Metadata))
            {
                if (Convert.ToDateTime(oldMediaItem.Metadata["createDate"]) < date)
                {
                    ret.Add(new MediaItemIdAndOrderItemId(oldMediaItem.Name, Convert.ToInt32(oldMediaItem.Metadata["orderItemNodeId"])));
                    container.GetBlobClient(oldMediaItem.Name).DeleteIfExists();
                }
            }

            return ret;
        }

        public MediaItemModel GetOne(string id)
        {
            var container = GetContainer();
            var blob = container.GetBlobClient(id.ToString());

            var blobContents = new MemoryStream();
            blob.DownloadTo(blobContents);

            // Create the stored object which we will return.
            var storedMediaItem = new MediaItemModel();
            storedMediaItem.Data = blobContents;
            storedMediaItem.Data.Seek(0, SeekOrigin.Begin);
            PopulateStoredMediaItemFromBlob(storedMediaItem, blob);

            return storedMediaItem;
        }

        #region Private methods

        private void PopulateStoredMediaItemFromBlob(MediaItemModel mediaItem, BlobClient blob)
        {
            var properties = blob.GetProperties().Value;
            mediaItem.Id = blob.Name;
            // The name was written through Uri.EscapeDataString below (Azure blob metadata values
            // must be ASCII) - reading it back without the matching UnescapeDataString left names
            // percent-encoded in the UI (fas 0a latent defect).
            mediaItem.Name = Uri.UnescapeDataString(properties.Metadata["name"]);
            mediaItem.OrderItemNodeId = Convert.ToInt32(properties.Metadata["orderItemNodeId"]);
            mediaItem.Url = MediaItemUrlBuilder.Build(_configuration.BaseUrl, mediaItem.Id);
            mediaItem.CreateDate = Convert.ToDateTime(properties.Metadata["createDate"]);
            mediaItem.ContentType = properties.ContentType;
        }

        #endregion
    }
}
