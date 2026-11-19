namespace MixedSpace.XR
{
    /// <summary>
    /// Pure pose solver. It converts a localization observation into a world
    /// alignment value and never calls a scene object or owns Unity lifecycle.
    /// </summary>
    public interface IMixedSpaceAlignmentSolver
    {
        MixedSpaceAlignmentSolution Solve(MixedSpaceAlignmentRequest request);
    }
}

