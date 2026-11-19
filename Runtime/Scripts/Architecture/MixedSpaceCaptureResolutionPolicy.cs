using System;
using System.Collections.Generic;
using UnityEngine;

namespace MixedSpace
{
    /// <summary>
    /// Pure resolution selection shared by platform adapters. The result is an
    /// index into the original candidate list, so a platform retains ownership of
    /// its native configuration and applies it only after selection succeeds.
    /// </summary>
    public static class MixedSpaceCaptureResolutionPolicy
    {
        /// <summary>
        /// Chooses the closest pixel count, preserving the existing HD-selection
        /// behavior. Equal scores keep the first advertised configuration rather
        /// than silently changing frame rate or another platform-specific field.
        /// Invalid candidate sizes are ignored; -1 means no usable candidate.
        /// </summary>
        public static int FindNearest(IReadOnlyList<Vector2Int> candidates, Vector2Int desired)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (desired.x <= 0 || desired.y <= 0)
                throw new ArgumentOutOfRangeException(nameof(desired));

            long targetArea = (long)desired.x * desired.y;
            long bestDistance = long.MaxValue;
            int selectedIndex = -1;
            for (int index = 0; index < candidates.Count; index++)
            {
                Vector2Int candidate = candidates[index];
                if (candidate.x <= 0 || candidate.y <= 0)
                    continue;

                // Promote before multiplication; modern camera dimensions must
                // not overflow a 32-bit score or require a synthetic huge target.
                long distance = Math.Abs((long)candidate.x * candidate.y - targetArea);
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                selectedIndex = index;
            }

            return selectedIndex;
        }

        /// <summary>
        /// Chooses the greatest positive pixel count directly. Equal resolutions
        /// retain the provider's ordering, as in FindNearest.
        /// </summary>
        public static int FindLargest(IReadOnlyList<Vector2Int> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            long bestArea = 0;
            int selectedIndex = -1;
            for (int index = 0; index < candidates.Count; index++)
            {
                Vector2Int candidate = candidates[index];
                if (candidate.x <= 0 || candidate.y <= 0)
                    continue;

                long area = (long)candidate.x * candidate.y;
                if (area <= bestArea)
                    continue;

                bestArea = area;
                selectedIndex = index;
            }

            return selectedIndex;
        }
    }
}
