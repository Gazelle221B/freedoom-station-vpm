using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Runs the production display function, including the last UART burst element.
public static class DoomFramebufferProbe
{
    struct Pixel { public uint character, cursorX, color, cursorY; }
    public static int Check()
    {
        var source=new StringBuilder("#pragma kernel Run\nstruct uart_buffer { uint ptr;\n");
        for(int i=0;i<64;i++) source.AppendLine("uint buf"+i+";");
        source.AppendLine("}; Texture2D<float4> _SelfTexture2D;");
        var display=Shader.Find("Nix/fb");
        if(!display) throw new Exception("Production framebuffer shader missing");
        string header=Path.GetDirectoryName(AssetDatabase.GetAssetPath(display))+"/src/fb.h";
        string relative=Path.GetRelativePath("Assets/Doom/Editor",header).Replace('\\','/');
        source.AppendLine("#include \""+relative+"\"");
        source.AppendLine("uint _Ptr,_CursorX,_CursorY; StructuredBuffer<uint> _Chars; RWStructuredBuffer<uint4> _Results;");
        source.AppendLine("[numthreads(64,1,1)] void Run(uint3 id:SV_DispatchThreadID) { if(id.x>=80) return; uart_buffer buffer=(uart_buffer)0; buffer.ptr=_Ptr;");
        for(int i=0;i<64;i++) source.AppendLine("buffer.buf"+i+"=_Chars["+i+"];");
        source.AppendLine("uint4 s1=uint4(_CursorX,_CursorY,0,0),s2=0; float4 pixel=update_fb(uint2(id.x,0),buffer,s1,s2); _Results[id.x]=uint4(round(pixel.x*255),s1.x,round(pixel.y*255),s1.y); }");
        const string path="Assets/Doom/Editor/DoomFramebufferProbeGenerated.compute";
        File.WriteAllText(path,source.ToString());
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
        var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
        if(!shader) throw new Exception("Framebuffer probe failed to import");
        var texture=new Texture2D(81,25,TextureFormat.RGBAFloat,false,true);
        texture.SetPixels(new Color[81*25]); texture.Apply();
        var output=new ComputeBuffer(80,16);
        var chars=new ComputeBuffer(64,4);
        var values=new uint[64]; for(int i=0;i<64;i++) values[i]=(uint)(65+i%26);
        chars.SetData(values);
        int checks=0;
        try {
            int kernel=shader.FindKernel("Run");
            shader.SetTexture(kernel,"_SelfTexture2D",texture);
            shader.SetBuffer(kernel,"_Results",output);
            shader.SetBuffer(kernel,"_Chars",chars);
            shader.SetInt("_CursorX",0); shader.SetInt("_CursorY",0);
            foreach(uint ptr in new uint[]{0,1,62,63,64,255,uint.MaxValue}) {
                shader.SetInt("_Ptr",unchecked((int)ptr)); shader.Dispatch(kernel,2,1,1);
                var pixels=new Pixel[80]; output.GetData(pixels);
                int count=ptr==uint.MaxValue ? 0 : (int)Math.Min(ptr,63)+1;
                for(int x=0;x<80;x++) {
                    uint character=x<count ? (uint)(65+x%26) : 0;
                    uint color=x<count ? 7u : 0u;
                    if(pixels[x].character!=character || pixels[x].color!=color || pixels[x].cursorX!=count || pixels[x].cursorY!=0)
                        throw new Exception("Framebuffer mismatch: ptr="+ptr+" x="+x+" char="+pixels[x].character+" cursor="+pixels[x].cursorX);
                    checks++;
                }
            }
        } finally { chars.Dispose(); output.Dispose(); UnityEngine.Object.DestroyImmediate(texture); }
        Debug.Log("DOOM_FRAMEBUFFER_PROBE_PASS cells="+checks);
        return checks;
    }
}
