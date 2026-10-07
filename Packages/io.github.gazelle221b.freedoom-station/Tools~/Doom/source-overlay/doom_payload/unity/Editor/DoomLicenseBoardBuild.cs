using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharp;
using UdonSharpEditor;

// Original document bytes are build inputs; runtime contains only serialized pages.
public static class DoomLicenseBoardBuild
{
    const string Folder="Assets/Doom/LicenseBoard";
    const string Station="Assets/Doom/DoomStation.prefab";
    [Serializable] public class Document { public string file,title,sha256,source,sourceSha256; public bool utf8Bom; public int bytes; }
    [Serializable] public class Manifest { public string sourceUrl,payloadSha256; public Document[] documents; }
    [Serializable] public class DocumentResult { public string file,title,sha256; public int pages; public bool originalTextEqual,originalBytesEqual; }
    [Serializable] public class Result { public bool passed; public int documents,pages,missingCharacters,fallbackCharacters,truncatedPages,blankGlyphs,checkedGlyphs; public string font,payloadSha256; public DocumentResult[] originals; public bool interactButtonsPassed,staticFont,noUpdate,crtUnobstructed; }
    static string Output => Environment.GetEnvironmentVariable("DOOM_BOARD_OUT") ?? Path.GetFullPath("DoomLicenseBoardVerification");
    public static void Run()
    {
        try { Build(); Validate(); EditorApplication.Exit(0); }
        catch(Exception e) { Debug.LogException(e); Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output,"error.txt"),e.ToString()); EditorApplication.Exit(2); }
    }
    public static void ValidateRun()
    {
        try { Validate(); EditorApplication.Exit(0); }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
    }
    public static void CompileRun()
    {
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        Debug.Log("DOOM_LICENSE_BOARD_COMPILE_READY");
        EditorApplication.Exit(0);
    }
    static Manifest ReadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder+"/documents.json",Encoding.UTF8));
    static string Original(Document doc) => File.ReadAllText(Folder+"/Documents/"+doc.file,Encoding.UTF8);
    static string Sha(byte[] bytes) { using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
    public static void Build()
    {
        Directory.CreateDirectory(Output);
        var manifest=ReadManifest();
        string characters=string.Concat(manifest.documents.Select(d=>Original(d)+d.title))+
            "原文ライセンス・クレジット 前の文書 次の文書 前のページ 次のページ 文書 ページ ソースの入手先 未公開 SHA-256 / ："+
            "ソースの入手先（未公開）: SHA-256:　"+
            manifest.sourceUrl+manifest.payloadSha256+"0123456789";
        string fontPath=Environment.GetEnvironmentVariable("DOOM_BOARD_FONT") ?? Folder+"/SourceFont.otf";
        if(Environment.GetEnvironmentVariable("DOOM_BOARD_REBUILD_FONT")=="1") {
            string error=AssetDatabase.MoveAsset(Folder+"/LicenseSDF.asset",AssetDatabase.GenerateUniqueAssetPath(Folder+"/LicenseSDF-previous.asset"));
            if(!string.IsNullOrEmpty(error)) throw new Exception(error);
        }
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Folder+"/LicenseSDF.asset");
        if(!font) font=DoomLicenseFont.Build(fontPath,Folder+"/LicenseSDF.asset",characters);
        // The original GPL texts contain form feeds. TMP otherwise substitutes
        // a visible missing-character square for this non-printing separator.
        if(characters.Contains('\f') && !font.characterLookupTable.ContainsKey(12)) {
            font.characterTable.Add(new TMP_Character(12,font,font.characterLookupTable[32].glyph));
            font.ReadFontAssetDefinition(); EditorUtility.SetDirty(font);
        }
        foreach(char character in characters.Distinct())
            if(!char.IsControl(character) && character!='\uFEFF' && !font.characterLookupTable.ContainsKey(character))
                throw new Exception("Existing static atlas misses U+"+((int)character).ToString("X4"));
        foreach(string name in new[]{"DoomLicenseBoard","DoomLicenseButton"}) {
            string path=Folder+"/"+name+".asset";
            var program=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            if(!program) { program=ScriptableObject.CreateInstance<UdonSharpProgramAsset>(); program.sourceCsScript=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Doom/Runtime/"+name+".cs"); AssetDatabase.CreateAsset(program,path); }
            // These scripts are authored for the installed serialization format.
            program.ScriptVersion=UdonSharpProgramVersion.CurrentVersion;
        }
        AssetDatabase.SaveAssets();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        var root=PrefabUtility.LoadPrefabContents(Station);
        try {
            var previous=root.transform.Find("License Board");
            if(previous) {
                if(!previous.GetComponent<DoomLicenseBoard>()) throw new Exception("Unrecognized License Board object; cannot replace");
                // Update in place. Destroying proxies loaded from a prefab can
                // leave native serialization callbacks attached during its save.
                var existing=previous.GetComponent<DoomLicenseBoard>();
                foreach(var label in previous.GetComponentsInChildren<TMP_Text>(true)) {
                    label.font=font; label.fontSharedMaterial=font.material; label.isOrthographic=true;
                }
                AssignDocuments(existing,manifest);
                foreach(var button in previous.GetComponentsInChildren<DoomLicenseButton>(true))
                    UdonSharpEditorUtility.CopyProxyToUdon(button);
                PrefabUtility.SaveAsPrefabAsset(root,Station); AssetDatabase.SaveAssets(); return;
            }
            var panelRoot=new GameObject("License Board"); panelRoot.transform.SetParent(root.transform,false);
            panelRoot.transform.position=new Vector3(5,2.7f,1);
            var board=panelRoot.AddUdonSharpComponent<DoomLicenseBoard>();
            var panel=GameObject.CreatePrimitive(PrimitiveType.Quad); panel.name="Background";
            panel.transform.SetParent(panelRoot.transform,false); panel.transform.localScale=new Vector3(4.8f,4.2f,1);
            UnityEngine.Object.DestroyImmediate(panel.GetComponent<Collider>());
            var background=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Background.mat");
            if(!background) { background=new Material(Shader.Find("Unlit/Color")); AssetDatabase.CreateAsset(background,Folder+"/Background.mat"); }
            background.color=new Color(.035f,.065f,.095f); panel.GetComponent<Renderer>().sharedMaterial=background;
            board.Heading=Label("Document title",panelRoot.transform,font,new Vector3(0,1.75f,-.025f),1500,130,42,new Color(.45f,.85f,1));
            board.Body=Label("Verbatim document page",panelRoot.transform,font,new Vector3(0,0,-.03f),1400,1000,27,new Color(.93f,.96f,1));
            board.Body.alignment=TextAlignmentOptions.TopLeft;
            board.Counter=Label("Document and page counter",panelRoot.transform,font,new Vector3(0,-1.6f,-.035f),1400,90,25,Color.white);
            board.Source=Label("Corresponding source and payload SHA256",panelRoot.transform,font,new Vector3(0,-1.87f,-.035f),1500,110,18,new Color(.7f,.85f,.95f));
            AssignDocuments(board,manifest);
            string[] names={"前の文書","次の文書","前のページ","次のページ"};
            var buttonMat=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Button.mat");
            if(!buttonMat) { buttonMat=new Material(Shader.Find("Unlit/Color")); AssetDatabase.CreateAsset(buttonMat,Folder+"/Button.mat"); }
            buttonMat.color=new Color(.09f,.22f,.32f);
            for(int i=0;i<4;i++) {
                var obj=GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name=names[i]; obj.transform.SetParent(panelRoot.transform,false);
                obj.transform.localPosition=new Vector3(-1.8f+i*1.2f,-2.38f,-.05f); obj.transform.localScale=new Vector3(1.1f,.48f,.18f);
                obj.GetComponent<Renderer>().sharedMaterial=buttonMat;
                var button=obj.AddUdonSharpComponent<DoomLicenseButton>(); button.Board=board; button.Action=i;
                var udon=UdonSharpEditorUtility.GetBackingUdonBehaviour(button); udon.interactText=names[i]; udon.proximity=3;
                // Labels are siblings to avoid inheriting the cube's nonuniform scale.
                var label=Label(names[i]+" label",panelRoot.transform,font,new Vector3(-1.8f+i*1.2f,-2.38f,-.15f),350,100,37,Color.white);
                label.text=names[i]; UdonSharpEditorUtility.CopyProxyToUdon(button);
            }
            board.DocumentIndex=0; board.PageIndex=0; board.ShowPage();
            UdonSharpEditorUtility.CopyProxyToUdon(board);
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            // Save the owning Station once: saving a child first lets UdonSharp's
            // import pass replace its proxies while the parent is still being saved.
            PrefabUtility.SaveAsPrefabAsset(root,Station); AssetDatabase.SaveAssets();
        } finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void AssignDocuments(DoomLicenseBoard board,Manifest manifest)
    {
        var pages=new List<string>(); var starts=new List<int>(); var counts=new List<int>();
        foreach(var doc in manifest.documents) {
            starts.Add(pages.Count); var split=Split(Original(doc),board.Body); pages.AddRange(split); counts.Add(split.Count);
        }
        // Per-page BOM forces quoted YAML, preserving CRLF even in ASCII pages.
        board.Pages=pages.Select(p=>"\uFEFF"+p).ToArray(); board.DocumentTitles=manifest.documents.Select(d=>d.title).ToArray();
        board.DocumentStarts=starts.ToArray(); board.DocumentCounts=counts.ToArray();
        board.SourceUrl=manifest.sourceUrl; board.PayloadSha256=manifest.payloadSha256;
        board.DocumentIndex=0; board.PageIndex=0; board.ShowPage();
        UdonSharpEditorUtility.CopyProxyToUdon(board);
    }
    static TextMeshPro Label(string name,Transform parent,TMP_FontAsset font,Vector3 local,float width,float height,float size,Color color)
    {
        var obj=new GameObject(name); obj.transform.SetParent(parent,false); obj.transform.localPosition=local;
        var text=obj.AddComponent<TextMeshPro>(); text.font=font; text.fontSharedMaterial=font.material;
        text.rectTransform.sizeDelta=new Vector2(width,height); text.rectTransform.localScale=Vector3.one*.003f;
        text.fontSize=size; text.color=color; text.richText=false; text.enableAutoSizing=false;
        text.isOrthographic=true; // Use point units consistently for layout and world-space glyph size.
        text.enableWordWrapping=true; text.overflowMode=TextOverflowModes.Overflow;
        text.alignment=TextAlignmentOptions.Center; text.margin=new Vector4(8,4,8,4);
        return text;
    }
    static List<string> Split(string text,TMP_Text body)
    {
        var pages=new List<string>(); int start=0;
        while(start<text.Length) {
            int lo=1,hi=Math.Min(5000,text.Length-start),best=0;
            while(lo<=hi) {
                int mid=(lo+hi)/2;
                var size=body.GetPreferredValues(text.Substring(start,mid),body.rectTransform.rect.width-16,Mathf.Infinity);
                if(size.y<=body.rectTransform.rect.height-30) { best=mid; lo=mid+1; } else hi=mid-1;
            }
            if(best==0) throw new Exception("One character cannot fit on board");
            int end=start+best;
            if(end<text.Length) {
                int newline=text.LastIndexOf('\n',end-1,best);
                if(newline>=start) end=newline+1;
                if(end>start && text[end-1]=='\r' && text[end]=='\n') end--;
                if(end>start && char.IsHighSurrogate(text[end-1]) && char.IsLowSurrogate(text[end])) end--;
            }
            if(end<=start) throw new Exception("Pagination made no progress");
            pages.Add(text.Substring(start,end-start)); start=end;
        }
        if(pages.Count==0) pages.Add("");
        return pages;
    }
    public static void Validate()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene(DoomBootstrap.ScenePath);
        var board=UnityEngine.Object.FindObjectOfType<DoomLicenseBoard>();
        if(!board) throw new Exception("Board missing from Station prefab instance");
        var manifest=ReadManifest(); var font=board.Body.font;
        var report=new Result { documents=manifest.documents.Length,pages=board.Pages.Length,font=font.name,payloadSha256=board.PayloadSha256,
            staticFont=font.atlasPopulationMode==AtlasPopulationMode.Static && font.atlasTextures.Length==1 && (font.fallbackFontAssetTable==null || font.fallbackFontAssetTable.Count==0),
            noUpdate=!File.ReadAllText("Assets/Doom/Runtime/DoomLicenseBoard.cs").Contains("void Update(") && !File.ReadAllText("Assets/Doom/Runtime/DoomLicenseButton.cs").Contains("void Update("),
            originals=new DocumentResult[manifest.documents.Length] };
        int offset=0;
        for(int d=0;d<manifest.documents.Length;d++) {
            var doc=manifest.documents[d]; int count=board.DocumentCounts[d];
            if(board.DocumentStarts[d]!=offset) throw new Exception("Non-contiguous document ranges");
            string assembled=string.Concat(Enumerable.Range(offset,count).Select(index=>board.GetPageText(index)));
            byte[] original=File.ReadAllBytes(Folder+"/Documents/"+doc.file);
            byte[] encoded=new UTF8Encoding(false,true).GetBytes(assembled);
            if(doc.utf8Bom) encoded=new byte[]{239,187,191}.Concat(encoded).ToArray();
            var result=new DocumentResult {file=doc.file,title=doc.title,pages=count,sha256=Sha(original),originalTextEqual=assembled==Original(doc),originalBytesEqual=original.SequenceEqual(encoded)};
            if(!result.originalTextEqual || !result.originalBytesEqual || result.sha256!=doc.sha256) {
                File.WriteAllBytes(Path.Combine(Output,"mismatch-"+doc.file),encoded);
                throw new Exception("Original mismatch: "+doc.file+" originalBytes="+original.Length+" assembledBytes="+encoded.Length+" sha="+result.sha256+" expected="+doc.sha256);
            }
            report.originals[d]=result;
            for(int p=0;p<count;p++) {
                board.DocumentIndex=d; board.PageIndex=p; board.ShowPage(); board.Body.ForceMeshUpdate(true,true);
                if(board.Body.isTextTruncated || board.Body.preferredHeight>board.Body.rectTransform.rect.height-8) report.truncatedPages++;
                var info=board.Body.textInfo;
                for(int c=0;c<info.characterCount;c++) {
                    var ci=info.characterInfo[c];
                    if(char.IsWhiteSpace(ci.character) || ci.character=='\uFEFF') continue;
                    if(ci.fontAsset!=font) report.fallbackCharacters++;
                    if(!font.characterLookupTable.ContainsKey(ci.character)) {
                        report.missingCharacters++;
                        Debug.LogError("DOOM_BOARD_MISSING doc="+d+" page="+p+" U+"+((int)ci.character).ToString("X4")+" index="+ci.index);
                    }
                }
                if(p==0) CaptureBoard(board,$"document-{d:00}-first");
                if(p==count-1) CaptureBoard(board,$"document-{d:00}-last");
            }
            offset+=count;
        }
        if(offset!=board.Pages.Length) throw new Exception("Unused pages");
        var buttons=board.GetComponentsInChildren<DoomLicenseButton>(true);
        // Buttons are under the same root as the board, with real in-world colliders.
        if(buttons.Length!=4) throw new Exception("Expected four interact buttons");
        foreach(var button in buttons) {
            if(!button.GetComponent<Collider>() || !UdonSharpEditorUtility.GetBackingUdonBehaviour(button)) throw new Exception("Interact path missing");
            board.DocumentIndex=1; board.PageIndex=1; board.ShowPage(); button.Interact();
            if(button.Action==0 && board.DocumentIndex!=0 || button.Action==1 && board.DocumentIndex!=2 ||
               button.Action==2 && board.PageIndex!=0 || button.Action==3 && board.PageIndex!=Math.Min(2,board.DocumentCounts[1]-1)) throw new Exception("Interact action failed: "+button.Action);
        }
        report.interactButtonsPassed=true;
        var terminal=GameObject.Find("Terminal").GetComponent<Renderer>();
        var panel=board.transform.Find("Background").GetComponent<Renderer>();
        report.crtUnobstructed=panel.bounds.min.x>terminal.bounds.max.x;
        board.DocumentIndex=0; board.PageIndex=0; board.ShowPage(); CaptureBoard(board,"board-overview"); CaptureWorld();
        foreach(var label in board.GetComponentsInChildren<TMP_Text>(true)) {
            label.ForceMeshUpdate(true,true);
            foreach(var ci in label.textInfo.characterInfo.Take(label.textInfo.characterCount)) {
                if(char.IsWhiteSpace(ci.character) || char.IsControl(ci.character) || ci.character=='\uFEFF') continue;
                if(ci.fontAsset!=font) report.fallbackCharacters++;
                if(!font.characterLookupTable.ContainsKey(ci.character)) report.missingCharacters++;
            }
        }
        // A cmap entry alone is insufficient: inspect every used non-whitespace glyph's SDF ink.
        var pixels=font.atlasTextures[0].GetPixels32(); int atlasWidth=font.atlasTextures[0].width;
        string required=string.Concat(manifest.documents.Select(d=>Original(d)+d.title))+string.Concat(board.GetComponentsInChildren<TMP_Text>(true).Select(t=>t.text));
        foreach(char character in required.Distinct()) {
            if(char.IsWhiteSpace(character) || char.IsControl(character) || character=='\uFEFF') continue;
            TMP_Character entry;
            if(!font.characterLookupTable.TryGetValue(character,out entry)) { report.missingCharacters++; continue; }
            var rect=entry.glyph.glyphRect; bool ink=false;
            for(int y=rect.y;y<rect.y+rect.height && !ink;y++)
                for(int x=rect.x;x<rect.x+rect.width;x++)
                    if(pixels[y*atlasWidth+x].a>127) { ink=true; break; }
            report.checkedGlyphs++; if(!ink) report.blankGlyphs++;
        }
        // Confirm serialized Udon pages are identical to the tested proxy strings.
        UdonSharpEditorUtility.CopyProxyToUdon(board);
        string[] serialized;
        if(!UdonSharpEditorUtility.GetBackingUdonBehaviour(board).publicVariables.TryGetVariableValue("Pages",out serialized) || !serialized.SequenceEqual(board.Pages))
            throw new Exception("Serialized Udon pages differ");
        report.passed=report.staticFont && report.noUpdate && report.crtUnobstructed && report.missingCharacters==0 && report.fallbackCharacters==0 && report.truncatedPages==0 && report.blankGlyphs==0;
        File.WriteAllText(Path.Combine(Output,"board-result.json"),JsonUtility.ToJson(report,true));
        if(!report.passed) throw new Exception("Board validation failed; see board-result.json");
        Debug.Log("DOOM_LICENSE_BOARD_PASS documents="+report.documents+" pages="+report.pages+" missing=0 fallback=0 truncated=0");
    }
    static void CaptureBoard(DoomLicenseBoard board,string name) {
        var center=board.transform.position; Capture(new Vector3(center.x,center.y-.15f,center.z-4),2.65f,1600,1600,name);
    }
    public static void CaptureWorld(string name="crt-and-board-layout") { Capture(new Vector3(2.8f,2.5f,-6),3f,1920,1080,name); }
    static void Capture(Vector3 position,float size,int width,int height,string name)
    {
        var go=new GameObject("License board screenshot camera") { hideFlags=HideFlags.HideAndDontSave };
        var camera=go.AddComponent<Camera>(); camera.enabled=false; camera.orthographic=true; camera.orthographicSize=size;
        camera.transform.position=position; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.015f,.02f,.025f); camera.cullingMask=~(1<<30);
        var rt=RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32); camera.targetTexture=rt; camera.Render();
        var old=RenderTexture.active; RenderTexture.active=rt; var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
        RenderTexture.active=old; camera.targetTexture=null; RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(go);
    }
}
