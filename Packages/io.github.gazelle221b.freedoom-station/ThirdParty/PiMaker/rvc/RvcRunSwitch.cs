using UdonSharp;
using UnityEngine;
using VRC.Udon;

// RvcRunSwitch - drop-in replacement for the external PiMaker "Dial" type
// referenced by NixControl.cs. Mirrors the used Dial contract (from
// PiMaker/VRChatUnityThings Assets/_Dial/Dial.cs):
//   - CurrentState: committed state.
//   - NextState: pending state, set BEFORE "DialEnable" is sent; NixControl.DialEnable
//     reads NextState to decide reset (0) vs run (1) vs linux-booted (2).
//   - SetState(state, notify): notify=true fires Controller.SendCustomEvent("DialEnable")
//     (Dial fires events only when transition is true); state is committed afterwards.
// Interact() toggles 0 <-> 1 and announces, so the existing NixControl.DialEnable
// handles the state machine exactly as it does with the analog dial.
public class RvcRunSwitch : UdonSharpBehaviour
{
    public int CurrentState = 0;
    public int NextState = 0;

    // UdonBehaviour that receives "DialEnable" (e.g. the NixControl UdonBehaviour).
    public UdonBehaviour Controller;

    // World interact: toggle 0 <-> 1 with event, like Dial.DesktopInteract().
    public override void Interact()
    {
        SetState(CurrentState == 1 ? 0 : 1, true);
    }

    // notify=false is a silent commit (Dial: "otherwise the rotation is just visual").
    // This is what NixControl._BootLinuxSecondary uses: Dial.SetState(1, false).
    public void SetState(int state, bool notify)
    {
        if (state < 0) return;

        NextState = state;
        if (notify && Controller != null)
        {
            Controller.SendCustomEvent("DialEnable");
        }
        CurrentState = state;
    }
}
