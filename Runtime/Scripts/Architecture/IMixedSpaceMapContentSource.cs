using System.Threading;
using System.Threading.Tasks;

namespace MixedSpace
{
    /// <summary>
    /// Supplies optional runtime map artifacts without exposing HTTP jobs or a
    /// global SDK instance to the map repository.
    /// </summary>
    public interface IMixedSpaceMapContentSource
    {
        Task<byte[]> DownloadBinaryAsync(
            int mapKey,
            CancellationToken cancellationToken = default);

        Task<byte[]> DownloadPointCloudAsync(
            int mapKey,
            CancellationToken cancellationToken = default);
    }
}
