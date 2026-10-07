using UnityEngine;

// Editor-only regression entry point. Preserve the production one-step GPU
// harness; remove presentation frame caps for long Sv32 suites.
public static class DoomIsaRegression
{
    public static void Run()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        DoomIsaTests.Run();
    }
}
