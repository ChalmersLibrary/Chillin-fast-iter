using System.Collections.Generic;

namespace Chalmers.ILL.Migration
{
    public class MigrationResult
    {
        public int SourceOrderCount;
        public int WrittenFileCount;
        public int SourceLogItemCount;
        public int SourceAttachmentCount;
        public int MaxNodeId;

        public List<int> SpotCheckedNodeIds { get; } = new List<int>();
        public List<string> Errors { get; } = new List<string>();

        public long? ElasticSearchDocCount;

        public bool Success => Errors.Count == 0;
    }
}
