using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("nyix.mathbreakers.gsr", "Mathbreakers ghost speedrun mod", "1.0.0")]
public class MBMod : BaseUnityPlugin
{
  // Levels currently unlocked by our mod.
  public static readonly bool fast = false;
  public static readonly HashSet<int> UnlockedLevels = new HashSet<int>();
  public static HashSet<string> unlockedWeapons = new HashSet<string>();
  public static float maxBad = 0f;
  public static bool showDeaths = false;
  public static bool invertShift = false;

  public static ManualLogSource Log;
  public KeyCode DumpKey = KeyCode.F9;

  // FileSystemWatcher reference and flags for cross-thread sync
  private FileSystemWatcher watcher;
  private static string watchPath;
  public static float lastRestartTime;
  static string[] shaderNames;
  static int shaderIdx = -1;
  static List<Renderer> hidden = new List<Renderer>();

  public static bool shouldRestart = false;
  public static int currentMode = 0;

  private static readonly string MaxBadFilePath = "maxBadTime";
  public static bool loaded;
  public static bool loadCustomSkybox;
  static Texture2D bgTex;
  static Texture2D playerTex;
  static Mesh customMesh;
  static bool loadCustomPlayer;
  const float modelScale = 7f; // tweak after first look

  private void Awake()
  {
    Log = Logger;
    PlayerCoordsUI.Log = Logger;
    Log.LogInfo("================================");

    var harmony = new Harmony("nyix.mathbreakers.gsr");

    var uiObj2 = new GameObject("PlayerCoordsUI");
    UnityEngine.Object.DontDestroyOnLoad(uiObj2);
    var ui2 = uiObj2.AddComponent<PlayerCoordsUI>();
    harmony.PatchAll();

    showDeaths = File.Exists("showDeaths");
    if (File.Exists(MaxBadFilePath))
    {
      try
      {
        string fileContent = File.ReadAllText(MaxBadFilePath).Trim();
        if (float.TryParse(fileContent, out float parsedValue))
        {
          maxBad = parsedValue;
          Log.LogInfo($"Loaded maxBad value: {maxBad}");
        }
        else
        {
          Log.LogWarning($"Failed to parse float from file: {fileContent}");
        }
      }
      catch (Exception ex)
      {
        Log.LogError($"Error reading maxBadTime file: {ex.Message}");
      }
    }
    if (File.Exists("background.png"))
    {
      loadCustomSkybox = true;
      bgTex = new Texture2D(2, 2);
      bgTex.hideFlags = HideFlags.HideAndDontSave;
      bgTex.LoadImage(File.ReadAllBytes("background.png"));
      Log.LogInfo($"[BG] LoadImage={loaded} size={bgTex.width}x{bgTex.height}");
      var bgObj = new GameObject("BackgroundCamera");
      UnityEngine.Object.DontDestroyOnLoad(bgObj);
      var bgCam = bgObj.AddComponent<Camera>();
      bgCam.cullingMask = 0; // draws no scene objects
      bgCam.clearFlags = CameraClearFlags.SolidColor;
      bgCam.cullingMask = 1 << 31;
      bgObj.AddComponent<BackgroundImage>().tex = bgTex;
      bgCam.backgroundColor = Color.magenta;
    }
    invertShift = File.Exists("invertShift");
    if (File.Exists("player.obj"))
    {
      loadCustomPlayer = true;
      playerTex = new Texture2D(2, 2);
      playerTex.hideFlags = HideFlags.HideAndDontSave;
      playerTex.LoadImage(File.ReadAllBytes("player.png"));
      customMesh = LoadObj("player.obj");
      customMesh.hideFlags = HideFlags.HideAndDontSave;
    }
    SetupFileWatcher();

    Log.LogInfo("Mathbreakers Save Test loaded!");
  }

  static Mesh LoadObj(string path)
  {
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    var pos = new List<Vector3>();
    var uvs = new List<Vector2>();
    var verts = new List<Vector3>();
    var vertUv = new List<Vector2>();
    var tris = new List<int>();
    var map = new Dictionary<string, int>();

    foreach (string line in File.ReadAllLines(path))
    {
      string[] p = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      if (p.Length == 0)
        continue;

      if (p[0] == "v")
        pos.Add(
          new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv))
        ); // negate x: Unity is left-handed
      else if (p[0] == "vt")
        uvs.Add(new Vector2(float.Parse(p[1], inv), float.Parse(p[2], inv)));
      else if (p[0] == "f")
      {
        int[] idx = new int[p.Length - 1];
        for (int i = 1; i < p.Length; i++)
        {
          int id;
          if (!map.TryGetValue(p[i], out id))
          {
            string[] c = p[i].Split('/');
            id = verts.Count;
            verts.Add(pos[int.Parse(c[0]) - 1]);
            vertUv.Add(uvs[int.Parse(c[1]) - 1]); // throws if exported without UVs
            map[p[i]] = id;
          }
          idx[i - 1] = id;
        }
        for (int i = 1; i < idx.Length - 1; i++)
        {
          tris.Add(idx[0]);
          tris.Add(idx[i + 1]); // reversed winding to match the negated x
          tris.Add(idx[i]);
        }
      }
    }

    Mesh m = new Mesh();
    m.vertices = verts.ToArray();
    m.uv = vertUv.ToArray();
    m.triangles = tris.ToArray();
    m.RecalculateNormals();
    return m;
  }

  private void SetupFileWatcher()
  {
    // Watch the directory where the game executable or BepInEx root resides
    watchPath = Paths.GameRootPath;

    watcher = new FileSystemWatcher(watchPath);
    watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
    watcher.Filter = "mode";

    watcher.Changed += OnFileChanged;
    watcher.Created += OnFileChanged;

    watcher.EnableRaisingEvents = true;
    Log.LogInfo("[FileWatcher] Watching for commands in: " + watchPath);
  }

  private static void OnFileChanged(object sender, FileSystemEventArgs e)
  {
    string fileName = Path.GetFileName(e.FullPath).ToLower();

    if (fileName == "mode")
    {
      try
      {
        // Read contents of 'mode' file
        string content = File.ReadAllText(e.FullPath).Trim();
        if (int.TryParse(content, out int parsedMode))
        {
          currentMode = parsedMode + 1;
          shouldRestart = true;
          Log.LogInfo("[FileWatcher] Mode set to: " + currentMode);
          try
          {
            File.Delete(e.FullPath);
          }
          catch { }
        }
      }
      catch
      {
        // File may be temporarily locked by external process write
      }
    }
  }

  private void Update()
  {
    if (Input.GetKeyDown(KeyCode.F5))
    {
      SkinnedMeshRenderer smr = GameObject
        .FindWithTag("Player")
        .GetComponentInChildren<SkinnedMeshRenderer>();
      smr.sharedMaterial.color = Color.white;
    }
    if (Input.GetKeyDown(KeyCode.F6))
    {
      foreach (Renderer r in FindObjectsOfType(typeof(Renderer)) as Renderer[])
        r.enabled = true;
    }
    if (Input.GetKeyDown(KeyCode.F8))
    {
      foreach (Camera c in Camera.allCameras)
      {
        Log.LogInfo(
          $"[BG] camera '{c.name}' tag={c.tag} depth={c.depth} clear={c.clearFlags} bg={c.backgroundColor} mask={c.cullingMask} enabled={c.enabled}"
        );
      }
    }
    if (Input.GetKeyDown(KeyCode.F8))
    {
      Renderer[] all = FindObjectsOfType(typeof(Renderer)) as Renderer[];
      Array.Sort(all, (a, b) => b.bounds.size.magnitude.CompareTo(a.bounds.size.magnitude));
      for (int i = 0; i < 25 && i < all.Length; i++)
      {
        Renderer r = all[i];
        Log.LogInfo(
          $"[Sky] {r.name} | parent={r.transform.parent.name} | layer={r.gameObject.layer} | size={r.bounds.size.magnitude} | shader={r.material.shader.name} | queue={r.material.renderQueue}"
        );
      }
    }
    // Execute pending restart requests safely on Unity's main thread
    if (shouldRestart)
    {
      lastRestartTime = Time.time;
      shouldRestart = false;
      Log.LogInfo("[FileWatcher] Processing restart command...");
      if (PathComparer.IsRunning)
      {
        PathComparer.EndRun(false);
      }
      if (showDeaths)
      {
        GameObject deadPlayer = GameObject.FindWithTag("Player");
        if (deadPlayer == null)
        {
          Log.LogError("[DeathMarkers] No Player found on restart; death marker not stored.");
        }
        else
        {
          DeathMarkers.Add(Application.loadedLevel, deadPlayer.transform.position);
        }
      }
      Application.LoadLevel(currentMode);
    }

    if (
      !PathComparer.IsRunning
      && (
        Input.GetKeyDown(KeyCode.W)
        || Input.GetKeyDown(KeyCode.A)
        || Input.GetKeyDown(KeyCode.S)
        || Input.GetKeyDown(KeyCode.D)
      )
    )
    {
      Log.LogInfo("[PathComparer] WASD detected, starting run on level " + Application.loadedLevel);
      PathComparer.StartRun(Application.loadedLevel);
    }

    if (Input.GetKeyDown(KeyCode.F4))
    {
      HitboxMarkers.Toggle();
    }

    if (Input.GetKeyDown(KeyCode.F10))
    {
      GameObject egg = GameObject.Find("Snowball");
      if (egg != null)
      {
        Debug.Log(egg.transform.position + " GameObject.Find('easter egg').position");
      }
      else
      {
        Debug.Log("Easter egg GameObject not found in current scene.");
      }
    }

    if (Input.GetKeyDown(DumpKey))
    {
      Debug.Log("[NodeDumper] === DUMPING ALL LEVEL NODES ===");

      Transform[] allTransforms = FindObjectsOfType(typeof(Transform)) as Transform[];
      if (allTransforms != null)
      {
        foreach (Transform t in allTransforms)
        {
          if (t == null)
            continue;

          Component[] components = t.GetComponents<Component>();
          string componentList = "";
          foreach (Component comp in components)
          {
            if (comp != null)
            {
              componentList += comp.GetType().Name + ", ";
            }
          }

          Debug.Log("[NodeDumper] Node: " + t.name + " | Components: [" + componentList + "]");
        }
      }
      Debug.Log("[NodeDumper] === DUMP COMPLETE ===");
    }

    if (fast || Input.GetKeyDown(KeyCode.F7))
    {
      GameObject player = GameObject.FindWithTag("Player");
      if (player != null)
      {
        var walker = player.GetComponent("FPSWalkerEnhanced");
        if (walker != null)
        {
          var type = walker.GetType();
          string[] speedFields =
          {
            "speed",
            "walkSpeed",
            "runSpeed",
            "moveSpeed",
            "jumpSpeed",
            "ySpeed",
            "gravity",
          };

          if (
            (float)
              type.GetField(
                  "walkSpeed",
                  BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                )
                .GetValue(walker) == 24f
          ) // Prevent multi-frame stacking
          {
            foreach (var fieldName in speedFields)
            {
              var field = type.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
              );
              if (field != null && field.FieldType == typeof(float))
              {
                float val = (float)field.GetValue(walker);
                field.SetValue(walker, val * 7f);
                Debug.Log(
                  string.Format(
                    "[MBMod] Quadrupled FPSWalkerEnhanced.{0} to {1}",
                    fieldName,
                    val * 4f
                  )
                );
              }
              if (field != null && field.FieldType == typeof(Vector3))
              {
                Vector3 val = (Vector3)field.GetValue(walker);
                field.SetValue(walker, val * 4f);
                Debug.Log(
                  string.Format(
                    "[MBMod] Quadrupled FPSWalkerEnhanced.{0} to {1}",
                    fieldName,
                    val * 4f
                  )
                );
              }
            }
          }
        }
      }
    }
  }

  private void OnLevelWasLoaded(int level)
  {
    shaderIdx = -1;
    if (loadCustomSkybox)
    {
      Camera cam = Camera.main;
      cam.clearFlags = CameraClearFlags.SolidColor;
      cam.backgroundColor = Color.black;
      GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
      Destroy(quad.GetComponent<Collider>());
      quad.transform.parent = cam.transform;

      float d = cam.farClipPlane * 0.999f;
      float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
      Vector3 s = cam.transform.lossyScale;

      quad.transform.localRotation = Quaternion.identity;
      quad.transform.localPosition = new Vector3(0f, 0f, d / s.z);
      quad.transform.localScale = new Vector3(h * cam.aspect / s.x, h / s.y, 1f);

      Material mat = new Material(Shader.Find("Unlit/Texture"));
      mat.mainTexture = bgTex;
      mat.renderQueue = 1000;
      quad.renderer.material = mat;
    }
    if (loadCustomPlayer)
    {
      GameObject player = GameObject.FindWithTag("Player");
      SkinnedMeshRenderer smr = player.GetComponentInChildren<SkinnedMeshRenderer>();
      Mesh old = smr.sharedMesh;

      // Copy so each level load starts from the same source mesh
      Mesh m = (Mesh)UnityEngine.Object.Instantiate(customMesh);
      Vector3[] v = m.vertices;
      Quaternion fix = Quaternion.Euler(90f, 0f, 0f);
      for (int i = 0; i < v.Length; i++)
        v[i] = fix * v[i];
      m.vertices = v;
      m.RecalculateBounds();

      Bounds ob = old.bounds;
      Bounds nb = m.bounds;

      float s = ob.size.y / nb.size.y * modelScale;
      Vector3 srcAnchor = new Vector3(nb.center.x, nb.min.y, nb.center.z);
      Vector3 dstAnchor = new Vector3(ob.center.x, ob.min.y, ob.center.z);
      for (int i = 0; i < v.Length; i++)
        v[i] = (v[i] - srcAnchor) * s + dstAnchor;
      m.vertices = v;
      m.RecalculateBounds();

      // Bone positions in mesh space come from the old mesh's bind poses
      Matrix4x4[] bind = old.bindposes;
      Vector3[] bonePos = new Vector3[bind.Length];
      for (int b = 0; b < bind.Length; b++)
        bonePos[b] = bind[b].inverse.MultiplyPoint3x4(Vector3.zero);

      int iBody = -1,
        iHead = -1,
        iArmR = -1,
        iArmL = -1;
      for (int b = 0; b < smr.bones.Length; b++)
      {
        string n = smr.bones[b].name;
        if (n == "body")
          iBody = b;
        else if (n == "head")
          iHead = b;
        else if (n == "arm_right")
          iArmR = b;
        else if (n == "arm_left")
          iArmL = b;
      }
      if (iBody < 0 || iHead < 0 || iArmR < 0 || iArmL < 0)
        throw new Exception("player is missing one of body/head/arm_right/arm_left");

      string[] wb = File.ReadAllText("wingbox.txt")
        .Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
      if (wb.Length != 5)
        throw new Exception("wingbox.txt needs 5 numbers: yMin yMax zMin zMax sideMin");
      var ci = System.Globalization.CultureInfo.InvariantCulture;
      float yMin = float.Parse(wb[0], ci),
        yMax = float.Parse(wb[1], ci),
        zMin = float.Parse(wb[2], ci),
        zMax = float.Parse(wb[3], ci),
        sideMin = float.Parse(wb[4], ci);

      Vector3 armSpan = bonePos[iArmR] - bonePos[iArmL];
      Log.LogInfo($"[Player] arm span direction={armSpan.normalized} (should be about +/-1 on x)");

      Bounds bb = m.bounds;
      BoneWeight[] weights = new BoneWeight[v.Length];
      bool[] isWing = new bool[v.Length];
      int wingCount = 0;
      for (int i = 0; i < v.Length; i++)
      {
        Vector3 n = new Vector3(
          (v[i].x - bb.min.x) / bb.size.x,
          (v[i].y - bb.min.y) / bb.size.y,
          (v[i].z - bb.min.z) / bb.size.z
        );
        float side = Mathf.Abs(n.x - 0.5f) * 2f;
        isWing[i] = n.y >= yMin && n.y <= yMax && n.z >= zMin && n.z <= zMax && side >= sideMin;

        int best = -1;
        if (isWing[i])
        {
          best = Vector3.Dot(v[i] - bb.center, armSpan) > 0f ? iArmR : iArmL;
          wingCount++;
        }
        else
        {
          float bestDist = float.MaxValue;
          for (int b = 0; b < bonePos.Length; b++)
          {
            if (b == iArmR || b == iArmL)
              continue;
            float d = (v[i] - bonePos[b]).sqrMagnitude;
            if (d < bestDist)
            {
              bestDist = d;
              best = b;
            }
          }
        }
        weights[i].boneIndex0 = best;
        weights[i].weight0 = 1f;
      }
      Log.LogInfo($"[Player] {wingCount}/{v.Length} vertices in wing region");

      // Split into two submeshes so the wing region can be shown in a different color
      int[] tri = m.triangles;
      List<int> bodyTris = new List<int>();
      List<int> wingTris = new List<int>();
      for (int t = 0; t < tri.Length; t += 3)
      {
        List<int> dst = isWing[tri[t]] ? wingTris : bodyTris;
        dst.Add(tri[t]);
        dst.Add(tri[t + 1]);
        dst.Add(tri[t + 2]);
      }
      m.subMeshCount = 2;
      m.SetTriangles(bodyTris.ToArray(), 0);
      m.SetTriangles(wingTris.ToArray(), 1);
      m.boneWeights = weights;
      m.bindposes = bind;
      Vector3 shift = new Vector3(0f, -1f, 15f);
      for (int i = 0; i < v.Length; i++)
        v[i] += shift;
      m.vertices = v;
      m.RecalculateBounds();
      smr.sharedMesh = m;

      Material mat = new Material(Shader.Find("Diffuse"));
      mat.mainTexture = playerTex;
      mat.color = Color.white;

      Material wingMat = mat;
      if (File.Exists("wingdebug"))
      {
        wingMat = new Material(Shader.Find("Diffuse"));
        wingMat.color = Color.red;
      }
      smr.sharedMaterials = new Material[] { mat, wingMat };
      CustomPlayerAnim anim = smr.gameObject.AddComponent<CustomPlayerAnim>();
      anim.Init(smr, player.transform);
    }
    if (showDeaths)
      DeathMarkers.SpawnAll(level);
  }

  private void OnDestroy()
  {
    if (watcher != null)
    {
      watcher.EnableRaisingEvents = false;
      watcher.Dispose();
    }
  }
}

[HarmonyPatch(typeof(EndLevelTrigger), "OnTriggerEnter")]
public static class EndLevelTrigger_OnTriggerEnter_Patch
{
  public static void Prefix(EndLevelTrigger __instance, Collider other, float ___timeout)
  {
    if (__instance != null)
    {
      MBMod.Log.LogInfo(
        "[PathComparer] EndLevelTrigger fired. timeout=" + ___timeout + " otherTag=" + other.tag
      );

      if (___timeout < 0f && other.tag == "Player")
      {
        int currentLevel = Application.loadedLevel;

        MBMod.Log.LogInfo("[PathComparer] Calling EndRun(true) for level " + currentLevel);
        PathComparer.EndRun(true);
        File.Create("level_cleared.txt").Close();
      }
    }
  }
}

[HarmonyPatch(typeof(NumberHoopCheckpoint), "Start")]
public static class NumberHoopCheckpoint_Start_Patch
{
  public static void Postfix(NumberHoopCheckpoint __instance)
  {
    BoxCollider box = __instance.gameObject.GetComponent<BoxCollider>();
    HitboxMarkers.Create(box, new Color(1f, 0f, 0f, 0.5f));
  }
}

public class BackgroundImage : MonoBehaviour
{
  public Texture2D tex;

  void OnPostRender()
  {
    Graphics.Blit(tex, (RenderTexture)null);
  }
}

public class CustomPlayerAnim : MonoBehaviour
{
  // Clips come from this file (game dir). See player.anim for the format.
  const string animFile = "player.anim";

  // Tune these to your game's units
  const float runSpeed = 2f; // horizontal speed above this counts as walking/running
  const float jumpVel = 1f; // upward speed above this counts as jumping
  const float fallVel = -2f; // downward speed below this counts as falling
  const float stride = 0.5f; // run cycles per unit of distance traveled
  const float fadeTime = 0.1f; // crossfade between states

  // +1 or -1: flip if a pitch/roll in the file rotates the wrong way
  const float pitchSign = 1f;
  const float flapSign = 1f;

  struct Key
  {
    public float time,
      pitch,
      yaw,
      roll;
  }

  class ClipDef
  {
    public string name;
    public bool loop;
    public Dictionary<string, List<Key>> bones = new Dictionary<string, List<Key>>();
  }

  // Driving mode for Replay/Ghost
  public bool isReplayGhost = false;
  public Vector3 currentGhostVelocity;

  Animation anim;
  Transform tracked;
  Transform frame; // parent of the bones
  Quaternion modelBasis = Quaternion.identity;
  Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
  Dictionary<string, Quaternion> rest = new Dictionary<string, Quaternion>();
  Vector3 lastPos;
  float vy;

  string playerState = "idle";
  string lastState = "idle";
  string currentAnim = "idle";

  public void Init(SkinnedMeshRenderer smr, Transform trackedPlayer)
  {
    tracked = trackedPlayer;
    if (tracked != null)
      lastPos = tracked.position;

    // Identify model root / frame
    if (smr.bones.Length > 0 && smr.bones[0] != null)
    {
      frame = smr.bones[0].parent;
    }
    if (frame == null)
      frame = smr.transform;

    // Dynamically index ALL bones/transforms in the model hierarchy
    Transform[] allTransforms = frame.GetComponentsInChildren<Transform>(true);
    foreach (Transform t in allTransforms)
    {
      if (!bones.ContainsKey(t.name))
      {
        bones[t.name] = t;
        rest[t.name] = t.localRotation;
      }
    }

    // Calculate model basis if default bones exist; fallback to identity for custom rigs
    if (
      bones.ContainsKey("arm_right")
      && bones.ContainsKey("arm_left")
      && bones.ContainsKey("head")
      && bones.ContainsKey("body")
    )
    {
      Vector3 sideLocal = frame.InverseTransformDirection(
        (bones["arm_right"].position - bones["arm_left"].position).normalized
      );
      Vector3 upLocal = frame.InverseTransformDirection(
        (bones["head"].position - bones["body"].position).normalized
      );
      Vector3 forwardLocal = Vector3.Cross(sideLocal, upLocal).normalized;
      modelBasis = Quaternion.LookRotation(forwardLocal, upLocal);
    }
    else
    {
      modelBasis = Quaternion.identity;
    }

    anim = frame.gameObject.GetComponent<Animation>();
    if (anim == null)
      anim = frame.gameObject.AddComponent<Animation>();

    anim.cullingType = AnimationCullingType.AlwaysAnimate;

    if (File.Exists(animFile))
    {
      List<ClipDef> clipDefs = ParseAnimFile(animFile);
      foreach (ClipDef def in clipDefs)
      {
        AnimationClip clip = BuildClip(def);
        if (clip != null)
          anim.AddClip(clip, def.name);
      }
    }
    else
    {
      MBMod.Log.LogWarning("[CustomPlayerAnim] " + animFile + " not found!");
    }
  }

  static List<ClipDef> ParseAnimFile(string path)
  {
    var ci = System.Globalization.CultureInfo.InvariantCulture;
    List<ClipDef> defs = new List<ClipDef>();
    ClipDef clip = null;
    List<Key> keys = null;
    string[] lines = File.ReadAllLines(path);
    for (int n = 0; n < lines.Length; n++)
    {
      string line = lines[n].Trim();
      if (line.Length == 0 || line[0] == '#')
        continue;
      string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
      try
      {
        if (p[0] == "clip")
        {
          if (p.Length != 3 || (p[2] != "loop" && p[2] != "once"))
            throw new Exception("expected 'clip <name> <loop|once>'");
          clip = new ClipDef { name = p[1], loop = p[2] == "loop" };
          defs.Add(clip);
        }
        else if (p[0] == "bone")
        {
          if (p.Length != 2)
            throw new Exception("expected 'bone <name>'");
          keys = new List<Key>();
          clip.bones.Add(p[1], keys);
        }
        else
        {
          if (p.Length != 4)
            throw new Exception("expected '<time> <pitch> <yaw> <roll>'");
          keys.Add(
            new Key
            {
              time = float.Parse(p[0], ci),
              pitch = float.Parse(p[1], ci),
              yaw = float.Parse(p[2], ci),
              roll = float.Parse(p[3], ci),
            }
          );
        }
      }
      catch (Exception e)
      {
        throw new Exception(path + ":" + (n + 1) + ": " + e.Message, e);
      }
    }
    return defs;
  }

  AnimationClip BuildClip(ClipDef def)
  {
    AnimationClip clip = new AnimationClip();
    clip.name = def.name;
    clip.wrapMode = def.loop ? WrapMode.Loop : WrapMode.ClampForever;

    foreach (KeyValuePair<string, List<Key>> kv in def.bones)
    {
      if (!bones.ContainsKey(kv.Key))
      {
        MBMod.Log.LogWarning(
          "[CustomPlayerAnim] Skipping bone '"
            + kv.Key
            + "' in clip '"
            + def.name
            + "' (not found on model)."
        );
        continue;
      }
      Transform bone = bones[kv.Key];

      Quaternion m = Quaternion.Inverse(bone.parent.rotation) * frame.rotation * modelBasis;

      AnimationCurve[] c =
      {
        new AnimationCurve(),
        new AnimationCurve(),
        new AnimationCurve(),
        new AnimationCurve(),
      };
      Quaternion prev = Quaternion.identity;
      foreach (Key k in kv.Value)
      {
        Quaternion delta =
          Quaternion.AngleAxis(k.roll * flapSign, Vector3.forward)
          * Quaternion.AngleAxis(k.pitch * pitchSign, Vector3.right)
          * Quaternion.AngleAxis(k.yaw, Vector3.up);
        Quaternion q = m * delta * Quaternion.Inverse(m) * rest[kv.Key];

        if (c[0].length > 0 && Quaternion.Dot(prev, q) < 0f)
          q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
        prev = q;

        c[0].AddKey(k.time, q.x);
        c[1].AddKey(k.time, q.y);
        c[2].AddKey(k.time, q.z);
        c[3].AddKey(k.time, q.w);
      }
      foreach (AnimationCurve curve in c)
      {
        for (int i = 0; i < curve.length; i++)
          curve.SmoothTangents(i, 0f);
      }

      string path = PathFromFrame(bone);
      clip.SetCurve(path, typeof(Transform), "localRotation.x", c[0]);
      clip.SetCurve(path, typeof(Transform), "localRotation.y", c[1]);
      clip.SetCurve(path, typeof(Transform), "localRotation.z", c[2]);
      clip.SetCurve(path, typeof(Transform), "localRotation.w", c[3]);
    }
    return clip;
  }

  string PathFromFrame(Transform bone)
  {
    if (bone == frame)
      return "";
    string path = bone.name;
    for (Transform t = bone.parent; t != null && t != frame; t = t.parent)
    {
      path = t.name + "/" + path;
    }
    return path;
  }

  void LateUpdate()
  {
    float dt = Time.deltaTime;
    if (dt <= 0f)
      return;

    Vector3 vel;
    if (isReplayGhost)
    {
      vel = currentGhostVelocity;
    }
    else if (tracked != null)
    {
      vel = (tracked.position - lastPos) / dt;
      lastPos = tracked.position;
    }
    else
    {
      vel = Vector3.zero;
    }

    float hSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
    vy = Mathf.Lerp(vy, vel.y, 1f - Mathf.Exp(-10f * dt));

    // Determine current physical player state
    if (vy > jumpVel)
      playerState = "jump";
    else if (vy < fallVel)
      playerState = "fall";
    else if (hSpeed > runSpeed)
      playerState = "walk";
    else
      playerState = "idle";

    // State machine animation resolution
    string nextAnimation = "idle";

    switch (playerState)
    {
      case "jump":
        switch (lastState)
        {
          case "jump":
          case "fly":
            nextAnimation = "fly";
            break;
          default:
            nextAnimation = "jump";
            break;
        }
        break;

      case "walk":
        nextAnimation = "walk";
        break;

      case "fall":
        nextAnimation = "fall";
        break;

      default:
        nextAnimation = "idle";
        break;
    }

    // Safety check: Fall back gracefully if a requested clip doesn't exist
    if (anim[nextAnimation] == null)
    {
      if (nextAnimation == "fly" && anim["jump"] != null)
        nextAnimation = "jump";
      else if (nextAnimation == "walk" && anim["run"] != null)
        nextAnimation = "run";
      else if (anim["idle"] != null)
        nextAnimation = "idle";
    }

    // Crossfade to new animation when state changes
    if (nextAnimation != currentAnim)
    {
      lastState = playerState;
      currentAnim = nextAnimation;
      if (anim[currentAnim] != null)
        anim.CrossFade(currentAnim, fadeTime);
    }

    // Scale movement animation speed by player movement speed
    if ((currentAnim == "walk" || currentAnim == "run") && anim[currentAnim] != null)
      anim[currentAnim].speed = hSpeed * stride * anim[currentAnim].length;
  }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKey), new Type[] { typeof(KeyCode) })]
public static class Input_GetKey_Patch
{
  public static void Postfix(KeyCode key, ref bool __result)
  {
    if (key == KeyCode.LeftShift && MBMod.invertShift)
    {
      __result = !__result;
    }
  }
}
