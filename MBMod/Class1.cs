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
  const float modelScale = 10f; // tweak after first look

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
    // if (Input.GetKeyDown(KeyCode.F3))
    // {
    //   GameObject player = GameObject.FindWithTag("Player");
    //   Mesh mesh = player.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
    //   int size = 1024;
    //   Texture2D t = new Texture2D(size, size);
    //   Color[] fill = new Color[size * size];
    //   for (int i = 0; i < fill.Length; i++)
    //     fill[i] = Color.black;
    //   t.SetPixels(fill);

    //   Vector2[] uv = mesh.uv;
    //   int[] tris = mesh.triangles;
    //   for (int i = 0; i < tris.Length; i += 3)
    //   {
    //     for (int e = 0; e < 3; e++)
    //     {
    //       Vector2 a = uv[tris[i + e]] * (size - 1);
    //       Vector2 b = uv[tris[i + (e + 1) % 3]] * (size - 1);
    //       int steps = (int)Vector2.Distance(a, b) + 1;
    //       for (int s = 0; s <= steps; s++)
    //       {
    //         Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
    //         t.SetPixel((int)p.x, (int)p.y, Color.white);
    //       }
    //     }
    //   }
    //   t.Apply();
    //   File.WriteAllBytes("uv_template.png", t.EncodeToPNG());
    // }
    // if (Input.GetKeyDown(KeyCode.F5))
    // {
    //   GameObject player = GameObject.FindWithTag("Player");
    //   SkinnedMeshRenderer smr = player.GetComponentInChildren<SkinnedMeshRenderer>();
    //   Material m = smr.material;
    //   Mesh mesh = smr.sharedMesh;
    //   Log.LogInfo(
    //     $"[Player] mesh={mesh.name} verts={mesh.vertexCount} uv={mesh.uv.Length} colors={mesh.colors.Length} bones={smr.bones.Length} rootBone={smr.rootBone}"
    //   );
    //   Log.LogInfo(
    //     $"[Player] _MainTex={m.HasProperty("_MainTex")} _Color={m.HasProperty("_Color")} color={m.color}"
    //   );
    //   foreach (Transform b in smr.bones)
    //     Log.LogInfo($"[Player] bone {b.name}");
    // }
    if (Input.GetKeyDown(KeyCode.F6))
    {
      foreach (Renderer r in hidden)
        r.enabled = true;
      hidden.Clear();

      Renderer[] all = FindObjectsOfType(typeof(Renderer)) as Renderer[];

      if (shaderIdx == -1)
      {
        var set = new HashSet<string>();
        foreach (Renderer r in all)
          set.Add(r.sharedMaterial.shader.name);
        shaderNames = new string[set.Count];
        set.CopyTo(shaderNames);
      }

      shaderIdx = (shaderIdx + 1) % shaderNames.Length;
      string target = shaderNames[shaderIdx];

      foreach (Renderer r in all)
      {
        if (r.sharedMaterial.shader.name == target)
        {
          r.enabled = false;
          hidden.Add(r);
        }
      }
      Log.LogInfo($"[Bisect] hiding {hidden.Count} renderers using '{target}'");
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

      BoneWeight[] weights = new BoneWeight[v.Length];
      for (int i = 0; i < v.Length; i++)
      {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int b = 0; b < bonePos.Length; b++)
        {
          float d = (v[i] - bonePos[b]).sqrMagnitude;
          if (d < bestDist)
          {
            bestDist = d;
            best = b;
          }
        }
        weights[i].boneIndex0 = best;
        weights[i].weight0 = 1f;
      }
      m.boneWeights = weights;
      m.bindposes = bind;

      smr.sharedMesh = m;

      Material mat = new Material(Shader.Find("Diffuse"));
      mat.mainTexture = playerTex;
      smr.sharedMaterials = new Material[] { mat }; // mat.shader = Shader.Find("Diffuse"); // or "Transparent/Cutout/Diffuse" if the texture has alpha
      mat.color = Color.white;
      smr.sharedMaterial = mat;

      Log.LogInfo(
        $"[Player] shader={smr.material.shader.name} tex={smr.material.mainTexture} color={smr.material.color}"
      );
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
