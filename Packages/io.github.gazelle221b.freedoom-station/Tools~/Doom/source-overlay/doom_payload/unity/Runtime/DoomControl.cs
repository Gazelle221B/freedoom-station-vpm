using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DoomControl : UdonSharpBehaviour
{
    public CustomRenderTexture Vm;
    public CustomRenderTexture Fb;

    [SerializeField] private int _ticksDivisor = 4;
    public int TicksDivisor
    {
        get => _ticksDivisor;
        set
        {
            _ticksDivisor = Mathf.Max(1, value);
            ApplyTicks();
        }
    }
    public int EffectiveTicksPerFrame => 16384 / _ticksDivisor;
    public int ProductionTicks => 16384;
    public int TicksDivisorCurrent => _ticksDivisor;

    private void ApplyTicks()
    {
        if (Vm != null && Vm.material != null)
        {
            Vm.material.SetInt("_Ticks", 16384);
            Vm.material.SetInt("_TicksDivisor", _ticksDivisor);
        }
    }

    [SerializeField] private bool _realtimeMode = false;
    public bool RealtimeMode
    {
        get => _realtimeMode;
        set { _realtimeMode = value; ApplyUpdateMode(); }
    }

    public DoomKeyButton KeyW;
    public DoomKeyButton KeyA;
    public DoomKeyButton KeyS;
    public DoomKeyButton KeyD;
    public DoomKeyButton KeyFire;
    public DoomKeyButton KeyUse;
    public DoomKeyButton KeyEnter;
    public DoomKeyButton KeyEscape;

    [HideInInspector] public bool Running;
    [HideInInspector] public int UartTagAck;
    [HideInInspector] public int SubmitKey;

    private int _disableInit;
    private int _uartSendTag;
    private int _uartCooldown;
    private const int QUEUE_MASK = 31;
    private int[] _queueChars = new int[QUEUE_MASK + 1];
    private int _queueHead;
    private int _queueTail;
    private bool QueueFull => ((_queueTail + 1) & QUEUE_MASK) == _queueHead;
    private bool QueueEmpty => _queueHead == _queueTail;
    private const int UART_COOLDOWN_FRAMES = 3;

    void Start()
    {
        if (Vm == null) { Debug.LogError("[DoomControl] Vm CRT null"); return; }
        if (Fb == null) { Debug.LogError("[DoomControl] Fb CRT null"); return; }
        Vm.material.SetInt("_Init", 1);
        Vm.material.SetInt("_DoTick", 0);
        ApplyTicks();
        Vm.updateMode = CustomRenderTextureUpdateMode.OnDemand;
        Vm.Update();
        if (Fb.material != null)
        {
            Fb.material.SetInt("_Init", 1);
            Fb.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            Fb.Update();
        }
        Running = true;
        _disableInit = 2;
        _uartSendTag = 1;
        _uartCooldown = 0;
        _queueHead = 0;
        _queueTail = 0;
    }

    void Update()
    {
        if (Vm == null) return;

        if (_disableInit > 0)
        {
            Vm.Update();
            if (Fb != null) Fb.Update();
            _disableInit--;
            if (_disableInit == 0)
            {
                Vm.material.SetInt("_Init", 0);
                if (Fb != null && Fb.material != null)
                    Fb.material.SetInt("_Init", 0);
                if (Running) ApplyUpdateMode();
            }
            return;
        }

        if (_uartCooldown > 0) _uartCooldown--;
        TryFlushQueue();
        if (Running && !_realtimeMode)
        {
            Vm.Update();
            if (Fb != null) Fb.Update();
        }
    }

    public void SendW() { EnqueueChar(119); }
    public void SendA() { EnqueueChar(97); }
    public void SendS() { EnqueueChar(115); }
    public void SendD() { EnqueueChar(100); }
    public void SendFire() { EnqueueChar(32); }
    public void SendUse() { EnqueueChar(101); }
    public void SendEnter() { EnqueueChar(10); }
    public void SendEscape() { EnqueueChar(27); }
    public void SubmitKeyAction() { if (SubmitKey != 0) EnqueueChar(SubmitKey); }

    public void EnqueueChar(int charCode)
    {
        if (charCode <= 0 || charCode > 255) return;
        if (QueueFull) return;
        _queueChars[_queueTail] = charCode;
        _queueTail = (_queueTail + 1) & QUEUE_MASK;
        TryFlushQueue();
    }

    public void _Reset()
    {
        if (Vm == null) return;
        Vm.material.SetInt("_Init", 1);
        Vm.material.SetInt("_DoTick", 0);
        ApplyTicks();
        Vm.updateMode = CustomRenderTextureUpdateMode.OnDemand;
        Vm.Update();
        Vm.material.SetInt("_UdonUARTInChar", 0);
        Vm.material.SetInt("_UdonUARTInTag", 0);
        if (Fb != null && Fb.material != null)
        {
            Fb.material.SetInt("_Init", 1);
            Fb.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            Fb.Update();
        }
        Running = true;
        _disableInit = 2;
        _uartSendTag = 1;
        _uartCooldown = 0;
        _queueHead = 0;
        _queueTail = 0;
    }

    public void _Pause() { Running = false; ApplyUpdateMode(); }

    public void _Resume()
    {
        if (Vm == null) return;
        Running = true;
        ApplyUpdateMode();
    }

    private void ApplyUpdateMode()
    {
        if (Vm == null) return;
        if (Running)
        {
            Vm.material.SetInt("_DoTick", 0);
            Vm.updateMode = _realtimeMode
                ? CustomRenderTextureUpdateMode.Realtime
                : CustomRenderTextureUpdateMode.OnDemand;
            if (Fb != null)
                Fb.updateMode = _realtimeMode
                    ? CustomRenderTextureUpdateMode.Realtime
                    : CustomRenderTextureUpdateMode.OnDemand;
        }
        else
        {
            Vm.updateMode = CustomRenderTextureUpdateMode.OnDemand;
            if (Fb != null)
                Fb.updateMode = CustomRenderTextureUpdateMode.OnDemand;
        }
    }

    private void TryFlushQueue()
    {
        if (!Running) return;
        if (_uartCooldown > 0) return;
        if (QueueEmpty) return;
        if (_uartSendTag > 1 && UartTagAck != (_uartSendTag - 1)) return;
        int c = _queueChars[_queueHead];
        _queueHead = (_queueHead + 1) & QUEUE_MASK;
        Vm.material.SetInt("_UdonUARTInChar", c);
        Vm.material.SetInt("_UdonUARTInTag", _uartSendTag);
        _uartSendTag++;
        _uartCooldown = UART_COOLDOWN_FRAMES;
    }
}
