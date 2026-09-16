using System;
using System.Collections.Generic;
using System.IO;
using Chalmers.ILL.Configuration;
using Chalmers.ILL.MediaItems;
using Chalmers.ILL.Models;
using Newtonsoft.Json;

namespace Chalmers.ILL.Isolated
{
    // File-based IMediaItemManager for isolated mode (fas 6, isolerat läge steg A). Mirrors
    // BlobStorageMediaItemManager's four metadata fields (name, orderItemNodeId, createDate,
    // contentType) as a JSON sidecar file next to the payload, under DataPath/media. URL building
    // is shared via MediaItemUrlBuilder so the two implementations can't drift.
    public class FileMediaItemManager : IMediaItemManager
    {
        private readonly IChillinConfiguration _config;

        public FileMediaItemManager(IChillinConfiguration config)
        {
            _config = config;
        }

        private string MediaDirectory => Path.Combine(_config.DataPath, "media");
        private string PayloadPath(string id) => Path.Combine(MediaDirectory, id);
        private string MetaPath(string id) => Path.Combine(MediaDirectory, id + ".meta.json");

        public MediaItemModel CreateMediaItem(string name, int orderItemNodeId, string orderId, Stream data, string contentType)
        {
            Directory.CreateDirectory(MediaDirectory);

            var id = Guid.NewGuid().ToString();

            data.Position = 0;
            using (var file = File.Create(PayloadPath(id)))
            {
                data.CopyTo(file);
            }

            var meta = new MediaItemMeta
            {
                Name = name,
                OrderItemNodeId = orderItemNodeId,
                CreateDate = DateTime.Now,
                ContentType = contentType
            };
            File.WriteAllText(MetaPath(id), JsonConvert.SerializeObject(meta));

            return ToModel(id, meta);
        }

        public IList<MediaItemIdAndOrderItemId> DeleteOlderThan(DateTime date)
        {
            var ret = new List<MediaItemIdAndOrderItemId>();

            if (!Directory.Exists(MediaDirectory))
                return ret;

            foreach (var metaFile in Directory.GetFiles(MediaDirectory, "*.meta.json"))
            {
                var id = Path.GetFileName(metaFile).Replace(".meta.json", "");
                var meta = JsonConvert.DeserializeObject<MediaItemMeta>(File.ReadAllText(metaFile));

                if (meta.CreateDate < date)
                {
                    ret.Add(new MediaItemIdAndOrderItemId(id, meta.OrderItemNodeId));
                    File.Delete(metaFile);
                    if (File.Exists(PayloadPath(id)))
                        File.Delete(PayloadPath(id));
                }
            }

            return ret;
        }

        public MediaItemModel GetOne(string id)
        {
            var meta = JsonConvert.DeserializeObject<MediaItemMeta>(File.ReadAllText(MetaPath(id)));
            var model = ToModel(id, meta);

            var data = new MemoryStream(File.ReadAllBytes(PayloadPath(id)));
            data.Seek(0, SeekOrigin.Begin);
            model.Data = data;

            return model;
        }

        private MediaItemModel ToModel(string id, MediaItemMeta meta) => new MediaItemModel
        {
            Id = id,
            Name = meta.Name,
            OrderItemNodeId = meta.OrderItemNodeId,
            Url = MediaItemUrlBuilder.Build(_config.BaseUrl, id),
            CreateDate = meta.CreateDate,
            ContentType = meta.ContentType
        };

        private class MediaItemMeta
        {
            public string Name { get; set; }
            public int OrderItemNodeId { get; set; }
            public DateTime CreateDate { get; set; }
            public string ContentType { get; set; }
        }
    }
}
