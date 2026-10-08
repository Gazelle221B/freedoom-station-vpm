// SPDX-License-Identifier: GPL-2.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

// Exercise the production memory helpers on D3D11, including the cache's
// sentinel/overflow cases. Expected ordinary RAM values use an independent
// byte-addressed host model. Generated shader/probe assets remain local.
public static class DoomMemoryStoreTests
{
    const int Count = 512, Width = 128;
    const string ProbePath = "Assets/Doom/Generated/DoomMemoryStoreProbe.shader";

    [Serializable] class Report {
        public int cases, failed;
        public string gpu, api, memorySourceSha256;
    }

    public static void Run()
    {
        try { Check(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
    }

    static uint Mix(uint x) {
        unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15;
            x *= 0x846ca68bu; return x ^ (x >> 16); }
    }

    static uint Read(Dictionary<uint, byte> ram, uint address) {
        uint value = 0;
        for (int j = 0; j < 4; j++)
            if (ram.TryGetValue(address + (uint)j, out byte b)) value |= (uint)b << (8 * j);
        return value;
    }

    static void Write(Dictionary<uint, byte> ram, uint address, uint value, int size) {
        for (int j = 0; j < size; j++) ram[address + (uint)j] = (byte)(value >> (8 * j));
    }

    static uint[] Expected(uint kind, uint address, uint value, uint offset) {
        const uint Old = 0xdeadbeef;
        var ram = new Dictionary<uint, byte>();
        if (kind < 6) {
            if (kind != 0) Write(ram, address, kind == 2 ? value : Old, 4);
            if (kind == 4 || kind == 5) Write(ram, address + 4, 0x12345678, 4);
            Write(ram, address + (kind >= 3 ? offset : 0), value,
                kind == 3 ? 1 : kind == 4 ? 2 : 4);
            return new[] { Read(ram, address), Read(ram, address + 4), 0u, 0u };
        }
        if (kind == 6) return new[] { unchecked(value + 4), value, 3u, unchecked(value + 4) };
        // At RAM word zero, empty L1 entries shadow reads. Preserve the old
        // byte-store fallback; all case values have a nonzero highest byte.
        if (kind == 7) return new[] { 0u, 0u, 3u, value & 0xff000000u };
        if (kind == 8) return new uint[4];
        if (kind == 9) return new[] { value & 1, 0u, 0u, 0u };
        if (kind == 10) return new[] { value, ~value, 0u, 0u };
        uint ier = (value >> 8) & 255, thr = value & 255;
        uint iir = (ier & 2) != 0 && thr == 0 ? 2u : 7u;
        return new[] { (iir << 24) | (ier << 16) | (thr << 8), value >> 24, 0u, 0u };
    }

    public static void Check()
    {
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            throw new Exception("Memory-store regression requires D3D11.");
        var production = Shader.Find("Nix/rvc");
        if (!production) throw new Exception("Production Nix/rvc shader missing.");
        string core = Path.GetDirectoryName(AssetDatabase.GetAssetPath(production)).Replace('\\', '/');
        Directory.CreateDirectory(Path.GetDirectoryName(ProbePath));
        File.WriteAllText(ProbePath, ProbeSource.Replace("@CORE@", core));
        AssetDatabase.ImportAsset(ProbePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ProbePath);
        if (!shader || ShaderUtil.ShaderHasError(shader)) {
            if (shader) foreach (var msg in ShaderUtil.GetShaderMessages(shader)) Debug.LogError(msg.message);
            throw new Exception("Production memory probe did not compile.");
        }
        var material = new Material(shader);
        var memory = new RenderTexture(2048, 68, 0, GraphicsFormat.R32G32B32A32_UInt);
        var result = new RenderTexture(Width, Count / Width, 0, GraphicsFormat.R32G32B32A32_UInt);
        var packed = new RenderTexture(Width * 4, Count / Width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var buffer = new ComputeBuffer(Count, 16);
        int failures = 0;
        try {
            var cases = new uint[Count * 4];
            for (uint i = 0; i < Count; i++) {
                cases[i * 4] = i % 12;
                cases[i * 4 + 1] = 64 + (i % 128) * 4;
                cases[i * 4 + 2] = Mix(i + 12345) | 0x80000000u;
                // Include empty UART THR and every unaligned byte offset.
                if (i % 12 == 11 && (i / 12) % 2 == 0) cases[i * 4 + 2] &= 0xffffff00u;
                cases[i * 4 + 3] = i / 12 % 4;
            }
            buffer.SetData(cases); material.SetBuffer("_Cases", buffer);
            material.SetInt("_CaseWidth", Width);
            memory.filterMode = result.filterMode = packed.filterMode = FilterMode.Point;
            memory.Create(); result.Create(); packed.Create();
            material.SetInt("_Initialize", 1); Graphics.Blit(null, memory, material, 0);
            material.SetInt("_Initialize", 0); material.SetTexture("_Memory", memory);
            Graphics.Blit(null, result, material, 0);
            material.SetTexture("_Result", result); Graphics.Blit(null, packed, material, 1);
            var request = AsyncGPUReadback.Request(packed); request.WaitForCompletion();
            if (request.hasError) throw new Exception("Memory probe readback failed.");
            var bytes = request.GetData<Color32>();
            for (uint i = 0; i < Count; i++) {
                var expected = Expected(cases[i*4], cases[i*4+1], cases[i*4+2], cases[i*4+3]);
                for (int lane = 0; lane < 4; lane++) {
                    var c = bytes[(int)(i / Width) * Width * 4 + lane * Width + (int)(i % Width)];
                    uint actual = (uint)c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | ((uint)c.a << 24);
                    if (actual == expected[lane]) continue;
                    if (++failures <= 12) Debug.LogError($"RAM_STORE case={i} kind={cases[i*4]} lane={lane} expected={expected[lane]:x8} actual={actual:x8}");
                }
            }
            string output = Environment.GetEnvironmentVariable("DOOM_MEMORY_OUT") ?? "UserSettings/Doom/memory";
            Directory.CreateDirectory(output);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(core + "/src/mem.h"))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(Path.Combine(output, "result.json"), JsonUtility.ToJson(new Report {
                cases = Count, failed = failures, gpu = SystemInfo.graphicsDeviceName,
                api = SystemInfo.graphicsDeviceType.ToString(), memorySourceSha256 = hash
            }, true));
            if (failures != 0) throw new Exception($"Memory-store regression failed: {failures} words.");
            Debug.Log($"DOOM_MEMORY_STORE_PASS cases={Count} words={Count*4} sha256={hash}");
        } finally {
            buffer.Release(); memory.Release(); result.Release(); packed.Release();
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(memory); UnityEngine.Object.DestroyImmediate(result);
            UnityEngine.Object.DestroyImmediate(packed);
        }
    }

    const string ProbeSource = @"
Shader ""Hidden/DoomMemoryStoreProbe"" { SubShader { Cull Off ZWrite Off ZTest Always Pass {
CGPROGRAM
#pragma target 5.0
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
#define PASS_TICK
Texture2D<uint4> _Memory;
#define STATE_TEX(pos) (_Memory[pos])
#define STATE_TEX_HART(pos,hidx) (_Memory[pos])
static uint2 s_dim=uint2(2048,4096), m_dim=uint2(2048,1), hart_offset=uint2(0,0);
static uint hart=0;
uint _RTC0,_RTC1,_UdonUARTInChar,_UdonUARTInTag;
Texture2D<float4> _Data_MTD_R,_Data_MTD_G,_Data_MTD_B,_Data_MTD_A;
Texture2D<float4> _Data_DTB_R,_Data_DTB_G,_Data_DTB_B,_Data_DTB_A;
#include ""@CORE@/helpers.cginc""
#include ""@CORE@/src/types.h""
#include ""@CORE@/src/uart.h""
#include ""@CORE@/src/mem.h""
StructuredBuffer<uint4> _Cases;
uint _CaseWidth,_Initialize;
uint4 frag(v2f_img i):SV_Target {
    if(_Initialize!=0) return uint4(0,0,0,0);
    uint2 pos=uint2(i.pos.xy); uint4 c=_Cases[pos.y*_CaseWidth+pos.x];
    uint kind=c.x, a=0x80000000u+c.y, v=c.z, o=c.w;
    if(kind<6) {
        if(kind!=0) mem_set(a,kind==2?v:0xdeadbeefu,4);
        if(kind==4||kind==5) mem_set(a+4,0x12345678u,4);
        mem_set(a+(kind>=3?o:0),v,kind==3?1:kind==4?2:4);
        return uint4(mem_get_word(a),mem_get_word(a+4),cpu.stall,cpu.cache.ram_l1_last_val);
    }
    if(kind==6) {
        for(uint j=0;j<5;j++) mem_set(a+j*8192u,v+j,4);
        return uint4(mem_get_word(a+4*8192u),mem_get_word(a),cpu.stall,cpu.cache.ram_l1_last_val);
    }
    if(kind==7) {
        mem_set(0x80000000u,v,4);
        return uint4(mem_get_word(0x80000000u),0,cpu.stall,cpu.cache.ram_l1_last_val);
    }
    if(kind==8) { mem_set(0x80000000u+RAM_MAX,v,4); return uint4(0,0,cpu.stall,cpu.cache.ram_l1_last_val); }
    if(kind==9) { mem_set(0x02000000u,v,4); return uint4(cpu.clint.msip,0,cpu.stall,0); }
    if(kind==10) {
        mem_set(0x02004000u,v,4); mem_set(0x02004004u,~v,4);
        return uint4(cpu.clint.mtimecmp_lo,cpu.clint.mtimecmp_hi,cpu.stall,0);
    }
    mem_set(0x10000000u,v,4);
    return uint4(cpu.uart.rbr_thr_ier_iir,cpu.uart.lcr_mcr_lsr_scr,cpu.stall,0);
}
ENDCG
} Pass { CGPROGRAM
#pragma target 5.0
#pragma vertex vert_img
#pragma fragment frag
#include ""UnityCG.cginc""
Texture2D<uint4> _Result; uint _CaseWidth;
float4 frag(v2f_img i):SV_Target {
    uint2 p=uint2(i.pos.xy); uint value=_Result[uint2(p.x%_CaseWidth,p.y)][p.x/_CaseWidth];
    return float4(value&255,(value>>8)&255,(value>>16)&255,value>>24)/255.0;
}
ENDCG
} } }
";
}
