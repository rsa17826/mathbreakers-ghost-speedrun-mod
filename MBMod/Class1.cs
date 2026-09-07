using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("nyix.mathbreakers.saves", "Mathbreakers Save Test", "1.0.0")]
public class MBMod : BaseUnityPlugin
{
  // Levels currently unlocked by our mod.
  public static readonly bool fast = false;
  public static readonly HashSet<int> UnlockedLevels = new HashSet<int>();
  public static HashSet<string> unlockedWeapons = new HashSet<string>();

  public static ManualLogSource Log;
  public KeyCode DumpKey = KeyCode.F9;

  private void Update()
  {
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
      PathComparer.StartRun(Application.loadedLevel);
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
    if (fast)
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

  private void Awake()
  {
    Log = Logger;

    Log.LogInfo("================================");

    var harmony = new Harmony("nyix.mathbreakers.a");

    var uiObj2 = new GameObject("PlayerCoordsUI");
    UnityEngine.Object.DontDestroyOnLoad(uiObj2);
    var ui2 = uiObj2.AddComponent<PlayerCoordsUI>();
    harmony.PatchAll();
    Log.LogInfo("Mathbreakers Save Test loaded!");
  }
}

[HarmonyPatch(typeof(EndLevelTrigger), "OnTriggerEnter")]
public static class EndLevelTrigger_OnTriggerEnter_Patch
{
  // Inject the 'other' parameter and the private 'timeout' field (using ___)
  public static void Prefix(EndLevelTrigger __instance, Collider other, float ___timeout)
  {
    if (__instance != null)
    {
      // Replicate the original trigger condition
      if (___timeout < 0f && other.tag == "Player")
      {
        // Store the level integers to prevent accidental string concatenation issues
        int currentLevel = Application.loadedLevel;
        int nextLevel = currentLevel + 1;

        // Call your custom method
        PathComparer.EndRun(true);
      }
    }
  }
}
