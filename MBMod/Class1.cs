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

  public static bool shouldRestart = false;
  public static int currentMode = 0;

  private static readonly string MaxBadFilePath = "maxBadTime";
  public static bool loaded;
  static Texture2D bgTex;

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
    bgTex = new Texture2D(2, 2);
    bgTex.hideFlags = HideFlags.HideAndDontSave;
    bgTex.LoadImage(File.ReadAllBytes("background.png")); // relative to game root, like your other files
    Log.LogInfo($"[BG] LoadImage={loaded} size={bgTex.width}x{bgTex.height}");
    var bgObj = new GameObject("BackgroundCamera");
    UnityEngine.Object.DontDestroyOnLoad(bgObj);
    var bgCam = bgObj.AddComponent<Camera>();
    bgCam.cullingMask = 0; // draws no scene objects
    bgCam.clearFlags = CameraClearFlags.SolidColor;
    bgCam.cullingMask = 1 << 31;
    bgObj.AddComponent<BackgroundImage>().tex = bgTex;
    bgCam.backgroundColor = Color.magenta;
    SetupFileWatcher();

    Log.LogInfo("Mathbreakers Save Test loaded!");
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
          Log.LogInfo("[FileWatcher] Mode updated to: " + currentMode);
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
    Camera cam = Camera.main;
    cam.clearFlags = CameraClearFlags.SolidColor;
    cam.backgroundColor = Color.black;

    GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
    Destroy(quad.GetComponent<Collider>());
    quad.transform.parent = cam.transform;

    float d = cam.farClipPlane * 0.9f;
    float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
    Vector3 s = cam.transform.lossyScale;

    quad.transform.localRotation = Quaternion.identity;
    quad.transform.localPosition = new Vector3(0f, 0f, d / s.z);
    quad.transform.localScale = new Vector3(h * cam.aspect / s.x, h / s.y, 1f);

    Material mat = new Material(Shader.Find("Unlit/Texture"));
    mat.mainTexture = bgTex;
    mat.renderQueue = 1000;
    quad.renderer.material = mat;

    Log.LogInfo(
      $"[BG] dist={(quad.transform.position - cam.transform.position).magnitude} far={cam.farClipPlane} lossy={s}"
    );

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
