using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using ArchipelagoNet;
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
  public static ArchipelagoClient ApClient;
  public KeyCode DumpKey = KeyCode.F9;

  private void Update()
  {
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
    Log.LogInfo("Mathbreakers Save Test loading...");
    Log.LogInfo("================================");

    var harmony = new Harmony("nyix.mathbreakers.saves");

    // Find UnityEngine.PlayerPrefs methods.
    MethodInfo numberGeneratorStart = AccessTools.Method(typeof(NumberGenerator), "Start");

    if (numberGeneratorStart == null)
    {
      Log.LogError("Could not find NumberGenerator.Start!");
    }
    else
    {
      harmony.Patch(
        numberGeneratorStart,
        postfix: new HarmonyMethod(
          typeof(NumberGenerator_StartPatch),
          nameof(NumberGenerator_StartPatch.Postfix)
        )
      );
      Log.LogInfo("Patched NumberGenerator.Start");
    }
    MethodInfo setInt = AccessTools.Method(
      typeof(PlayerPrefs),
      "SetInt",
      new Type[] { typeof(string), typeof(int) }
    );
    MethodInfo getint = AccessTools.Method(
      typeof(PlayerPrefs),
      "GetInt",
      new Type[] { typeof(string), typeof(int) }
    );

    MethodInfo hasKey = AccessTools.Method(
      typeof(PlayerPrefs),
      "HasKey",
      new Type[] { typeof(string) }
    );

    if (setInt == null)
    {
      Log.LogError("Could not find PlayerPrefs.SetInt!");
    }
    else
    {
      harmony.Patch(
        setInt,
        prefix: new HarmonyMethod(typeof(MBMod), nameof(PlayerPrefsSetIntPrefix))
      );
      harmony.Patch(getint, prefix: new HarmonyMethod(typeof(MBMod), nameof(getintPrefix)));

      Log.LogInfo("Patched PlayerPrefs.SetInt");
    }

    if (hasKey == null)
    {
      Log.LogError("Could not find PlayerPrefs.HasKey!");
    }
    else
    {
      harmony.Patch(
        hasKey,
        prefix: new HarmonyMethod(typeof(MBMod), nameof(PlayerPrefsHasKeyPrefix))
      );

      Log.LogInfo("Patched PlayerPrefs.HasKey");
    }

    MethodInfo lightAreaStart = AccessTools.Method(typeof(PositiveNegativeLightArea), "Start");

    if (lightAreaStart == null)
    {
      Log.LogError("Could not find PositiveNegativeLightArea.Start!");
    }
    else
    {
      harmony.Patch(
        lightAreaStart,
        postfix: new HarmonyMethod(
          typeof(PositiveNegativeLightArea_StartPatch),
          nameof(PositiveNegativeLightArea_StartPatch.Postfix)
        )
      );

      Log.LogInfo("Patched PositiveNegativeLightArea.Start");
    }

    MethodInfo numberHoopStart = AccessTools.Method(typeof(NumberHoop), "Start");

    if (numberHoopStart == null)
    {
      Log.LogError("Could not find NumberHoop.Start!");
    }
    else
    {
      harmony.Patch(
        numberHoopStart,
        postfix: new HarmonyMethod(
          typeof(NumberHoop_StartPatch),
          nameof(NumberHoop_StartPatch.Postfix)
        )
      );

      Log.LogInfo("Patched NumberHoop.Start");
    }
    MethodInfo createNewWall = AccessTools.Method(
      AccessTools.TypeByName("NumberWallCreator"),
      "CreateNewWall"
    );

    if (createNewWall != null)
    {
      harmony.Patch(
        createNewWall,
        prefix: new HarmonyMethod(typeof(MBMod), nameof(NumberWallCreatorPrefix))
      );
      Log.LogInfo("Patched NumberWallCreator.CreateNewWall");
    }
    MethodInfo modifyNumber = AccessTools.Method(
      AccessTools.TypeByName("NumberManager"),
      "ModifyNumber",
      new Type[]
      {
        typeof(UnityEngine.GameObject),
        AccessTools.TypeByName("Fraction"),
        typeof(bool),
        AccessTools.TypeByName("OperationType"),
        typeof(bool),
        typeof(string),
      }
    );

    if (modifyNumber != null)
    {
      harmony.Patch(
        modifyNumber,
        prefix: new HarmonyMethod(typeof(MBMod), nameof(ModifyNumberPrefix))
      );
      Log.LogInfo("Patched NumberManager.ModifyNumber (Strict Signature)");
    }
    else
    {
      Log.LogError("Could not find the specific ModifyNumber overload!");
    }
    var numberInfoType = AccessTools.TypeByName("NumberInfo");
    if (numberInfoType != null)
    {
      var onDestroyNumberMethod = AccessTools.Method(numberInfoType, "OnDestroyNumber");
      if (onDestroyNumberMethod != null)
      {
        harmony.Patch(
          onDestroyNumberMethod,
          prefix: new HarmonyMethod(typeof(MBMod), nameof(NumberInfoOnDestroyNumberPrefix))
        );
        Log.LogInfo("Patched NumberInfo.OnDestroyNumber successfully!");
      }
    }
    var uiObj = new GameObject("APConnectUI");
    UnityEngine.Object.DontDestroyOnLoad(uiObj);
    var ui = uiObj.AddComponent<APConnectUI>();
    var uiObj2 = new GameObject("PlayerCoordsUI");
    UnityEngine.Object.DontDestroyOnLoad(uiObj2);
    var ui2 = uiObj2.AddComponent<PlayerCoordsUI>();
    ui.OnConnectRequested += (hostname, port, game, playerName, password) =>
    {
      var client = new ArchipelagoClient(hostname, port, game, playerName, password);
      ApClient = client;
      client.OnLog += msg => Log.LogInfo(msg);
      client.OnError += msg =>
      {
        Log.LogError(msg);
        ui.SetStatus("Error: " + msg);
      };
      client.OnConnectedEvent += () =>
      {
        ui.SetStatus("Connected!");
        client.MarkPlayerLoaded(); // TODO
        // SendNewLocationCheck("weapon:throw");
        SendNewLocationCheck("menu - level:level1");
      };
      client.OnGiveItem += (itemName, item) =>
      {
        if (itemName == "Light Area Unlock")
        {
          ApState.LightAreaUnlocked = true;
          foreach (
            var area in UnityEngine.Object.FindObjectsOfType(typeof(PositiveNegativeLightArea))
          )
          {
            var col = MBMod.FindTriggerCollider((PositiveNegativeLightArea)area);
            if (col != null)
              col.enabled = true;
          }
          Log.LogInfo(
            "[AP] Light Area Unlock received - enabling all PositiveNegativeLightArea colliders."
          );
        }
        else if (itemName == "level:level1")
        {
          UnlockedLevels.Add(1);
        }
        else if (itemName == "level:level2")
        {
          UnlockedLevels.Add(2);
        }
        else if (itemName == "level:level3")
        {
          UnlockedLevels.Add(3);
        }
        else if (itemName == "level:level4")
        {
          UnlockedLevels.Add(4);
        }
        else if (itemName == "level:level5")
        {
          UnlockedLevels.Add(5);
        }
        else if (itemName == "trap:nothing") { }
        else if (
          itemName == "Multiply Hoop Unlock"
          || itemName == "Add Hoop Unlock"
          || itemName == "Exponent Hoop Unlock"
        )
        {
          HoopType unlockedType =
            itemName == "Multiply Hoop Unlock" ? HoopType.Multiply
            : itemName == "Add Hoop Unlock" ? HoopType.Add
            : HoopType.Exponent;

          if (unlockedType == HoopType.Multiply)
            ApState.HoopMultiplyUnlocked = true;
          if (unlockedType == HoopType.Add)
            ApState.HoopAddUnlocked = true;
          if (unlockedType == HoopType.Exponent)
            ApState.HoopExponentUnlocked = true;

          // Only enable colliders on hoops matching this specific
          // type - other hoop types stay gated on their own item.
          foreach (var hoopObj in UnityEngine.Object.FindObjectsOfType(typeof(NumberHoop)))
          {
            var hoop = (NumberHoop)hoopObj;
            if (hoop.ht != unlockedType)
              continue;

            var col = MBMod.FindTriggerCollider(hoop);
            if (col != null)
              col.enabled = true;
          }

          Log.LogInfo("[AP] " + itemName + " received - enabling matching NumberHoop colliders.");
        }
        else if (
          itemName == "weapon:WeaponMachineGun"
          || itemName == "weapon:WeaponFactorHammer"
          || itemName == "weapon:WeaponSword"
          || itemName == "weapon:WeaponMultiplyCone"
          || itemName == "weapon:WeaponRocketLauncher"
          || itemName == "weapon:throw"
        )
        {
          unlockedWeapons.Add(itemName);
          Log.LogInfo("[AP] " + itemName + " received - ");

          GameObject player = GameObject.FindWithTag("Player");
          if (player != null)
          {
            var pnc = player.GetComponent("PlayerNumberController");
            if (pnc != null)
            {
              var pncType = pnc.GetType();
              var weaponsField = pncType.GetField(
                "weapons",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
              );

              if (weaponsField != null)
              {
                var weaponsList = weaponsField.GetValue(pnc) as System.Collections.IList;
                if (weaponsList != null)
                {
                  List<int> restrictions = new List<int>();
                  if (!unlockedWeapons.Contains("weapon:throw"))
                    restrictions.Add(1);
                  if (!unlockedWeapons.Contains("weapon:WeaponMachineGun"))
                    restrictions.Add(2);
                  if (!unlockedWeapons.Contains("weapon:WeaponRocketLauncher"))
                    restrictions.Add(3);
                  if (!unlockedWeapons.Contains("weapon:WeaponSword"))
                    restrictions.Add(4);
                  if (!unlockedWeapons.Contains("weapon:WeaponMultiplyCone"))
                    restrictions.Add(5);
                  if (!unlockedWeapons.Contains("weapon:WeaponFactorHammer"))
                    restrictions.Add(6);
                  if (
                    !unlockedWeapons.Contains("weapon:MagnetWeapon")
                    && !unlockedWeapons.Contains("weapon:VacuumWeapon")
                  )
                  {
                    restrictions.Add(7);
                  }

                  var enableRestrictionsMethod = pncType.GetMethod(
                    "EnableWeaponRestrictions",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                  );
                  if (enableRestrictionsMethod != null)
                  {
                    enableRestrictionsMethod.Invoke(pnc, new object[] { restrictions });
                    Debug.Log("[MBMod] Updated weapon restrictions live for " + itemName);
                  }
                }
              }
            }
          }
        }
        else
        {
          Log.LogError(itemName + "itemName not used!!!");
        }
      };
      client.Connect();
    };

    harmony.PatchAll();
    Log.LogInfo("Mathbreakers Save Test loaded!");
  }

  private static void ModifyNumberPrefix(object[] __args)
  {
    // 1. Use the BepInEx logger to guarantee it shows up in the console
    Log.LogInfo("[asdasdasdasd: ModifyNumberPrefix triggered!");

    // 2. Safely check if we have the expected number of arguments
    if (__args == null || __args.Length < 2)
      return;

    // 3. Extract the arguments using the __args array
    // __args[0] is GameObject numberObject
    // __args[1] is Fraction newFrac
    var numberObject = __args[0] as UnityEngine.GameObject;
    var newFrac = __args[1];

    if (numberObject == null || newFrac == null)
      return;

    // Check if the numerator field of the Fraction object is 0 via reflection
    var numField = newFrac.GetType().GetField("numerator");
    if (numField != null && (int)numField.GetValue(newFrac) == 0)
    {
      var component = numberObject.GetComponent("NumberInfo");
      if (component != null)
      {
        var destroyField = component.GetType().GetField("destroyIfZero");
        bool destroyIfZero = destroyField != null && (bool)destroyField.GetValue(component);

        if (destroyIfZero)
        {
          var parent = numberObject.transform.parent;
          if (parent != null)
          {
            var wallCreator = parent.GetComponent("NumberWallCreator");
            if (wallCreator != null)
            {
              string wallId = GetWallUniqueId(wallCreator as UnityEngine.Component);
              Log.LogInfo("[MBMod] Zeroed-out brick belonged to wall ID: " + wallId);
            }
          }
        }
      }
    }
  }

  private static bool NumberWallCreatorPrefix(object __instance)
  {
    var mb = __instance as UnityEngine.MonoBehaviour;
    if (mb == null)
      return true;

    string wallUniqueId = GetWallUniqueId(mb);
    Debug.Log("[MBMod] Unique Wall ID: " + wallUniqueId);

    return true;
  }

  private static void NumberInfoOnDestroyNumberPrefix(UnityEngine.MonoBehaviour __instance)
  {
    if (__instance == null)
      return;

    var go = __instance.gameObject;
    if (go == null)
      return;

    string wallId = "UnknownWall";
    var curr = go.transform.parent;
    while (curr != null)
    {
      var wallCreator = curr.GetComponent("NumberWallCreator");
      if (wallCreator != null)
      {
        wallId = GetWallUniqueId(wallCreator as UnityEngine.Component);
        break;
      }
      curr = curr.parent;
    }

    Log.LogInfo("[MBMod] Block destroyed via NumberInfo.OnDestroyNumber! Wall ID: " + wallId);

    SendNewLocationCheck("level" + Application.loadedLevel + " - wall:" + wallId);
  }

  /// <summary>
  /// Port of the JS SendNewLocationCheck() location-check guard logic: looks the name up
  /// in slot_data.AP_LOCATION_IDS, skips it if already checked or unmapped,
  /// and sends the LocationChecks packet if the client is authenticated.
  /// </summary>
  internal static void SendNewLocationCheck(string name)
  {
    if (ApClient == null || ApClient.SlotData == null)
    {
      Log.LogError("[AP] SendNewLocationCheck: failed to check " + name);
      return;
    }

    object idsToken;
    if (!ApClient.SlotData.TryGetValue("AP_LOCATION_IDS", out idsToken))
    {
      Log.LogError(
        "[AP] SendNewLocationCheck: failed to check " + name + " (no AP_LOCATION_IDS in slot_data)"
      );
      return;
    }

    var idsMap = idsToken as Dictionary<string, object>;
    object idToken;
    int apLocationId;
    if (idsMap == null || !idsMap.TryGetValue(name, out idToken) || idToken == null)
    {
      Log.LogWarning("[Archipelago] Ignored location check (No ID mapped): \"" + name + "\"");
      return;
    }
    apLocationId = Convert.ToInt32(idToken);

    if (ApClient.CheckedLocations.Contains(apLocationId))
    {
      Log.LogWarning(
        "[Archipelago] Ignored location check (location already checked): \"" + name + "\""
      );
      return;
    }

    if (ApClient.IsAuthenticated)
    {
      Log.LogInfo("[Archipelago] Check registered: " + name + " (ID: " + apLocationId + ")");
      ApClient.AddToChecksInFlight(apLocationId);
      ApClient.SendLocationChecks(new[] { apLocationId });
    }
    else
    {
      Log.LogWarning("[Archipelago] failed to send - Client not authenticated.");
    }
  }

  private static string GetWallUniqueId(UnityEngine.Component mb)
  {
    if (mb == null)
      return "UnknownWall";

    string wallName = mb.gameObject.name;
    Vector3 pos = mb.transform.position;
    int currentLevel = Application.loadedLevel;

    string wallUniqueId = string.Format(
      "Level_{0}_{1}_pos_{2:F1}_{3:F1}_{4:F1}",
      currentLevel,
      wallName,
      pos.x,
      pos.y,
      pos.z
    );

    var type = mb.GetType();
    if (type.Name.Contains("Round"))
    {
      var radiusField = type.GetField("radius");
      var thicknessField = type.GetField("thickness");
      var degreesField = type.GetField("degreesToComplete");
      var heightField = type.GetField("height");

      int radius = radiusField != null ? (int)radiusField.GetValue(mb) : 0;
      int thickness = thicknessField != null ? (int)thicknessField.GetValue(mb) : 0;
      int degrees = degreesField != null ? (int)degreesField.GetValue(mb) : 360;
      int height = heightField != null ? (int)heightField.GetValue(mb) : 4;

      wallUniqueId += string.Format(
        "_rad_{0}_thick_{1}_deg_{2}_h_{3}",
        radius,
        thickness,
        degrees,
        height
      );
    }

    return wallUniqueId;
  }

  /*
   * Intercepts:
   *
   * PlayerPrefs.SetInt("unlockedLevel7", 1);
   *
   * We prevent the game from writing that key to PlayerPrefs
   * and put the unlock into our own in-memory save instead.
   */
  private static bool PlayerPrefsSetIntPrefix(string key, int value)
  {
    int level;

    Log.LogInfo(Json.Serialize(UnlockedLevels));
    Log.LogInfo(key);
    if (!TryGetLevelKey(key, out level))
    {
      // Not one of our keys.
      // Let Unity handle it normally.
      return true;
    }

    Log.LogInfo("[SAVE] Game requested unlock: " + key + " = " + value);

    // if (value != 0)
    // {
    //   if (level == 1)
    //     SendNewLocationCheck("menu - level:level" + level);
    //   else
    //     SendNewLocationCheck("level" + (level - 1) + " - level:level" + level);
    //   // UnlockedLevels.Add(level);

    //   Log.LogInfo("[SAVE] Level " + level + " unlocked in mod save.");
    // }
    // else
    // {
    //   Log.LogInfo("[SAVE] Level " + level + " locked in mod save.");
    // }

    // false = don't execute the original PlayerPrefs.SetInt().
    return true;
  }

  private static bool getintPrefix(string key, int defaultValue, ref int __result)
  {
    // Debug.Log("asasaaaa " + key + " ");
    // if (key == "WeaponVacuum")
    // {
    //   __result = 1;
    //   return false;
    // }
    return true;
  }

  /*
   * Intercepts:
   *
   * PlayerPrefs.HasKey("unlockedLevel7");
   *
   * The game's MenuButton_LevelSelect.CheckUnlocks()
   * will receive the result from our save instead.
   */
  private static bool PlayerPrefsHasKeyPrefix(string key, ref bool __result)
  {
    int level;

    Log.LogInfo("[SAVE] Game asked for " + key);
    if (!TryGetLevelKey(key, out level))
    {
      // Not one of our keys.
      return true;
    }

    bool unlocked = UnlockedLevels.Contains(level);

    Log.LogInfo("[SAVE] Game asked whether level " + level + " is unlocked -> " + unlocked);

    __result = unlocked;

    // false = don't execute the original PlayerPrefs.HasKey().
    return false;
  }

  /*
   * Turns:
   *
   * "unlockedLevel1"
   * "unlockedLevel2"
   * "unlockedLevel42"
   *
   * into the corresponding integer level.
   */
  private static bool TryGetLevelKey(string key, out int level)
  {
    const string prefix = "unlockedLevel";
    level = 0;
    if (key == null)
      return false;
    if (!key.StartsWith(prefix, StringComparison.Ordinal))
      return false;
    string number = key.Substring(prefix.Length);
    return int.TryParse(number, out level);
  }

  internal static Collider FindTriggerCollider(Component comp)
  {
    var col = comp.GetComponent<Collider>();
    if (col != null)
      return col;

    col = comp.GetComponentInChildren<Collider>();
    if (col == null)
    {
      Log.LogWarning(
        "[AP] No Collider found on or under '"
          + comp.gameObject.name
          + "' - can't gate this trigger area."
      );
    }
    return col;
  }
}

public static class NumberGenerator_StartPatch
{
  public static void Postfix(NumberGenerator __instance)
  {
    if (__instance != null)
    {
      // Use reflection to check if numbersAreMonsters is false
      var field = __instance
        .GetType()
        .GetField(
          "numbersAreMonsters",
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
      bool numbersAreMonsters = field != null && (bool)field.GetValue(__instance);

      if (!numbersAreMonsters)
      {
        __instance.fireDelay = 0.5f;
      }
    }
  }
}

/// <summary>
/// Holds AP-unlock flags that gate gameplay elements (e.g. whether the
/// positive/negative light-area trigger colliders are currently active).
/// Set these from your ArchipelagoClient.OnGiveItem handler as the
/// matching items come in.
/// </summary>
public static class ApState
{
  public static bool LightAreaUnlocked = false;

  // One flag per HoopType, gating NumberHoop's trigger collider the
  // same way LightAreaUnlocked gates PositiveNegativeLightArea's.
  public static bool HoopMultiplyUnlocked = false;
  public static bool HoopAddUnlocked = false;
  public static bool HoopExponentUnlocked = false;

  public static bool IsHoopTypeUnlocked(HoopType ht)
  {
    switch (ht)
    {
      case HoopType.Multiply:
        return HoopMultiplyUnlocked;
      case HoopType.Add:
        return HoopAddUnlocked;
      case HoopType.Exponent:
        return HoopExponentUnlocked;
      default:
        return false;
    }
  }
}

public static class PositiveNegativeLightArea_StartPatch
{
  public static void Postfix(PositiveNegativeLightArea __instance)
  {
    var col = MBMod.FindTriggerCollider(__instance);
    if (col != null)
    {
      col.enabled = ApState.LightAreaUnlocked;
    }
  }
}

public static class NumberHoop_StartPatch
{
  public static void Postfix(NumberHoop __instance)
  {
    var col = MBMod.FindTriggerCollider(__instance);
    if (col != null)
    {
      col.enabled = ApState.IsHoopTypeUnlocked(__instance.ht);
    }
  }
}

[HarmonyPatch(typeof(PlayerNumberController), "GiveAllWeaponsCheat")]
public static class PlayerNumberController_GiveAllWeaponsCheat_Patch
{
  public static void Postfix(object __instance)
  {
    if (__instance == null)
      return;

    // Use reflection to invoke GiveWeapon(new SnowballWeapon(), 8) if the method is private/internal
    var method = __instance
      .GetType()
      .GetMethod(
        "GiveWeapon",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
      );
    if (method != null)
    {
      var snowballWeapon = Activator.CreateInstance(AccessTools.TypeByName("SnowballWeapon"));
      method.Invoke(__instance, new object[] { snowballWeapon, 8 });
      Debug.Log("[MBMod] Added SnowballWeapon to GiveAllWeaponsCheat via Harmony Postfix.");
    }
  }
}

/// <summary>
/// Gates weapon pickups behind MBMod.unlockedWeapons. If the pickup's
/// weapon hasn't been unlocked (e.g. via Archipelago item receipt),
/// PlayerHitObject is skipped entirely so the weapon is not given
/// and the pickup object is left untouched.
/// </summary>
[HarmonyPatch(typeof(PlayerNumberController), "PlayerHitObject")]
public static class PlayerNumberController_PlayerHitObject_Patch
{
  public static void Postfix(object __instance, GameObject hitObj)
  {
    if (__instance == null || hitObj == null || hitObj.name == "Combined")
      return;
    string weaponKey = null;

    if (
      hitObj.name == "MachineGun"
      || hitObj.name == "FactorHammer"
      || hitObj.name == "Sword"
      || hitObj.name == "MultiplyCone"
      || hitObj.name == "RocketLauncher"
    )
    {
      weaponKey = "weapon:Weapon" + hitObj.name;
      MBMod.SendNewLocationCheck(
        "level" + Application.loadedLevel + " - weaponCheck:Weapon" + hitObj.name
      );
    }
    else if (
      hitObj.name == "WeaponMachineGun"
      || hitObj.name == "WeaponFactorHammer"
      || hitObj.name == "WeaponSword"
      || hitObj.name == "WeaponMultiplyCone"
      || hitObj.name == "WeaponRocketLauncher"
    )
    {
      weaponKey = "weapon:" + hitObj.name;
      MBMod.SendNewLocationCheck(
        "level" + Application.loadedLevel + " - weaponCheck:" + hitObj.name
      );
    }
    else if (
      hitObj.name == "WaveWeapon"
      || hitObj.name == "MultiplyWaveWeapon"
      || hitObj.name == "MultiplyWave"
      || hitObj.name == "MultiplyWeapon"
    )
    {
      weaponKey = "weapon:WeaponMultiplyCone";
      MBMod.SendNewLocationCheck(
        "level" + Application.loadedLevel + " - weaponCheck:WeaponMultiplyCone"
      );
    }
  }
}

[HarmonyPatch(typeof(Powerup_Transcendental), "OnPlayerTouch")]
public static class Powerup_Transcendental_Patch
{
  public static void Postfix(Powerup_Transcendental __instance)
  {
    if (__instance == null)
      return;

    Debug.Log("[MBMod] Powerup_Transcendental collected - pi!");
    MBMod.SendNewLocationCheck("level" + Application.loadedLevel + " - pi:pi");
  }
}

[HarmonyPatch(typeof(MenuButton_Screen), "MouseDown")]
public static class MenuButton_Screen_Patch
{
  public static bool Prefix(MenuButton_Screen __instance)
  {
    // Only allow the menu screen action if the Archipelago client is connected and authenticated
    if (MBMod.ApClient == null || !MBMod.ApClient.IsAuthenticated)
    {
      Debug.Log("[MBMod] MenuButton_Screen blocked - Archipelago client not connected.");
      return false; // Skip original MouseDown execution
    }

    return true; // Proceed normally if connected
  }
}

[HarmonyPatch(typeof(PlayerNumberController), "Start")]
public static class PlayerNumberController_Start_Patch
{
  public static void Postfix(object __instance)
  {
    if (__instance == null)
      return;

    var type = __instance.GetType();
    var weaponsField = type.GetField(
      "weapons",
      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    if (weaponsField == null)
      return;

    var weaponsList = weaponsField.GetValue(__instance) as System.Collections.IList;
    if (weaponsList == null)
      return;

    List<int> restrictions = new List<int>();
    if (!MBMod.unlockedWeapons.Contains("weapon:throw"))
    {
      restrictions.Add(1);
    }
    if (!MBMod.unlockedWeapons.Contains("weapon:WeaponMachineGun"))
    {
      restrictions.Add(2);
    }
    if (!MBMod.unlockedWeapons.Contains("weapon:WeaponRocketLauncher"))
    {
      restrictions.Add(3);
    }
    if (!MBMod.unlockedWeapons.Contains("weapon:WeaponSword"))
    {
      restrictions.Add(4);
    }
    if (!MBMod.unlockedWeapons.Contains("weapon:WeaponMultiplyCone"))
    {
      restrictions.Add(5);
    }
    if (!MBMod.unlockedWeapons.Contains("weapon:WeaponFactorHammer"))
    {
      restrictions.Add(6);
    }
    if (!MBMod.unlockedWeapons.Contains("weapon:MagnetWeapon"))
    {
      if (!MBMod.unlockedWeapons.Contains("weapon:VacuumWeapon"))
      {
        restrictions.Add(7);
      }
    }
    // if (!MBMod.unlockedWeapons.Contains("weapon:SnowballWeapon")){
    //   restrictions.Add(8)
    // }
    var enableRestrictionsMethod = type.GetMethod(
      "EnableWeaponRestrictions",
      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    );
    if (enableRestrictionsMethod != null)
    {
      enableRestrictionsMethod.Invoke(__instance, new object[] { restrictions });
      Debug.Log("[MBMod] Called EnableWeaponRestrictions for ununlocked ThrowWeapon.");
    }
    if (weaponsList.Count == 1)
    {
      var snowballWeaponType = AccessTools.TypeByName("SnowballWeapon");
      if (snowballWeaponType != null)
      {
        var snowballWeaponInstance = Activator.CreateInstance(snowballWeaponType);
        weaponsList.Add(snowballWeaponInstance);
        Debug.Log("[MBMod] No weapons remaining; added SnowballWeapon as fallback.");

        // Call SelectWeapon on the current instance as before
        var selectWeaponMethod = type.GetMethod(
          "SelectWeapon",
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        );
        if (selectWeaponMethod != null)
        {
          selectWeaponMethod.Invoke(__instance, new object[] { 0 });
        }

        // Call GlobalVars.pnc.SelectWeapon(8) directly
        if (GlobalVars.pnc != null)
        {
          GlobalVars.pnc.SelectWeapon(8);
          Debug.Log("[MBMod] Called GlobalVars.pnc.SelectWeapon(8).");
        }
      }
    }
  }
}

[HarmonyPatch(typeof(FPSWalkerEnhanced), "Kill")]
public static class FPSWalkerEnhanced_Kill_Patch
{
  public static void Prefix(FPSWalkerEnhanced __instance)
  {
    if (__instance != null)
    {
      // Instantly zero out the dieTimer so the respawn happens right away
      __instance.dieTimer = 0f;
      Debug.Log("[MBMod] Instant respawn triggered via Harmony patch.");
    }
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
        MBMod.SendNewLocationCheck("level" + currentLevel + " - level:level" + nextLevel);
      }
    }
  }
}
