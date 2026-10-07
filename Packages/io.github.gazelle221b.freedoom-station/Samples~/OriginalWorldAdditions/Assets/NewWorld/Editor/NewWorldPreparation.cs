// NewWorldPreparation: starter setup for a new VRChat world.
// Opens Assets/NewWorld/Scenes/NewWorld.unity when it already exists (never
// modifies, saves, or validates it); creates it with a floor, preview light,
// camera, and world descriptor when missing. Leaves build settings, project
// settings, other scenes, and existing materials untouched.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDK3.Components;

public static class NewWorldPreparation
{
    const string Root = "Assets/NewWorld";
    const string ScenePath = "Assets/NewWorld/Scenes/NewWorld.unity";
    const string FloorMatPath = "Assets/NewWorld/Materials/NewWorldFloor.mat";
    const float RespawnY = -20f;
    static readonly string[] Subs = { "Scenes", "Materials", "Prefabs", "Scripts", "Audio", "Textures" };

    [MenuItem("Tools/New World/Prepare")]
    public static void PrepareMenu() { Prepare(); }

    [MenuItem("Tools/New World/Validate")]
    public static void ValidateMenu() { Validate(); }

    // -executeMethod NewWorldPreparation.Prepare
    // Editing-friendly: an existing scene is only opened and activated.
    public static void Prepare()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("NewWorldPreparation: deferred while playing; exit Play mode and retry.");
            return;
        }
        bool batch = Application.isBatchMode;
        EnsureFolders();
        bool existed = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null;
        Scene scene;
        if (TryGetLoaded(ScenePath, out scene))
        {
            EditorSceneManager.SetActiveScene(scene);
            Debug.Log("NEW_WORLD_OPENED scene=" + ScenePath + " source=already-loaded");
            return;
        }
        if (existed)
        {
            // Additive preserves unsaved edits in other scenes; Single only when clean or batch.
            OpenSceneMode mode = (!batch && AnySceneDirty()) ? OpenSceneMode.Additive : OpenSceneMode.Single;
            scene = EditorSceneManager.OpenScene(ScenePath, mode);
            if (mode == OpenSceneMode.Additive) EditorSceneManager.SetActiveScene(scene);
            Debug.Log("NEW_WORLD_OPENED scene=" + ScenePath + " source=reopened");
            return;
        }

        NewSceneMode createMode = (!batch && AnySceneDirty()) ? NewSceneMode.Additive : NewSceneMode.Single;
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, createMode);
        if (createMode == NewSceneMode.Additive) EditorSceneManager.SetActiveScene(scene);

        GameObject geometry = Group(scene, "Geometry");
        GameObject lighting = Group(scene, "Lighting");
        GameObject gameplay = Group(scene, "Gameplay");
        GameObject audio = Group(scene, "Audio");

        GameObject floor = Floor(geometry, FloorMat());
        LightOn(lighting);
        PreviewCam(audio);
        GameObject world = World(gameplay);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new Exception("NewWorldPreparation.Prepare: failed to save " + ScenePath);
        AssetDatabase.SaveAssets();

        Validate();

        VRCSceneDescriptor desc = world.GetComponent<VRCSceneDescriptor>();
        PipelineManager pm = world.GetComponent<PipelineManager>();
        int n = desc.spawns == null ? 0 : desc.spawns.Length;
        string at = (n > 0 && desc.spawns[0] != null) ? desc.spawns[0].position.ToString() : "none";
        Debug.Log("NEW_WORLD_PREPARED scene=" + ScenePath + " mode=created"
            + " floor=" + floor.name + " spawns=" + n + "@" + at
            + " respawnY=" + desc.RespawnHeightY
            + " blueprintEmpty=" + string.IsNullOrEmpty(pm.blueprintId));
    }

    // -executeMethod NewWorldPreparation.Validate
    public static void Validate()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("NewWorldPreparation: deferred while playing; exit Play mode and retry.");
            return;
        }
        bool batch = Application.isBatchMode;
        Scene scene;
        if (!TryGetLoaded(ScenePath, out scene))
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new Exception("NewWorldPreparation.Validate: missing " + ScenePath);
            OpenSceneMode mode = (!batch && AnySceneDirty()) ? OpenSceneMode.Additive : OpenSceneMode.Single;
            scene = EditorSceneManager.OpenScene(ScenePath, mode);
            if (mode == OpenSceneMode.Additive) EditorSceneManager.SetActiveScene(scene);
        }

        List<VRCSceneDescriptor> descs = AllInScene<VRCSceneDescriptor>(scene);
        if (descs.Count != 1)
            throw new Exception("NewWorldPreparation.Validate: expected 1 VRCSceneDescriptor, found " + descs.Count);
        VRCSceneDescriptor desc = descs[0];
        if (desc.gameObject.name != "VRCWorld")
            throw new Exception("NewWorldPreparation.Validate: descriptor must be on 'VRCWorld', found on '" + desc.gameObject.name + "'");
        if (!Mathf.Approximately(desc.RespawnHeightY, RespawnY))
            throw new Exception("NewWorldPreparation.Validate: RespawnHeightY must be " + RespawnY + ", found " + desc.RespawnHeightY);
        if (desc.spawns == null || desc.spawns.Length == 0)
            throw new Exception("NewWorldPreparation.Validate: spawns empty; expected a spawn at (0, 1, 0)");

        PipelineManager pm = desc.GetComponent<PipelineManager>();
        if (pm == null)
            throw new Exception("NewWorldPreparation.Validate: PipelineManager missing on 'VRCWorld'");
        if (!string.IsNullOrEmpty(pm.blueprintId))
            throw new Exception("NewWorldPreparation.Validate: blueprintId must be empty for a new world, found '" + pm.blueprintId + "'");

        GameObject floor = NamedInScene(scene, "Floor");
        if (floor == null)
            throw new Exception("NewWorldPreparation.Validate: 'Floor' not found");
        BoxCollider box = floor.GetComponent<BoxCollider>();
        if (box == null || !box.enabled)
            throw new Exception("NewWorldPreparation.Validate: 'Floor' needs an enabled BoxCollider");
        Vector3 size = floor.transform.lossyScale;
        if (!Mathf.Approximately(size.x, 40f) || !Mathf.Approximately(size.y, 0.2f) || !Mathf.Approximately(size.z, 40f))
            throw new Exception("NewWorldPreparation.Validate: floor scale must be (40, 0.2, 40), found " + size);
        if (!Mathf.Approximately(floor.transform.position.y, -0.1f))
            throw new Exception("NewWorldPreparation.Validate: floor center Y must be -0.1");

        Physics.SyncTransforms();
        foreach (Transform spawn in desc.spawns)
        {
            if (spawn == null)
                throw new Exception("NewWorldPreparation.Validate: spawns contains a missing reference");
            if (spawn.gameObject.scene != scene)
                throw new Exception("NewWorldPreparation.Validate: spawn '" + spawn.name + "' is outside the new scene");
            RaycastHit hit;
            if (!Physics.Raycast(new Ray(spawn.position + Vector3.up * 2f, Vector3.down), out hit, 200f))
                throw new Exception("NewWorldPreparation.Validate: spawn '" + spawn.name + "' at " + spawn.position + " has no ground collider below it");
            if (Mathf.Abs(hit.point.x) > 20.5f || Mathf.Abs(hit.point.z) > 20.5f || hit.point.y < -1f || hit.point.y > 1f)
                throw new Exception("NewWorldPreparation.Validate: spawn ground hit at " + hit.point + " is off the 40x40 floor top");
        }

        int objects = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                objects++;
                foreach (Component c in t.GetComponents<Component>())
                    if (c == null)
                        throw new Exception("NewWorldPreparation.Validate: missing script on '" + t.name + "' (including inactive)");
            }
        }

        foreach (string dep in AssetDatabase.GetDependencies(ScenePath, false))
            if (dep.StartsWith("Assets/Scenes/", StringComparison.Ordinal))
                throw new Exception("NewWorldPreparation.Validate: new scene references old scene asset '" + dep + "'");

        Debug.Log("NEW_WORLD_VALIDATED scene=" + ScenePath + " descriptors=1 spawns=" + desc.spawns.Length
            + " respawnY=" + desc.RespawnHeightY + " blueprintEmpty=True floor=Floor"
            + " missingScripts=0 objects=" + objects + " oldRefs=0");
    }

    static bool TryGetLoaded(string path, out Scene match)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.path == path) { match = s; return true; }
        }
        match = default(Scene);
        return false;
    }

    static bool AnySceneDirty()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) return true;
        return false;
    }

    static List<T> AllInScene<T>(Scene scene) where T : Component
    {
        List<T> found = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            found.AddRange(root.GetComponentsInChildren<T>(true));
        return found;
    }

    static GameObject NamedInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        return null;
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "NewWorld");
        foreach (string sub in Subs)
            if (!AssetDatabase.IsValidFolder(Root + "/" + sub)) AssetDatabase.CreateFolder(Root, sub);
    }

    static GameObject Group(Scene scene, string name)
    {
        GameObject go = NamedInScene(scene, name);
        return go != null ? go : new GameObject(name);
    }

    static Material FloorMat()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(FloorMatPath);
        if (existing != null) return existing;
        Shader standard = Shader.Find("Standard");
        if (standard == null) throw new Exception("NewWorldPreparation: 'Standard' shader not found");
        Material mat = new Material(standard);
        mat.color = new Color(0.5f, 0.5f, 0.5f, 1f);
        AssetDatabase.CreateAsset(mat, FloorMatPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static GameObject Floor(GameObject parent, Material mat)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject floor = NamedInScene(scene, "Floor");
        bool isNew = false;
        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            isNew = true;
        }
        if (floor.transform.parent == null) floor.transform.SetParent(parent.transform, true);
        floor.transform.rotation = Quaternion.identity;
        floor.transform.position = new Vector3(0f, -0.1f, 0f);
        floor.transform.localScale = new Vector3(40f, 0.2f, 40f);
        BoxCollider box = floor.GetComponent<BoxCollider>();
        if (box == null) box = floor.AddComponent<BoxCollider>();
        box.enabled = true;
        box.isTrigger = false;
        Renderer renderer = floor.GetComponent<Renderer>();
        if (renderer != null && (isNew || renderer.sharedMaterial == null)) renderer.sharedMaterial = mat;
        return floor;
    }

    static void LightOn(GameObject parent)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject go = NamedInScene(scene, "Directional Light");
        if (go != null && go.GetComponent<Light>() != null)
        {
            if (go.transform.parent == null) go.transform.SetParent(parent.transform, true);
            return;
        }
        foreach (Light candidate in AllInScene<Light>(scene))
        {
            if (candidate.type == LightType.Directional)
            {
                if (candidate.transform.parent == null) candidate.transform.SetParent(parent.transform, true);
                return;
            }
        }
        GameObject created = go != null ? go : new GameObject("Directional Light");
        Light light = created.GetComponent<Light>();
        if (light == null) light = created.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1f;
        created.transform.SetParent(parent.transform, false);
        created.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static void PreviewCam(GameObject parent)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject go = NamedInScene(scene, "PreviewCamera");
        if (go == null)
        {
            go = new GameObject("PreviewCamera");
            go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            go.transform.position = new Vector3(0f, 2f, -6f);
            go.transform.LookAt(new Vector3(0f, 1f, 0f));
            go.transform.SetParent(parent.transform, true);
            try { go.tag = "MainCamera"; } catch (Exception) { }
            return;
        }
        if (go.GetComponent<Camera>() == null) go.AddComponent<Camera>();
        if (go.GetComponent<AudioListener>() == null) go.AddComponent<AudioListener>();
        if (go.transform.parent == null) go.transform.SetParent(parent.transform, true);
    }

    static GameObject World(GameObject parent)
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        GameObject world = NamedInScene(scene, "VRCWorld");
        if (world == null)
        {
            world = new GameObject("VRCWorld");
            world.transform.position = Vector3.zero;
            world.transform.SetParent(parent.transform, true);
        }
        else if (world.transform.parent == null)
        {
            world.transform.SetParent(parent.transform, true);
        }
        VRCSceneDescriptor desc = world.GetComponent<VRCSceneDescriptor>();
        if (desc == null) desc = world.AddComponent<VRCSceneDescriptor>();
        PipelineManager pm = world.GetComponent<PipelineManager>();
        if (pm == null) pm = world.AddComponent<PipelineManager>();
        pm.blueprintId = string.Empty;
        desc.RespawnHeightY = RespawnY;
        Transform spawn = world.transform.Find("Spawn");
        if (spawn == null)
        {
            GameObject spawnGo = new GameObject("Spawn");
            spawnGo.transform.SetParent(world.transform, false);
            spawnGo.transform.position = new Vector3(0f, 1f, 0f);
            spawnGo.transform.rotation = Quaternion.identity;
            spawn = spawnGo.transform;
        }
        else
        {
            spawn.position = new Vector3(0f, 1f, 0f);
        }
        bool contains = false;
        if (desc.spawns != null)
            foreach (Transform t in desc.spawns)
                if (t == spawn) { contains = true; break; }
        if (!contains) desc.spawns = new Transform[] { spawn };
        return world;
    }
}
