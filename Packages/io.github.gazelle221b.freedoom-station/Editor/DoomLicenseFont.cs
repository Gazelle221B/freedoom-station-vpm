using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Editor-only builder for the license board's single static SDF TextMeshPro font asset.
///
/// Rationale and contract (see outputs/doom-license-board/font-research.md in the 2026-10-01
/// workspace): the board renders the ORIGINAL license/credits UTF-8 documents plus a Japanese
/// guide with ONE static SDF font asset and NO fallback font table. TMP 3.0.6 on Unity
/// 2022.3.22f1 can only rasterize an SDF atlas through its own factory + dynamic population
/// path (FontEngine.TryAddGlyphsToTexture is engine-internal in this Unity version), so the
/// build pipeline imports the OTF as a Font, creates a dynamic single-atlas asset,
/// TryAddCharacters()s the explicit Unicode scalar set, then freezes it to Static and strips
/// the source font reference (runtime needs no sourceFontFile).
///
/// Failure policy: any required scalar that the source font cannot map, or that cannot fit
/// into the single atlas, throws InvalidOperationException with the U+XXXX list. Formatting
/// controls (LF, CR, TAB, U+FEFF and every other Cc/Cf scalar) never get glyphs; U+0020 space
/// is always retained. The accepted single source font is the derived Rvc License Sans
/// (RvcLicenseSans-Regular.ttf, built from Noto Sans JP + Noto Sans by
/// doom_payload/license_board/build_font.py; see font-research.md), which covers the
/// complete board document set INCLUDING U+0160 (S WITH CARON). No glyph substitution
/// is ever applied; for any other fontPath the raw-byte prescan fails missing scalars
/// explicitly.
/// </summary>
public static class DoomLicenseFont
{
    public const int DefaultPointSize = 90;
    public const int DefaultAtlasPadding = 9;
    public const int MinAtlasSize = 1024;
    public const int MaxAtlasSize = 8192;

    const string k_StagingFolder = "Assets/Doom/DoomLicenseFont/Staging";
    const string k_AtlasTextureName = "DoomLicenseFont Atlas";
    const string k_AtlasMaterialName = "DoomLicenseFont Atlas Material";

    /// <summary>
    /// Builds (or rebuilds) ONE static SDF TMP font asset from an explicit Unicode scalar set.
    /// </summary>
    /// <param name="fontPath">Path to the static OTF/TTF (absolute, or relative to the project root).</param>
    /// <param name="fontAssetPath">Project-relative target path for the .asset (must not exist yet).</param>
    /// <param name="requiredCharacters">Every scalar the board and viewer must render (documents + UI).</param>
    /// <returns>The persisted, verified TMP_FontAsset.</returns>
    public static TMP_FontAsset Build(string fontPath, string fontAssetPath, string requiredCharacters)
    {
        if (string.IsNullOrEmpty(fontPath))
            throw new ArgumentException("fontPath is required.", "fontPath");
        if (string.IsNullOrEmpty(fontAssetPath))
            throw new ArgumentException("fontAssetPath is required.", "fontAssetPath");
        if (!fontAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("fontAssetPath must be a project-relative Assets/ path.", "fontAssetPath");

        string absoluteFontPath = ResolveAbsolutePath(fontPath);
        if (!File.Exists(absoluteFontPath))
            throw new FileNotFoundException("Font file not found: " + absoluteFontPath, absoluteFontPath);

        List<uint> scalars = ParseRequiredCharacters(requiredCharacters);
        if (scalars.Count == 0)
            throw new InvalidOperationException("requiredCharacters produced no explicit Unicode scalars to build.");

        // 1. Coverage prescan from the raw font bytes (no import needed, public FontEngine API).
        FontEngineError initError = FontEngine.InitializeFontEngine();
        if (initError != FontEngineError.Success)
            throw new InvalidOperationException("FontEngine.InitializeFontEngine() failed: " + initError);
        FontEngineError loadError = FontEngine.LoadFontFace(File.ReadAllBytes(absoluteFontPath), DefaultPointSize);
        if (loadError != FontEngineError.Success)
            throw new InvalidOperationException("FontEngine.LoadFontFace(byte[], " + DefaultPointSize + ") failed: " + loadError + " for " + absoluteFontPath);

        List<uint> missingAtFont = new List<uint>();
        foreach (uint scalar in scalars)
        {
            uint glyphIndex;
            if (!FontEngine.TryGetGlyphIndex(scalar, out glyphIndex) || glyphIndex == 0u)
                missingAtFont.Add(scalar);
        }
        if (missingAtFont.Count > 0)
            throw new InvalidOperationException(
                "Source font does not cover required character(s): " + FormatUnicodeList(missingAtFont) +
                Environment.NewLine + "Font: " + absoluteFontPath +
                Environment.NewLine + "Resolve the missing scalars before calling Build (see font-research.md section 5).");

        EnsureAssetFolder(Path.GetDirectoryName(fontAssetPath).Replace('\\', '/'));

        // 2. Dynamic build needs a UnityEngine.Font import (staged under Assets, removed afterwards).
        string stagedPath = null;
        Font sourceFont = ImportFontForBuild(absoluteFontPath, ref stagedPath);

        try
        {
            // 3. Size the single atlas so the whole set fits (multi-atlas is banned).
            int atlasSize = ComputeAtlasSize(scalars.Count);

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont, DefaultPointSize, DefaultAtlasPadding, GlyphRenderMode.SDFAA,
                atlasSize, atlasSize, AtlasPopulationMode.Dynamic, false);
            if (fontAsset == null)
                throw new InvalidOperationException("TMP_FontAsset.CreateFontAsset returned null; check the font import (includeFontData must be enabled).");

            // 4. Explicit Unicode population.
            uint[] missingFromAdd;
            if (!fontAsset.TryAddCharacters(scalars.ToArray(), out missingFromAdd))
            {
                string detail = (missingFromAdd != null && missingFromAdd.Length > 0)
                    ? "characters not added: " + FormatUnicodeList(new List<uint>(missingFromAdd))
                    : "TryAddCharacters returned false without a missing list (atlas full or font face unavailable).";
                throw new InvalidOperationException(
                    "Failed to populate the TMP font asset. " + detail +
                    Environment.NewLine + "The single atlas was sized " + atlasSize + "x" + atlasSize +
                    " (point size " + DefaultPointSize + ", padding " + DefaultAtlasPadding + ").");
            }

            // 5. Freeze to Static; the setter drops sourceFontFile in the editor; also strip the
            //    serialized editor source refs so the shipped asset has no source font dependency.
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            ClearSourceFontReferences(fontAsset);

            // 6. Atlas texture + material configuration.
            ConfigureAtlasAndMaterial(fontAsset);

            // 7. Persist asset + subassets.
            SaveAsset(fontAsset, fontAssetPath);
        }
        finally
        {
            if (stagedPath != null)
                AssetDatabase.DeleteAsset(stagedPath);
        }
        AssetDatabase.Refresh();

        // 8. Verify the RELOADED asset (coverage with no fallback, static, single atlas, no source
        //    font, filter bilinear, non-empty glyph bitmap when visible glyphs are expected).
        TMP_FontAsset saved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontAssetPath);
        if (saved == null)
            throw new InvalidOperationException("Failed to reload built asset at " + fontAssetPath);
        VerifyPersistedAsset(saved, scalars);

        Debug.Log("DoomLicenseFont built: " + fontAssetPath + Environment.NewLine + BuildReport(saved, StringFromScalars(scalars)));
        return saved;
    }

    /// <summary>
    /// Parses a text into explicit Unicode scalars (surrogate pairs combined) in first-seen order.
    /// Formatting controls (Cc/Cf: LF, CR, TAB, U+FEFF and all other control/format scalars) are
    /// excluded - they never get atlas glyphs. U+0020 SPACE is always retained.
    /// </summary>
    public static List<uint> ParseRequiredCharacters(string text)
    {
        List<uint> result = new List<uint>();
        HashSet<uint> seen = new HashSet<uint>();
        if (text != null)
        {
            for (int i = 0; i < text.Length; i++)
            {
                uint scalar;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    scalar = (uint)char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else
                {
                    scalar = text[i];
                }

                if (IsFormattingControl(scalar))
                    continue;
                if (seen.Add(scalar))
                    result.Add(scalar);
            }
        }
        if (seen.Add(0x20u))
            result.Add(0x20u); // Retain the space glyph.
        return result;
    }

    /// <summary>
    /// Checks that every required scalar exists in the font asset (no fallback lookup). Returns
    /// false and fills missing with the U+ list otherwise. The asset's fallback table is ignored
    /// by design: coverage must come from this asset alone.
    /// </summary>
    public static bool CheckCoverage(TMP_FontAsset fontAsset, string requiredCharacters, out List<uint> missing)
    {
        missing = new List<uint>();
        if (fontAsset == null)
            throw new ArgumentNullException("fontAsset");

        List<uint> scalars = ParseRequiredCharacters(requiredCharacters);
        Dictionary<uint, TMP_Character> characters = new Dictionary<uint, TMP_Character>();
        if (fontAsset.characterTable != null)
        {
            foreach (TMP_Character character in fontAsset.characterTable)
            {
                if (character != null && !characters.ContainsKey(character.unicode))
                    characters.Add(character.unicode, character);
            }
        }

        foreach (uint scalar in scalars)
        {
            TMP_Character character;
            if (!characters.TryGetValue(scalar, out character) || character.glyph == null)
                missing.Add(scalar);
        }
        return missing.Count == 0;
    }

    /// <summary>
    /// Pre-flight glyph coverage report for a font file, without building any asset.
    /// </summary>
    public static string ReportCoverage(string fontPath, string requiredCharacters, out List<uint> missing)
    {
        missing = new List<uint>();
        if (string.IsNullOrEmpty(fontPath))
            throw new ArgumentException("fontPath is required.", "fontPath");

        string absoluteFontPath = ResolveAbsolutePath(fontPath);
        if (!File.Exists(absoluteFontPath))
            throw new FileNotFoundException("Font file not found: " + absoluteFontPath, absoluteFontPath);

        List<uint> scalars = ParseRequiredCharacters(requiredCharacters);
        FontEngine.InitializeFontEngine();
        FontEngineError loadError = FontEngine.LoadFontFace(File.ReadAllBytes(absoluteFontPath), DefaultPointSize);
        if (loadError != FontEngineError.Success)
            throw new InvalidOperationException("FontEngine.LoadFontFace failed: " + loadError + " for " + absoluteFontPath);

        List<uint> covered = new List<uint>();
        foreach (uint scalar in scalars)
        {
            uint glyphIndex;
            if (FontEngine.TryGetGlyphIndex(scalar, out glyphIndex) && glyphIndex != 0u)
                covered.Add(scalar);
            else
                missing.Add(scalar);
        }

        StringBuilder report = new StringBuilder();
        report.AppendLine("Font: " + absoluteFontPath);
        report.AppendLine("Required scalars: " + scalars.Count + "  covered: " + covered.Count + "  missing: " + missing.Count);
        if (missing.Count > 0)
            report.AppendLine("Missing: " + FormatUnicodeList(missing));
        return report.ToString();
    }

    /// <summary>
    /// Human-readable report for a built asset: coverage, atlas settings, face info, fallback count.
    /// </summary>
    public static string BuildReport(TMP_FontAsset fontAsset, string requiredCharacters)
    {
        if (fontAsset == null)
            return "DoomLicenseFont report: fontAsset is null.";

        List<uint> missing;
        bool covered = CheckCoverage(fontAsset, requiredCharacters, out missing);

        StringBuilder report = new StringBuilder();
        report.AppendLine("DoomLicenseFont report for " + fontAsset.name);
        report.AppendLine("Coverage (no fallback): " + (covered ? "OK (" + ParseRequiredCharacters(requiredCharacters).Count + " required scalars)" : "MISSING " + missing.Count + ": " + FormatUnicodeList(missing)));
        report.AppendLine("AtlasPopulationMode: " + fontAsset.atlasPopulationMode);
        report.AppendLine("Atlas textures: " + fontAsset.atlasTextureCount);
        if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0 && fontAsset.atlasTextures[0] != null)
        {
            Texture2D atlas = fontAsset.atlasTextures[0];
            report.AppendLine("Atlas: " + atlas.width + "x" + atlas.height + " " + atlas.format + " filter=" + atlas.filterMode + " wrap=" + atlas.wrapMode + " readable=" + atlas.isReadable);
        }
        report.AppendLine("Atlas render mode: " + fontAsset.atlasRenderMode);
        report.AppendLine("Atlas padding: " + fontAsset.atlasPadding);
        report.AppendLine("Face point size: " + fontAsset.faceInfo.pointSize + "  scale: " + fontAsset.faceInfo.scale);
        report.AppendLine("Characters: " + (fontAsset.characterTable != null ? fontAsset.characterTable.Count : 0) + "  Glyphs: " + (fontAsset.glyphTable != null ? fontAsset.glyphTable.Count : 0));
        report.AppendLine("Fallback fonts: " + (fontAsset.fallbackFontAssetTable != null ? fontAsset.fallbackFontAssetTable.Count : 0));
        report.AppendLine("Source font file (runtime): " + (fontAsset.sourceFontFile != null ? fontAsset.sourceFontFile.name : "<none>"));
        return report.ToString();
    }

    /// <summary>
    /// Power-of-two single-atlas side estimate for a character count, clamped to
    /// [MinAtlasSize, MaxAtlasSize]. Throws when the estimate exceeds the cap, because
    /// multi-atlas is banned (the build must fail loudly, not silently multi-atlas).
    /// </summary>
    public static int ComputeAtlasSize(int characterCount)
    {
        if (characterCount <= 0)
            throw new ArgumentException("characterCount must be positive.", "characterCount");

        float cell = DefaultPointSize + DefaultAtlasPadding * 2;
        float estimate = (float)Math.Ceiling(Math.Sqrt(characterCount)) * cell * 1.15f;
        int size = MinAtlasSize;
        while (size < MaxAtlasSize && size < estimate)
            size <<= 1;
        if (estimate > MaxAtlasSize)
            throw new InvalidOperationException(
                "Estimated single-atlas side " + Mathf.CeilToInt(estimate) +
                " exceeds MaxAtlasSize " + MaxAtlasSize +
                " for " + characterCount + " characters at point size " + DefaultPointSize +
                ". Reduce the point size (DefaultPointSize) or shrink the required character set.");
        return Mathf.Clamp(size, MinAtlasSize, MaxAtlasSize);
    }

    // ------------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------------

    static bool IsFormattingControl(uint scalar)
    {
        if (scalar == 0x20u)
            return false;
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32((int)scalar), 0);
        return category == UnicodeCategory.Control
            || category == UnicodeCategory.Format
            || category == UnicodeCategory.PrivateUse
            || category == UnicodeCategory.Surrogate;
    }

    static bool IsVisibleScalar(uint scalar)
    {
        return scalar != 0x20u
            && scalar != 0xA0u
            && CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32((int)scalar), 0) != UnicodeCategory.SpaceSeparator;
    }

    static string StringFromScalars(List<uint> scalars)
    {
        StringBuilder builder = new StringBuilder();
        foreach (uint scalar in scalars)
            builder.Append(char.ConvertFromUtf32((int)scalar));
        return builder.ToString();
    }

    static string FormatUnicodeList(List<uint> scalars)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < scalars.Count; i++)
        {
            if (i > 0)
                builder.Append(' ');
            builder.Append("U+").Append(scalars[i].ToString("X4"));
        }
        return builder.ToString();
    }

    static string ResolveAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
    }

    static Font ImportFontForBuild(string absoluteFontPath, ref string stagedPath)
    {
        string projectRelative = ToProjectRelative(absoluteFontPath);
        if (projectRelative != null)
        {
            AssetDatabase.ImportAsset(projectRelative, ImportAssetOptions.ForceUpdate);
            EnsureFontImporterIncludesFontData(projectRelative);
            Font font = AssetDatabase.LoadAssetAtPath<Font>(projectRelative);
            if (font == null)
                throw new InvalidOperationException("Failed to load existing Font asset at " + projectRelative);
            return font;
        }

        EnsureAssetFolder(k_StagingFolder + "/");
        string fileName = Path.GetFileName(absoluteFontPath);
        string staged = k_StagingFolder + "/" + fileName;
        string stagedAbsolute = Path.Combine(Path.GetDirectoryName(Application.dataPath), staged.Replace('/', Path.DirectorySeparatorChar));
        File.Copy(absoluteFontPath, stagedAbsolute, true);
        AssetDatabase.ImportAsset(staged, ImportAssetOptions.ForceUpdate);
        EnsureFontImporterIncludesFontData(staged);
        stagedPath = staged;
        Font stagedFont = AssetDatabase.LoadAssetAtPath<Font>(staged);
        if (stagedFont == null)
            throw new InvalidOperationException("Failed to import font at " + staged);
        return stagedFont;
    }

    static string ToProjectRelative(string absolutePath)
    {
        string root = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
        string full = Path.GetFullPath(absolutePath);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            string relative = full.Substring(root.Length).TrimStart('\\', '/');
            return relative.Replace('\\', '/');
        }
        return null;
    }

    static void EnsureFontImporterIncludesFontData(string assetPath)
    {
        TrueTypeFontImporter importer = AssetImporter.GetAtPath(assetPath) as TrueTypeFontImporter;
        if (importer != null && !importer.includeFontData)
        {
            importer.includeFontData = true;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
    }

    static void EnsureAssetFolder(string assetFolderPath)
    {
        string folder = assetFolderPath.TrimEnd('/');
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string walk = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = walk + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(walk, parts[i]);
            walk = next;
        }
    }

    static void ClearSourceFontReferences(TMP_FontAsset fontAsset)
    {
        SerializedObject serialized = new SerializedObject(fontAsset);
        SerializedProperty sourceFont = serialized.FindProperty("m_SourceFontFile");
        if (sourceFont != null)
            sourceFont.objectReferenceValue = null;
        SerializedProperty sourceFontGuid = serialized.FindProperty("m_SourceFontFileGUID");
        if (sourceFontGuid != null)
            sourceFontGuid.stringValue = string.Empty;
        SerializedProperty sourceFontEditorRef = serialized.FindProperty("m_SourceFontFile_EditorRef");
        if (sourceFontEditorRef != null)
            sourceFontEditorRef.objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureAtlasAndMaterial(TMP_FontAsset fontAsset)
    {
        if (fontAsset.atlasTextures == null || fontAsset.atlasTextures.Length == 0 || fontAsset.atlasTextures[0] == null)
            throw new InvalidOperationException("TMP font asset has no atlas texture.");
        if (fontAsset.atlasTextureCount != 1)
            throw new InvalidOperationException(
                "Multi-atlas was created despite enableMultiAtlasSupport=false (atlasTextureCount=" + fontAsset.atlasTextureCount + ").");

        Texture2D atlas = fontAsset.atlasTextures[0];
        atlas.name = k_AtlasTextureName;
        atlas.filterMode = FilterMode.Bilinear;
        atlas.wrapMode = TextureWrapMode.Clamp;
        atlas.Apply(false, false);

        if (fontAsset.material == null)
            throw new InvalidOperationException(
                "TMP font asset material is null. Import TMP Essential Resources first (Window > TextMeshPro > Import TMP Essential Resources).");
        fontAsset.material.name = k_AtlasMaterialName;
        fontAsset.material.mainTexture = atlas;

        EditorUtility.SetDirty(fontAsset);
        EditorUtility.SetDirty(atlas);
        EditorUtility.SetDirty(fontAsset.material);
    }

    static void SaveAsset(TMP_FontAsset fontAsset, string fontAssetPath)
    {
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontAssetPath) != null)
            throw new InvalidOperationException(
                "Target asset already exists: " + fontAssetPath + ". Delete it first or use a different fontAssetPath.");

        fontAsset.name = Path.GetFileNameWithoutExtension(fontAssetPath);
        AssetDatabase.CreateAsset(fontAsset, fontAssetPath);
        if (fontAsset.atlasTextures != null)
        {
            for (int i = 0; i < fontAsset.atlasTextures.Length; i++)
            {
                if (fontAsset.atlasTextures[i] != null)
                    AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[i], fontAsset);
            }
        }
        if (fontAsset.material != null)
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        AssetDatabase.SaveAssets();
    }

    static void VerifyPersistedAsset(TMP_FontAsset fontAsset, List<uint> scalars)
    {
        List<uint> missing;
        if (!CheckCoverage(fontAsset, StringFromScalars(scalars), out missing))
            throw new InvalidOperationException("Persisted asset is missing required characters: " + FormatUnicodeList(missing));

        if (fontAsset.atlasPopulationMode != AtlasPopulationMode.Static)
            throw new InvalidOperationException("Persisted asset is not Static (mode: " + fontAsset.atlasPopulationMode + ").");
        if (fontAsset.atlasTextureCount != 1)
            throw new InvalidOperationException("Persisted asset has multiple atlas textures (count: " + fontAsset.atlasTextureCount + ").");
        if (fontAsset.atlasTextures == null || fontAsset.atlasTextures.Length == 0 || fontAsset.atlasTextures[0] == null)
            throw new InvalidOperationException("Persisted asset has no atlas texture.");
        if (fontAsset.atlasTextures[0].filterMode != FilterMode.Bilinear)
            throw new InvalidOperationException("Persisted atlas filterMode is not Bilinear (got " + fontAsset.atlasTextures[0].filterMode + ").");
        if (fontAsset.material == null)
            throw new InvalidOperationException("Persisted asset has no material.");
        if (fontAsset.sourceFontFile != null)
            throw new InvalidOperationException("Persisted asset still references a source font file; runtime must not need it.");
        if (fontAsset.fallbackFontAssetTable != null && fontAsset.fallbackFontAssetTable.Count > 0)
            throw new InvalidOperationException("Persisted asset has fallback font entries; the board requires a single font with no fallback.");

        bool expectsVisible = false;
        foreach (uint scalar in scalars)
        {
            if (IsVisibleScalar(scalar))
            {
                expectsVisible = true;
                break;
            }
        }
        if (expectsVisible && !AtlasHasVisiblePixels(fontAsset.atlasTextures[0]))
            throw new InvalidOperationException("Atlas bitmap is empty although visible glyphs are expected.");
    }

    static bool AtlasHasVisiblePixels(Texture2D atlas)
    {
        try
        {
            if (!atlas.isReadable)
            {
                Debug.LogWarning("DoomLicenseFont: atlas texture is not readable; skipping the bitmap non-empty check (all other checks still apply).");
                return true;
            }
            byte[] data = atlas.GetRawTextureData<byte>().ToArray();
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                    return true;
            }
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("DoomLicenseFont: bitmap check unavailable: " + exception.Message);
            return true;
        }
    }

    /// <summary>
    /// Manual/CI hook: DOOM_LICENSE_FONT_PATH, DOOM_LICENSE_FONT_ASSET_PATH and
    /// DOOM_LICENSE_CHARSET_FILE (UTF-8 file with the required characters).
    /// </summary>
    [MenuItem("Tools/Doom License Font/Build From Environment")]
    public static void BuildFromEnvironment()
    {
        string fontPath = Environment.GetEnvironmentVariable("DOOM_LICENSE_FONT_PATH");
        string assetPath = Environment.GetEnvironmentVariable("DOOM_LICENSE_FONT_ASSET_PATH");
        string charsetFile = Environment.GetEnvironmentVariable("DOOM_LICENSE_CHARSET_FILE");
        if (string.IsNullOrEmpty(fontPath) || string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(charsetFile))
            throw new InvalidOperationException(
                "Set DOOM_LICENSE_FONT_PATH, DOOM_LICENSE_FONT_ASSET_PATH and DOOM_LICENSE_CHARSET_FILE.");

        if (!File.Exists(charsetFile))
            throw new FileNotFoundException("Charset file not found: " + charsetFile, charsetFile);
        string required = File.ReadAllText(charsetFile, Encoding.UTF8);

        TMP_FontAsset asset = Build(fontPath, assetPath, required);
        Debug.Log("DoomLicenseFont built: " + assetPath + Environment.NewLine + BuildReport(asset, required));
    }
}
