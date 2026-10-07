using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon;

// KeyboardManager2 - queued UART text sender for the rvc VM.
// Wire vm_mat to the CRT material (same instance NixControl uses, CRT.material).
// Wire Debug to the NixDebug UdonBehaviour: NixDebug calls Keyboard.CharToVM() every
// frame while the VM has consumed the previous input char
// (load_uart__input_tag() == UartTag; NixDebug.cs lines ~273-276), and the sender
// must keep NixDebug's "UartTag" in sync per char, otherwise the gate stays closed.
// Shader side (uart.h uart_tick): accepts _UdonUARTInChar when
// cpu.uart.input_tag != _UdonUARTInTag && UART RBR empty.
public class KeyboardManager2 : UdonSharpBehaviour
{
    public Material vm_mat;
    public UdonBehaviour Debug;

    // Optional UI input source for Submit(); default UI wiring:
    // InputField.onSubmit -> UdonBehaviour event "Submit".
    public InputField Input;

    // Host queue; NixControl resets it with Keyboard.queue = "" on reset.
    public string queue = "";

    // Tag of the last char SENT (boot path uses tag 1 for its newline).
    private int sentTag = 1;

    // Append typed text (+ newline) to the queue and clear the field.
    public void Submit()
    {
        if (Input == null) return;
        var text = Input.text;
        if (text.Length == 0) return;
        queue += text + "\n";
        Input.text = "";
    }

    // Consume one queued char; called by NixDebug when the VM is ready.
    public void CharToVM()
    {
        if (vm_mat == null) return;
        if (queue.Length == 0) return; // zero input: nothing to send, gate stays open

        var ch = queue[0];
        queue = queue.Substring(1);

        int code;
        if (ch == '\n') code = 10;
        else if (ch == '\r') code = 13;
        else if (ch == '\t') code = 9;
        else code = (int)ch & 0xFF;

        sentTag = sentTag + 1;
        vm_mat.SetInt("_UdonUARTInChar", code);
        vm_mat.SetInt("_UdonUARTInTag", sentTag);
        if (Debug != null)
        {
            // Advance NixDebug's gate so CharToVM is called again once the VM
            // consumes this char (curTag == UartTag -> next frame).
            Debug.SetProgramVariable("UartTag", sentTag);
        }
    }
}
