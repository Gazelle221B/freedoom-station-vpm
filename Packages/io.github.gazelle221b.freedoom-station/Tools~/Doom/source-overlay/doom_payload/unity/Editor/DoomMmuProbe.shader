// Actual production _Nix/rvc/src/mmu.h handlers on GPU versus C oracle.
Shader "Hidden/DoomMmuProbe" {
SubShader { Cull Off ZWrite Off ZTest Always Pass {
CGPROGRAM
#pragma target 5.0
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"

StructuredBuffer<uint> _Cases;
uint _CaseWidth, _Canary;
StructuredBuffer<uint> _RootPtes;
StructuredBuffer<uint> _LeafPtes;

#include "DoomMmuProbeGenerated.cginc"

uint4 frag(v2f_img i) : SV_Target {
    if (_Canary != 0) return uint4(0, 0xffffffffu, 0x80000000u, 0x01020304u);
    uint2 pos = uint2(i.pos.xy);
    uint caseIdx = pos.x + pos.y * _CaseWidth;
    uint base = caseIdx * 8u;
    uint privilege = _Cases[base];
    uint mprv      = _Cases[base + 1u];
    uint mpp       = _Cases[base + 2u];
    uint access    = _Cases[base + 3u];
    uint pteFlags  = _Cases[base + 4u];
    uint sumFlag   = _Cases[base + 5u];
    uint mxrFlag   = _Cases[base + 6u];
    uint layout    = _Cases[base + 7u];

    uint mstatus = (mprv << 17) | (mpp << 11) | (sumFlag << 18) | (mxrFlag << 19);
    cpu.csr.privilege = privilege;
    _Mstatus = mstatus;
    _CaseRootPte = _RootPtes[caseIdx];
    _CaseLeafPte = _LeafPtes[caseIdx];
    mmu_update(0x80080001u);

    ins_ret ins = (ins_ret)0;
    uint pa = mmu_translate(ins, 0x00401234u, access);
    uint fault = ins.trap.en ? 1u : 0u;
    uint cause = ins.trap.en ? ins.trap.type : 0u;
    uint tval  = ins.trap.en ? ins.trap.value : 0u;
    return uint4(pa, fault, cause, tval);
}
ENDCG
}
Pass {
CGPROGRAM
#pragma target 5.0
#pragma vertex vert_img
#pragma fragment pack_result
#include "UnityCG.cginc"
Texture2D<uint4> _ProbeResult;
uint _CaseWidth;
float4 pack_result(v2f_img i) : SV_Target {
    uint2 p = uint2(i.pos.xy);
    uint value = _ProbeResult[uint2(p.x % _CaseWidth, p.y)][p.x / _CaseWidth];
    return float4(value & 255, (value >> 8) & 255, (value >> 16) & 255, value >> 24) / 255.0;
}
ENDCG
}}}
