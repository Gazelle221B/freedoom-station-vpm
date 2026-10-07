using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Actual production handlers on the GPU versus independent 64-bit host math.
public static class DoomMulhProbe
{
    static string PrepareHandlers() {
        string source=File.ReadAllText("Assets/_Nix/rvc/src/emu.h").Replace("\r","");
        var text=new StringBuilder("// Generated from emu.h, do not edit.\n");
        text.AppendLine("struct ProbeCPU { uint xreg[32]; }; static ProbeCPU cpu;");
        text.AppendLine("struct FormatR { uint rd; uint rs1; uint rs2; }; struct ins_ret { uint write_reg; uint write_val; };");
        text.AppendLine("uint xreg(uint i) { return cpu.xreg[i]; }");
        foreach(string macro in new[]{"AS_SIGNED","AS_UNSIGNED","WR_RD"}) {
            var match=Regex.Match(source,@"(?m)^#define "+macro+@"\([^\n]+$");
            if(!match.Success) throw new Exception("Missing production macro "+macro);
            text.AppendLine(match.Value);
        }
        var definition=Regex.Match(source,@"(?m)^#define DEF\([^\n]+\n[^\n]+");
        var helper=Regex.Match(source,@"uint mul_high_unsigned\([^\n]+\{[\s\S]*?\n\}");
        if(!definition.Success||!helper.Success) throw new Exception("Production helper/DEF not found");
        text.AppendLine(definition.Value); text.AppendLine(helper.Value);
        foreach(string name in new[]{"mulh","mulhsu","mulhu","mul","div","divu","rem","remu"}) {
            var handler=Regex.Match(source,@"DEF\("+name+@", FormatR, \{[\s\S]*?\n\}\)");
            if(!handler.Success) throw new Exception("Production handler not found: "+name);
            text.AppendLine(handler.Value);
        }
        const string generated="Assets/Doom/Editor/DoomMProbeGenerated.cginc";
        File.WriteAllText(generated,text.ToString()); AssetDatabase.ImportAsset(generated,ImportAssetOptions.ForceUpdate);
        using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-","").ToLowerInvariant();
    }
    static uint Next(ref uint state) {
        state^=state<<13; state^=state>>17; state^=state<<5; return state;
    }
    static uint[] Expected(uint a,uint b,bool division) {
        long sa=unchecked((int)a),sb=unchecked((int)b);
        if(!division) return new[] {
            unchecked((uint)((sa*sb)>>32)), unchecked((uint)((sa*(long)b)>>32)),
            (uint)(((ulong)a*b)>>32), unchecked(a*b)
        };
        // 64-bit host operations avoid int.MinValue/-1 overflow; final words wrap.
        return new[] {
            b==0?uint.MaxValue:unchecked((uint)(sa/sb)),
            b==0?uint.MaxValue:(uint)((ulong)a/b),
            b==0?a:unchecked((uint)(sa%sb)),
            b==0?a:(uint)((ulong)a%b)
        };
    }
    static uint[] ReadResults(Material material,RenderTexture rt,RenderTexture packed,int width,int height) {
        Graphics.Blit(null,rt,material,0); Graphics.Blit(null,packed,material,1);
        var request=AsyncGPUReadback.Request(packed); request.WaitForCompletion();
        if(request.hasError) throw new Exception("Packed GPU readback failed");
        var bytes=request.GetData<Color32>(); var actual=new uint[width*height*4];
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) for(int lane=0;lane<4;lane++) {
            var c=bytes[y*width*4+x+lane*width];
            actual[(y*width+x)*4+lane]=(uint)c.r|((uint)c.g<<8)|((uint)c.b<<16)|((uint)c.a<<24);
        }
        return actual;
    }
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("DOOM_PROBE_OUT") ?? "DoomProbe";
        Directory.CreateDirectory(output);
        var report=new StringBuilder(); bool passed=false;
        Material material=null; RenderTexture rt=null,packed=null; ComputeBuffer operands=null;
        try {
            report.AppendLine("Production emu.h normalized SHA256="+PrepareHandlers());
            const string shaderPath="Assets/Doom/Editor/DoomMulhProbeGenerated.shader";
            File.Copy("Assets/Doom/Editor/DoomMulhProbe.shader.txt",shaderPath,true);
            AssetDatabase.ImportAsset(shaderPath,ImportAssetOptions.ForceUpdate);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if(shader) foreach(var message in ShaderUtil.GetShaderMessages(shader))
                report.AppendLine($"Shader {message.severity}: {message.message} file={message.file} line={message.line}");
            if(!shader||ShaderUtil.ShaderHasError(shader)) throw new Exception("M probe shader unavailable");
            string reference=Environment.GetEnvironmentVariable("DOOM_M_REFERENCE");
            if(string.IsNullOrEmpty(reference)) throw new Exception("Set DOOM_M_REFERENCE to the generated C int64/uint64 reference file");
            uint[] boundary={0,1,uint.MaxValue,0x80000000,0x7fffffff,0xffff,0x10000,
                0xfffe,0x10001,0xffff0000,0x80000001,0x7ffffffe,2,0xfffffffe,
                0x55555555,0xaaaaaaaa,0xff,0x100,0x7fff,0x8000};
            var cases=new List<uint[]>();
            foreach(uint a in boundary) foreach(uint b in boundary) cases.Add(new[]{a,b});
            uint seed=0x5256434d;
            for(int n=0;n<10000;n++) cases.Add(new[]{Next(ref seed),Next(ref seed)});
            var cExpected=new uint[cases.Count][];
            using(var reader=new BinaryReader(File.OpenRead(reference))) {
                if(reader.ReadUInt32()!=0x5256434d||reader.ReadUInt32()!=(uint)cases.Count) throw new Exception("C reference header mismatch");
                for(int n=0;n<cases.Count;n++) {
                    if(reader.ReadUInt32()!=cases[n][0]||reader.ReadUInt32()!=cases[n][1]) throw new Exception("C reference operand mismatch");
                    cExpected[n]=new uint[8];
                    for(int op=0;op<8;op++) cExpected[n][op]=reader.ReadUInt32();
                }
                if(reader.BaseStream.Position!=reader.BaseStream.Length) throw new Exception("Trailing C reference bytes");
            }
            const int width=128; int height=(cases.Count+width-1)/width;
            var pixels=new uint[width*height*2];
            for(int n=0;n<cases.Count;n++) { pixels[2*n]=cases[n][0]; pixels[2*n+1]=cases[n][1]; }
            var format=GraphicsFormat.R32G32B32A32_UInt;
            if(!SystemInfo.IsFormatSupported(format,FormatUsage.Render)) throw new Exception("uint4 GPU target unsupported");
            operands=new ComputeBuffer(width*height,8); operands.SetData(pixels);
            rt=new RenderTexture(new RenderTextureDescriptor(width,height) { graphicsFormat=format,depthBufferBits=0,msaaSamples=1 });
            rt.Create(); material=new Material(shader); material.SetBuffer("_Cases",operands); material.SetInt("_CaseWidth",width);
            packed=new RenderTexture(width*4,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            packed.Create(); material.SetTexture("_ProbeResult",rt);
            report.AppendLine("GPU="+SystemInfo.graphicsDeviceName+" API="+SystemInfo.graphicsDeviceType);
            report.AppendLine("Actual emu.h handlers versus C int64/uint64 reference (also cross-checked with C# long/ulong)");
            report.AppendLine("seed=0x5256434d; boundaryPairs=400 randomPairs=10000");
            material.SetInt("_Canary",1);
            var canary=ReadResults(material,rt,packed,width,height);
            uint[] canaryWords={0,uint.MaxValue,0x80000000,0x01020304};
            for(int n=0;n<canary.Length;n++) if(canary[n]!=canaryWords[n%4]) throw new Exception("GPU byte/lane canary failed");
            material.SetInt("_Canary",0); report.AppendLine("byte/lane canary passed at all pixels");
            int totalFailures=0;
            for(int pass=0;pass<2;pass++) {
                bool division=pass==1; string[] names=division?new[]{"DIV","DIVU","REM","REMU"}:new[]{"MULH","MULHSU","MULHU","MUL"};
                material.SetInt("_Division",pass);
                var actual=ReadResults(material,rt,packed,width,height);
                int[] failures=new int[4];
                for(int n=0;n<cases.Count;n++) {
                    uint a=cases[n][0],b=cases[n][1]; var expected=Expected(a,b,division);
                    for(int op=0;op<4;op++) if(expected[op]!=cExpected[n][op+4*pass]) throw new Exception("C/C# reference discrepancy");
                    for(int op=0;op<4;op++) if(actual[4*n+op]!=expected[op]) {
                        if(totalFailures++<32) report.AppendLine($"FAIL {names[op]} case={n} a=0x{a:x8} b=0x{b:x8} GPU=0x{actual[4*n+op]:x8} host=0x{expected[op]:x8}");
                        failures[op]++;
                    }
                }
                for(int op=0;op<4;op++) report.AppendLine($"{names[op]} cases={cases.Count} failures={failures[op]}");
            }
            material.SetInt("_Division",0); material.SetInt("_FaultInjection",1);
            var mutated=ReadResults(material,rt,packed,width,height); int detected=0;
            for(int n=0;n<cases.Count;n++) if(mutated[4*n]!=cExpected[n][0]) detected++;
            report.AppendLine($"negativeControlMulhMismatches={detected} expected={cases.Count}");
            passed=totalFailures==0&&detected==cases.Count; report.AppendLine("passed="+passed);
        } catch(Exception e) { report.AppendLine(e.ToString()); Debug.LogException(e); }
        finally {
            if(rt) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if(packed) { packed.Release(); UnityEngine.Object.DestroyImmediate(packed); }
            if(operands!=null) operands.Dispose();
            if(material) UnityEngine.Object.DestroyImmediate(material);
        }
        File.WriteAllText(Path.Combine(output,"m-extension-gpu-vs-host.txt"),report.ToString());
        Debug.Log("DOOM_M_PROBE_RESULT\n"+report);
        EditorApplication.Exit(passed?0:2);
    }
}
