namespace Chalmers.ILL.MediaItems
{
    // Shared by BlobStorageMediaItemManager and Isolated.FileMediaItemManager (fas 6, isolerat läge
    // steg A) so the URL a media item is served at can never drift between the two implementations.
    // The path is the legacy Umbraco surface alias (RouteConfig.cs's LegacyUmbracoSurfaceAlias),
    // which must be preserved permanently - see MediaItemSurfaceController.
    public static class MediaItemUrlBuilder
    {
        public static string Build(string baseUrl, string mediaItemId) =>
            baseUrl + "umbraco/surface/MediaItemSurface/GetMediaItem/" + mediaItemId;
    }
}
