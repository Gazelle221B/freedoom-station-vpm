Shader "Hidden/DoomTimeProbe" {
Properties { _ProbeTime("Explicit time",Float)=0 }
SubShader { Cull Off ZWrite Off ZTest Always Pass {
CGPROGRAM
#pragma target 5.0
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
float _ProbeTime;
float4 frag(v2f_img i):SV_Target {
    uint x=(uint)i.pos.x;
    double t=(double)_Time.x*1000000.0*0.1;
    double explicitT=(double)_ProbeTime*1000000.0*0.1;
    uint value=x==0?(uint)(_Time.y*1000):x==1?(uint)(_Time.x*100000):
        x==2?(uint)floor(t-4294967296.0*floor(t/4294967296.0)):
        (uint)floor(explicitT-4294967296.0*floor(explicitT/4294967296.0));
    return float4(value&255,(value>>8)&255,(value>>16)&255,value>>24)/255.0;
}
ENDCG
}}}
