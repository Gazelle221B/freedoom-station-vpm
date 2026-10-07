# Staged official Dial dependency - provenance

The source files were initially staged byte-for-byte from the official upstream
repository. Original hashes and Dial.cs are preserved in Tools/ThirdParty/Dial-source.
The imported .meta files preserve upstream GUIDs. Local compatibility changes:
Dial.SetState(int, bool) is public so rvc can call its existing boot transition;
Prefab label fonts and generated Udon bytecode references are repaired by Unity APIs.
One missing MeshCollider mesh is reassigned to the same object's imported FBX visual mesh.

## Upstream pins

| | |
| --- | --- |
| Repository | https://github.com/PiMaker/VRChatUnityThings |
| Branch | main |
| Pinned HEAD | 098a4f5348838e48f5bd1170cda8fe83e9c398ae ("update Unity packages", 2023-12-26 23:01:43 +0100) |
| Dial.cs history | single commit 162c9c9728a0ee32c7f2935148f8445625f628c0 ("add even more stuff", 2021-06-29 23:14:06 +0200) - no other version exists upstream |
| Local verification clone | work/VRChatUnityThings-upstream (kept read-only for future checks) |
| Official distribution | Dial.unitypackage at repo root (498,061 bytes; NOT staged - folder copy is canonical here, consistent with how rvc itself was imported) |

## Staged files (work/rvc-official-dependencies/Dial/), upstream GUIDs preserved

Functional core (needed for ANY use of the official Dial type):

| File | GUID (from .meta) | Role |
| --- | --- | --- |
| Dial.cs | f0ebfc67412c97241ad10e3489fd2818 | UdonSharpBehaviour; the Dial control |
| Dial.asset | 65088335e29edbe46ac6a7c7909a560b | U# program asset; sourceCsScript -> Dial.cs |
| DialDesktopInteractor.cs/.asset | 50ea2e4f57aa2454fb11e9e07effb31d / ec09ab0943a811149aad596e62d34b75 | desktop Interact() -> "DesktopInteract" on Controller |
| DialVRInteractor.cs/.asset | 07c6220d542594045968e33c6d8fab23 / 151287bc17337e140a9267d65b905017 | VR OnPickup/OnDrop -> "VRInteractStart/End" |

Prefab visuals (needed to USE the official dial in a world; Dial.cs drives
these GameObjects at runtime - RotatorDesktop/RotatorVR, Base2/3/4, ClickSource):

| File | GUID (from .meta) | Role |
| --- | --- | --- |
| _PREFAB.prefab | 6f1e66d7bc3775344b592fe04769cf65 | whole dial object (interactables, pickups, labels, audio) |
| Dial Material.mat | 770850b698b0e114ebe85dbece72c395 | mesh material from built-in shaders pack |
| Colors.png | 8492e90aad408bd479586d821c1567dc | dial texture |
| click_sound.wav | 423598dd621711c43a06d22afbd5a3ae | click audio (ClickSource) |
| dial_fbx.fbx | f9068dfae0d2f92489ced2a9423b70b7 | knob mesh (RotatorDesktop + RotatorVR) |
| dial2_fbx.fbx | 3c38791b405792d47997b1f177853a80 | Base2 mesh |
| dial3_fbx.fbx | 9396ee9179c58e240b897fb2fa651d0b | Base3 mesh |

Deliberately NOT staged: dial.blend / dial2.blend / dial3.blend (authoring
sources; visuals reference the .fbx, and the one stale .blend collider reference
is repaired to its matching FBX mesh), repo-root
unitypackages, and anything outside Assets/_Dial.

## Dependency surface of the staged code (verified against source)

Dial.cs / DialDesktopInteractor.cs / DialVRInteractor.cs use only:
UnityEngine (MonoBehaviour, GameObject, AudioSource, BoxCollider, Mathf,
Transform), UdonSharp (UdonSharpBehaviour), VRC.SDKBase (Networking,
VRCPlayerApi, VRC_Pickup), VRC.Udon (UdonBehaviour). The prefab's Labels
canvas is plain legacy uGUI Text.

NOT used: AudioLink, CyanEmu (README lists it as optional editor aid only),
TMPro, any other PiMaker code. All required packages are already in the
target project (UdonSharp 1.1.x ships with VRChat Worlds SDK 3.10.3;
com.unity.ugui 1.0.0 is pinned in Packages/manifest.json).

## Reference resolution after isolated import

- m_Script guid c333ccfdd0cbdbc4ca30cef2dd6e6b9b (all three U# program
  assets) = VRCSDK UdonBehaviour MonoBehaviour guid; same guid appears in
  rvc's own Nix*.asset files and resolves from the installed SDK.
- serializedProgramAsset guids 0b7b6b594b19a2b458a0e50486340f5f (Dial),
  8d8080c84756c314ca2ca4ebf4ece0d2 (DialDesktopInteractor),
  834597e1baf705d40b7924b8a25b8ff7 (DialVRInteractor) exist nowhere in the
  repo - UdonSharp-generated program assets, exactly like rvc's Nix
  programs. UdonSharp recompiles and regenerates them on import; the
  project's existing repair/recompile flow already handles this pattern.
- programSource refs in the prefab point at the staged .asset.meta guids
  (65088335 / ec09ab09 / 151287bc) - all resolve.
- Prefab Labels text uses external font guid
  79f72428ef5a94f44a224932dfc8bc22 (legacy uGUI Text), which exists in
  neither the repo nor built-in resources - labels import with a missing
  font. The repair helper assigns Unity's built-in font.
- A MeshCollider uses missing authoring-mesh guid 895e80e; the repair
  helper assigns the same object's FBX MeshFilter.sharedMesh.

## Compatibility findings (control path)

Official Dial is NOT a drop-in for the staged RvcRunSwitch adapter:

1. NixControl.cs calls Dial.SetState(1, false), but the official
   Dial.SetState(int, bool) is PRIVATE in the only upstream version
   (2021-06-29, predates rvc 2022-11-25). Compiling rvc's NixControl.cs
   against unmodified official Dial.cs fails with CS0122. The local
   compatibility patch makes only Dial.SetState public and preserves
   NixControl's existing call.
2. Official Dial delivers "DialEnable"/"DialDisable" to the UdonBehaviours
   on GameObjects listed in its Behaviours[] array (by state index), only
   on transitions with transition=true. NixControl.DialEnable() reads
   Dial.NextState, so putting NixControl's GameObject into the Behaviours
   slots keeps the state machine working. On load no events fire
   (documented upstream) and SetState is gated by the suspension state, so
   the world's initial state must match CurrentState.
3. In exchange you get the real dial: safe VR grab-turn with haptics,
   desktop click-to-advance, 2-4 states, per-state EnableDisable and
   Behaviours arrays, click sound, label canvas.

The project restores NixControl's original Dial field type and changes only
the visibility of Dial.SetState. Place NixControl's GameObject in the Dial
Behaviours slots for the configured states. RvcRunSwitch remains available
as a separate simple-control implementation; it is not the NixControl field type.
The source assets retain CC BY-NC-SA 2.0 licensing, including this derivative.
