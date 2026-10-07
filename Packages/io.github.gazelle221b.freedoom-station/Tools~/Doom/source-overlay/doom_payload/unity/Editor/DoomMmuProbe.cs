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

public static class DoomMmuProbe
{
    static string PrepareMmuHandlers()
    {
        string mmuSource = Environment.GetEnvironmentVariable("DOOM_MMU_SOURCE");
        if (string.IsNullOrEmpty(mmuSource))
            mmuSource = "Assets/_Nix/rvc/src/mmu.h";
        string source = File.ReadAllText(mmuSource).Replace("\r", "");
        var text = new StringBuilder("// Generated from _Nix/rvc/src/mmu.h, do not edit.\n");
        text.AppendLine("struct MmuState { uint mode; uint ppn; };");
        text.AppendLine("struct CsrState { uint privilege; };");
        text.AppendLine("struct Trap { bool en; uint type; uint value; };");
        text.AppendLine("struct ins_ret { uint write_reg; uint write_val; uint pc_val; uint csr_write; uint csr_val; Trap trap; };");
        text.AppendLine("static ins_ret ins_ret_noop() { ins_ret r = (ins_ret)0; return r; }");
        text.AppendLine("struct ProbeCPU { MmuState mmu; CsrState csr; };");
        text.AppendLine("static ProbeCPU cpu;");
        text.AppendLine("#define PRIV_USER 0");
        text.AppendLine("#define PRIV_SUPERVISOR 1");
        text.AppendLine("#define PRIV_MACHINE 3");
        text.AppendLine("static const uint trap_InstructionPageFault = 12;");
        text.AppendLine("static const uint trap_LoadPageFault = 13;");
        text.AppendLine("static const uint trap_StorePageFault = 15;");
        text.AppendLine("#define CSR_MSTATUS 0x300");
        text.AppendLine("static uint _Mstatus;");
        text.AppendLine("static uint _CaseRootPte;");
        text.AppendLine("static uint _CaseLeafPte;");
        text.AppendLine("uint read_csr_raw(uint addr) {");
        text.AppendLine("    if (addr == CSR_MSTATUS) return _Mstatus;");
        text.AppendLine("    return 0;");
        text.AppendLine("}");
        text.AppendLine("void write_csr_raw(uint addr, uint val) { }");
        text.AppendLine("uint mem_get_cached_or_tex(uint addr) {");
        text.AppendLine("    if (addr == 0x1004) return _CaseRootPte;");
        text.AppendLine("    if (addr == 0x2004) return _CaseLeafPte;");
        text.AppendLine("    return 0;");
        text.AppendLine("}");
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("#include")) continue;
            text.AppendLine(line);
        }
        const string generated = "Assets/Doom/Editor/DoomMmuProbeGenerated.cginc";
        var dir = Path.GetDirectoryName(generated);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(generated, text.ToString());
        AssetDatabase.ImportAsset(generated, ImportAssetOptions.ForceUpdate);
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source)))
                .Replace("-", "").ToLowerInvariant();
    }

    static uint[] ReadResults(Material material, RenderTexture rt, RenderTexture packed, int width, int height)
    {
        Graphics.Blit(null, rt, material, 0);
        Graphics.Blit(null, packed, material, 1);
        var request = AsyncGPUReadback.Request(packed);
        request.WaitForCompletion();
        if (request.hasError) throw new Exception("Packed GPU readback failed");
        var bytes = request.GetData<Color32>();
        var actual = new uint[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                for (int lane = 0; lane < 4; lane++)
                {
                    var c = bytes[y * width * 4 + x + lane * width];
                    actual[(y * width + x) * 4 + lane] = (uint)c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | ((uint)c.a << 24);
                }
        return actual;
    }
    static void ComputePtes(uint layout, uint pteFlags, out uint rootPte, out uint leafPte)
    {
        uint rootBase = ((0x80002000u >> 12) << 10) | 1u;
        uint leafBase = ((0x80004000u >> 12) << 10) | pteFlags;
        leafPte = leafBase;
        if (layout == 1) leafPte &= ~1u;
        if (layout == 2) leafPte &= ~14u;
        if (layout >= 3)
        {
            uint physical = layout == 3 ? 0x80000000u : 0x80004000u;
            rootPte = ((physical >> 12) << 10) | pteFlags;
            leafPte = leafBase;
        }
        else rootPte = rootBase;
    }

    [MenuItem("Doom/MMU Probe/Run (GPU vs C Reference)")]
    public static void Run()
    {
        string output = Environment.GetEnvironmentVariable("DOOM_MMU_OUT") ?? "DoomMmuProbe";
        Directory.CreateDirectory(output);
        var report = new StringBuilder();
        bool passed = false;
        Material material = null; RenderTexture rt = null, packed = null;
        ComputeBuffer casesBuf = null, rootPtesBuf = null, leafPtesBuf = null;
        try
        {
            report.AppendLine("Production _Nix/rvc/src/mmu.h normalized SHA256=" + PrepareMmuHandlers());
            const string shaderPath = "Assets/Doom/Editor/DoomMmuProbe.shader";
            if (!File.Exists(shaderPath))
            {
                string srcPath = "Assets/doom_payload/unity/Editor/DoomMmuProbe.shader";
                if (File.Exists(srcPath))
                {
                    var sd = Path.GetDirectoryName(shaderPath);
                    if (!Directory.Exists(sd)) Directory.CreateDirectory(sd);
                    File.Copy(srcPath, shaderPath, true);
                    AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate);
                }
            }
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader)
                foreach (var msg in ShaderUtil.GetShaderMessages(shader))
                    report.AppendLine(string.Format("Shader {0}: {1} file={2} line={3}", msg.severity, msg.message, msg.file, msg.line));
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new Exception("MMU probe shader unavailable or has errors");

            string reference = Environment.GetEnvironmentVariable("DOOM_MMU_REFERENCE");
            if (string.IsNullOrEmpty(reference))
                throw new Exception("Set DOOM_MMU_REFERENCE to the mmu_sum_probe JSONL output file");
            if (!File.Exists(reference))
                throw new Exception(string.Format("Reference file not found: {0}", reference));

            var refCases = new List<RefCase>();
            int refCount = 0;
            var parser = new JsonRefParser();
            foreach (string line in File.ReadLines(reference))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                refCases.Add(parser.Parse(line));
                refCount++;
            }
            report.AppendLine(string.Format("C reference cases={0}", refCount));
            const int expectedCount = 69120;
            if (refCount != expectedCount)
                throw new Exception(string.Format("C reference case count mismatch: got {0}, expected {1}", refCount, expectedCount));
            for (int n = 0; n < refCases.Count; n++)
                if (refCases[n].id != (uint)n)
                    throw new Exception(string.Format("C reference ID mismatch at case {0}: got {1}", n, refCases[n].id));
            report.AppendLine("C reference ID/axes count verified");

            int totalCases = refCases.Count;
            const int width = 256;
            int height = (totalCases + width - 1) / width;

            var caseData = new uint[totalCases * 8];
            var rootPtes = new uint[totalCases];
            var leafPtes = new uint[totalCases];
            for (int n = 0; n < totalCases; n++)
            {
                var c = refCases[n];
                int b = n * 8;
                caseData[b] = c.priv;
                caseData[b + 1] = c.mprv;
                caseData[b + 2] = c.mpp;
                caseData[b + 3] = c.access;
                caseData[b + 4] = c.pteFlags;
                caseData[b + 5] = c.sumFlag;
                caseData[b + 6] = c.mxrFlag;
                caseData[b + 7] = c.layout;
                ComputePtes(c.layout, c.pteFlags, out rootPtes[n], out leafPtes[n]);
            }

            var format = GraphicsFormat.R32G32B32A32_UInt;
            if (!SystemInfo.IsFormatSupported(format, FormatUsage.Render))
                throw new Exception("uint4 GPU target unsupported");

            casesBuf = new ComputeBuffer(totalCases * 8, 4);
            casesBuf.SetData(caseData);
            rootPtesBuf = new ComputeBuffer(totalCases, 4);
            rootPtesBuf.SetData(rootPtes);
            leafPtesBuf = new ComputeBuffer(totalCases, 4);
            leafPtesBuf.SetData(leafPtes);

            rt = new RenderTexture(new RenderTextureDescriptor(width, height)
                { graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1 });
            rt.Create();

            material = new Material(shader);
            material.SetBuffer("_Cases", casesBuf);
            material.SetBuffer("_RootPtes", rootPtesBuf);
            material.SetBuffer("_LeafPtes", leafPtesBuf);
            material.SetInt("_CaseWidth", width);

            packed = new RenderTexture(width * 4, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            packed.Create();
            material.SetTexture("_ProbeResult", rt);

            report.AppendLine(string.Format("GPU={0} API={1}", SystemInfo.graphicsDeviceName, SystemInfo.graphicsDeviceType));
            report.AppendLine(string.Format("Actual mmu.h handlers vs C oracle, {0} cases", totalCases));

            material.SetInt("_Canary", 1);
            var canary = ReadResults(material, rt, packed, width, height);
            uint[] canaryWords = { 0, uint.MaxValue, 0x80000000, 0x01020304 };
            for (int n = 0; n < canary.Length; n++)
                if (canary[n] != canaryWords[n % 4])
                    throw new Exception(string.Format("GPU byte/lane canary failed at pixel {0} lane {1}: got 0x{2:x8}", n / 4, n % 4, canary[n]));
            material.SetInt("_Canary", 0);
            report.AppendLine("byte/lane canary passed at all pixels");

            var actual = ReadResults(material, rt, packed, width, height);
            // Preserve GPU words for an independent, complete C/GPU comparison.
            using (var writer = new BinaryWriter(File.Create(Path.Combine(output, "gpu-results.bin")))) {
                writer.Write(0x4d4d5531u);
                writer.Write((uint)totalCases);
                for (int n = 0; n < totalCases * 4; n++) writer.Write(actual[n]);
            }

            int failures = 0;
            int sumFetchFail = 0, mprvFetchFail = 0, otherFail = 0, dataFail = 0, noMprvFail = 0;
            for (int n = 0; n < totalCases; n++)
            {
                var c = refCases[n];
                uint gpuPa = actual[n * 4];
                uint gpuFault = actual[n * 4 + 1];
                uint gpuCause = actual[n * 4 + 2];
                uint gpuTval = actual[n * 4 + 3];
                bool match = gpuPa == c.expPa && gpuFault == c.expFault &&
                             gpuCause == c.expCause && gpuTval == c.expTval;
                if (!match)
                {
                    failures++;
                    if (c.access != 0) dataFail++;
                    if (c.mprv == 0) noMprvFail++;
                    string category;
                    if (c.access == 0 && c.mprv == 1 && c.mpp != c.priv && c.priv != 3)
                    { category = "mprv_fetch"; mprvFetchFail++; }
                    else if (c.access == 0 && c.priv == 1 && c.sumFlag == 1 && (c.pteFlags & 16u) != 0)
                    { category = "sum_fetch"; sumFetchFail++; }
                    else { category = "other"; otherFail++; }
                    if (failures <= 32)
                    {
                        report.AppendLine(string.Format("FAIL case={0} priv={1} mprv={2} mpp={3} access={4} flags={5} sum={6} mxr={7} layout={8}",
                            n, c.priv, c.mprv, c.mpp, c.access, c.pteFlags, c.sumFlag, c.mxrFlag, c.layout));
                        report.AppendLine(string.Format("  expected pa=0x{0:x8} fault={1} cause={2} tval=0x{3:x8}",
                            c.expPa, c.expFault, c.expCause, c.expTval));
                        report.AppendLine(string.Format("  GPU      pa=0x{0:x8} fault={1} cause={2} tval=0x{3:x8} category={4}",
                            gpuPa, gpuFault, gpuCause, gpuTval, category));
                    }
                }
            }
            report.AppendLine(string.Format("cases={0} failures={1} sumFetch={2} mprvFetch={3} other={4} dataFailures={5} mprvOffFailures={6}",
                totalCases, failures, sumFetchFail, mprvFetchFail, otherFail, dataFail, noMprvFail));

            material.SetInt("_Canary", 1);
            var mutated = ReadResults(material, rt, packed, width, height);
            int detected = 0;
            for (int n = 0; n < totalCases; n++)
                if (mutated[n * 4] != refCases[n].expPa || mutated[n * 4 + 1] != refCases[n].expFault)
                    detected++;
            material.SetInt("_Canary", 0);
            report.AppendLine(string.Format("negativeControlMismatches={0} expected={1}", detected, totalCases));
            passed = failures == 0 && detected == totalCases;
            report.AppendLine("passed=" + passed);
        }
        catch (Exception e)
        {
            report.AppendLine(e.ToString());
            Debug.LogException(e);
        }
        finally
        {
            if (rt) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
            if (packed) { packed.Release(); UnityEngine.Object.DestroyImmediate(packed); }
            if (casesBuf != null) casesBuf.Dispose();
            if (rootPtesBuf != null) rootPtesBuf.Dispose();
            if (leafPtesBuf != null) leafPtesBuf.Dispose();
            if (material) UnityEngine.Object.DestroyImmediate(material);
        }
        File.WriteAllText(Path.Combine(output, "mmu-gpu-vs-c.txt"), report.ToString());
        Debug.Log("DOOM_MMU_PROBE_RESULT\n" + report);
        EditorApplication.Exit(passed ? 0 : 2);
    }

    [Serializable]
    struct JsonRefExpected { public uint pa; public uint fault; public uint cause; public uint tval; }

    [Serializable]
    struct JsonRefLine
    {
        public uint id;
        public uint priv;
        public uint mprv;
        public uint mpp;
        public uint access;
        public uint pteFlags;
        public uint sum;
        public uint mxr;
        public uint layout;
        public JsonRefExpected expected;
    }

    struct RefCase
    {
        public uint id, priv, mprv, mpp, access, pteFlags, sumFlag, mxrFlag, layout;
        public uint expPa, expFault, expCause, expTval;
    }

    class JsonRefParser
    {
        public RefCase Parse(string json)
        {
            var src = JsonUtility.FromJson<JsonRefLine>(json);
            return new RefCase
            {
                id = src.id, priv = src.priv, mprv = src.mprv, mpp = src.mpp,
                access = src.access, pteFlags = src.pteFlags,
                sumFlag = src.sum, mxrFlag = src.mxr, layout = src.layout,
                expPa = src.expected.pa, expFault = src.expected.fault,
                expCause = src.expected.cause, expTval = src.expected.tval
            };
        }
    }
}
