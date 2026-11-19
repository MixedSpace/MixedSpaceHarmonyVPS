using System;
using System.Threading.Tasks;

namespace MixedSpace.XR
{
    /// <summary>
    /// Optional scene-presentation use case for sparse map geometry. It is kept
    /// outside the runtime map repository so native data ownership and rendering
    /// can evolve independently.
    /// </summary>
    public sealed class MixedSpaceMapPreviewService
    {
        private readonly IMixedSpaceMapContentSource m_ContentSource;

        public MixedSpaceMapPreviewService(IMixedSpaceMapContentSource contentSource)
        {
            m_ContentSource = contentSource ??
                throw new ArgumentNullException(nameof(contentSource));
        }

        public async Task<bool> PresentIfRequestedAsync(MixedSpaceMapBinding binding)
        {
            if (!binding)
                return false;

            MixedSpaceMapDefinition definition = binding.Definition;
            bool shouldDownload = definition.ContentPolicy.PresentPreviewAtRuntime ||
                (definition.ContentPolicy.Location ==
                    MixedSpaceMapContentLocation.ProviderManaged &&
                 string.Equals(
                    definition.ProviderKey,
                    MixedSpaceProviderIds.OnDevice,
                    StringComparison.Ordinal) &&
                  !binding.Preview);
            if (!shouldDownload)
                return true;

            try
            {
                byte[] pointCloud = await m_ContentSource.DownloadPointCloudAsync(
                    definition.Key);
                if (pointCloud == null || pointCloud.Length == 0)
                    return false;

                MixedSpacePointCloudView view = MixedSpacePointCloudPresenter.Show(
                    binding,
                    MixedSpacePointCloudVisibility.BuildAndWorkspace,
                    randomColor: true);
                return view.PresentDocument(pointCloud, definition.Label);
            }
            catch (Exception exception)
            {
                MixedSpaceLogger.LogError(
                    $"Map {definition.Key} point-cloud presentation failed: " +
                    exception.Message);
                return false;
            }
        }
    }
}
