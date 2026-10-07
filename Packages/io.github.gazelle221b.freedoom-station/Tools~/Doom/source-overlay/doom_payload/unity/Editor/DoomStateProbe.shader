Shader "Hidden/DoomStateProbe" {
Properties { _MainTex("VM",2D)="black"{} _OffsetY("Read offset",Int)=0 }
SubShader { Cull Off ZWrite Off ZTest Always Pass {
CGPROGRAM
#pragma target 5.0
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
Texture2D<uint4> _MainTex;
uint _OffsetY;
float4 frag(v2f_img i):SV_Target {
    uint2 p=uint2(i.pos.xy);
    uint value=_MainTex[uint2(p.x%64,p.y+_OffsetY)][p.x/64];
    return float4(value&255,(value>>8)&255,(value>>16)&255,value>>24)/255.0;
}
ENDCG
}}}
