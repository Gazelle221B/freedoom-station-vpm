using UdonSharp;
using UnityEngine;

// Interact button for the license board. Action: 0=previous document,
// 1=next document, 2=previous page, 3=next page. The Editor builder adds
// the collider and proximity/interact setup for VR and desktop.
public class DoomLicenseButton : UdonSharpBehaviour
{
    public DoomLicenseBoard Board;
    public int Action;

    public override void Interact()
    {
        if (Board == null) return;
        if (Action == 0) Board.PreviousDocument();
        else if (Action == 1) Board.NextDocument();
        else if (Action == 2) Board.PreviousPage();
        else if (Action == 3) Board.NextPage();
    }
}
