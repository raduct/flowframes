using Flowframes.Data;
using Flowframes.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace Flowframes.Media
{
    class GetMediaResolutionCached
    {
        private static readonly Dictionary<QueryInfo, Size> cache = new Dictionary<QueryInfo, Size>();

        public static async Task<Size> GetSizeAsync(string path)
        {
            Logger.Log($"Getting media resolution ({path})", true);

            long filesize = IoUtils.GetPathSize(path);
            QueryInfo hash = new QueryInfo(path, filesize);

            if (filesize > 0 && cache.TryGetValue(hash, out Size size))
            {
                Logger.Log($"Cache contains this hash, using cached value.", true);
                return size;
            }

            Logger.Log($"Hash not cached, reading resolution.", true);

            size = await IoUtils.GetVideoOrFramesRes(path);

            if (size.Width > 0 && size.Height > 0)
            {
                Logger.Log($"Adding hash with value {size} to cache.", true);
                cache.Add(hash, size);
            }

            return size;
        }

        public static void Clear()
        {
            cache.Clear();
        }
    }
}
