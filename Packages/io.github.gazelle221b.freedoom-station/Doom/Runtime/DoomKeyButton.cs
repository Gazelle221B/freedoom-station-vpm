using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DoomKeyButton : UdonSharpBehaviour
{
    public DoomControl Control;
    public int KeyCode = 119;

    public override void Interact()
    {
        if (Control != null && KeyCode > 0)
            Control.EnqueueChar(KeyCode);
    }
}
