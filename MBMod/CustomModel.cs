using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

// Loads the custom skinned model (player.skin, from export_model.py) and its animations
// (player.anim, from export_anim.py), and spawns independent copies of it: the player and the ghost.
// The model has its own skeleton; nothing from the game's original player rig is used.
public static class CustomModel
{
  public static bool enabled;

  // Model height relative to the player's CharacterController height
  const float heightScale = 1f;

  // Extra degrees about Y if the model ends up facing the wrong way
  const float yaw = 0f;

  // Shifts the model in the player's local axes (x = right, y = up, z = forward). Negative y is down.
  static readonly Vector3 offset = new Vector3(0f, -0.2f, 6.3f);

  // Every animation CustomPlayerAnim can pick must exist in player.anim
  static readonly string[] requiredClips = { "idle", "walk", "jump", "fly", "fall" };

  const string modelName = "CustomPlayerModel";

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

  struct Placement
  {
    public Vector3 position;
    public float scale;
  }

  // "model": the file is already in Unity axes (x=side, y=up, z=forward).
  // "blender": raw Blender armature axes, converted on load.
  static bool blenderSpace;
  static string[] boneNames;
  static int[] boneParent; // -1 = child of the model root
  static Vector3[] bonePos; // rest positions in model space
  static Mesh mesh;
  static Texture2D tex;
  static AnimationClip[] clips;

  static bool havePlacement;
  static Placement placement;

  public static void Load(string skinPath, string animPath, string texPath)
  {
    LoadSkin(skinPath);
    List<AnimationClip> built = new List<AnimationClip>();
    foreach (ClipDef def in ParseAnimFile(animPath))
      built.Add(BuildClip(animPath, def));
    clips = built.ToArray();
    foreach (string name in requiredClips)
    {
      if (!built.Exists(c => c.name == name))
        throw new Exception(animPath + " has no clip named '" + name + "'");
    }

    tex = new Texture2D(2, 2);
    tex.hideFlags = HideFlags.HideAndDontSave;
    tex.LoadImage(File.ReadAllBytes(texPath));
    enabled = true;
  }

  // ---------------------------------------------------------------- skin file

  // space <model|blender>
  // bone<TAB><name><TAB><parent|-><TAB><x><TAB><y><TAB><z>     (tabs so names can contain spaces)
  // v <x> <y> <z> <nx> <ny> <nz> <u> <v> <bone0> <w0> <bone1> <w1> <bone2> <w2> <bone3> <w3>
  // t <a> <b> <c>
  static void LoadSkin(string path)
  {
    CultureInfo ci = CultureInfo.InvariantCulture;
    List<string> names = new List<string>();
    List<string> parentNames = new List<string>();
    List<Vector3> pos = new List<Vector3>();
    List<Vector3> verts = new List<Vector3>();
    List<Vector3> normals = new List<Vector3>();
    List<Vector2> uvs = new List<Vector2>();
    List<BoneWeight> weights = new List<BoneWeight>();
    List<int> tris = new List<int>();
    string space = null;

    string[] lines = File.ReadAllLines(path);
    for (int n = 0; n < lines.Length; n++)
    {
      string line = lines[n].Trim();
      if (line.Length == 0 || line[0] == '#')
        continue;
      string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
      try
      {
        if (p[0] == "space")
        {
          if (p.Length != 2 || (p[1] != "model" && p[1] != "blender"))
            throw new Exception("expected 'space <model|blender>'");
          space = p[1];
          blenderSpace = p[1] == "blender";
        }
        else if (p[0] == "bone")
        {
          string[] t = line.Split('\t');
          if (t.Length != 6)
            throw new Exception("expected tab separated 'bone <name> <parent|-> <x> <y> <z>'");
          names.Add(t[1]);
          parentNames.Add(t[2]);
          pos.Add(ToUnity(new Vector3(F(t[3], ci), F(t[4], ci), F(t[5], ci))));
        }
        else if (p[0] == "v")
        {
          if (p.Length != 17)
            throw new Exception("expected 16 values after 'v'");
          verts.Add(ToUnity(new Vector3(F(p[1], ci), F(p[2], ci), F(p[3], ci))));
          normals.Add(ToUnity(new Vector3(F(p[4], ci), F(p[5], ci), F(p[6], ci))));
          uvs.Add(new Vector2(F(p[7], ci), F(p[8], ci)));
          BoneWeight w = new BoneWeight();
          w.boneIndex0 = int.Parse(p[9], ci);
          w.weight0 = F(p[10], ci);
          w.boneIndex1 = int.Parse(p[11], ci);
          w.weight1 = F(p[12], ci);
          w.boneIndex2 = int.Parse(p[13], ci);
          w.weight2 = F(p[14], ci);
          w.boneIndex3 = int.Parse(p[15], ci);
          w.weight3 = F(p[16], ci);
          weights.Add(w);
        }
        else if (p[0] == "t")
        {
          if (p.Length != 4)
            throw new Exception("expected 't <a> <b> <c>'");
          int a = int.Parse(p[1], ci);
          int b = int.Parse(p[2], ci);
          int c = int.Parse(p[3], ci);
          // Both the model-space and blender-space mappings to Unity mirror the mesh, so reverse the winding
          tris.Add(a);
          tris.Add(c);
          tris.Add(b);
        }
        else
        {
          throw new Exception("unknown line type '" + p[0] + "'");
        }
      }
      catch (Exception e)
      {
        throw new Exception(path + ":" + (n + 1) + ": " + e.Message, e);
      }
    }
    if (space == null)
      throw new Exception(path + ": missing 'space' line");

    boneNames = names.ToArray();
    bonePos = pos.ToArray();
    boneParent = new int[boneNames.Length];
    for (int i = 0; i < boneNames.Length; i++)
    {
      if (parentNames[i] == "-")
      {
        boneParent[i] = -1;
        continue;
      }
      boneParent[i] = names.IndexOf(parentNames[i]);
      if (boneParent[i] < 0)
        throw new Exception(
          path + ": bone '" + names[i] + "' has unknown parent '" + parentNames[i] + "'"
        );
    }
    foreach (BoneWeight w in weights)
    {
      if (
        w.boneIndex0 >= boneNames.Length
        || w.boneIndex1 >= boneNames.Length
        || w.boneIndex2 >= boneNames.Length
        || w.boneIndex3 >= boneNames.Length
      )
        throw new Exception(path + ": a vertex references a bone index past the last bone");
    }

    // Bones are translation-only at rest, so the bind pose is just the inverse translation
    Matrix4x4[] bind = new Matrix4x4[boneNames.Length];
    for (int i = 0; i < bind.Length; i++)
      bind[i] = Matrix4x4.TRS(-bonePos[i], Quaternion.identity, Vector3.one);

    mesh = new Mesh();
    mesh.hideFlags = HideFlags.HideAndDontSave;
    mesh.vertices = verts.ToArray();
    mesh.normals = normals.ToArray();
    mesh.uv = uvs.ToArray();
    mesh.boneWeights = weights.ToArray();
    mesh.bindposes = bind;
    mesh.triangles = tris.ToArray();
    mesh.RecalculateBounds();
  }

  static float F(string s, CultureInfo ci)
  {
    return float.Parse(s, ci);
  }

  // Blender armature axes (x right, y back, z up) -> Unity axes (x right, y up, z forward)
  static Vector3 ToUnity(Vector3 v)
  {
    return blenderSpace ? new Vector3(-v.x, v.z, -v.y) : v;
  }

  // ---------------------------------------------------------------- animation file

  // clip <name> <loop|once>
  // bone <name>                      (clip and bone names may contain spaces)
  // <time> <pitch> <yaw> <roll>      (degrees about the model's side / up / forward axes)
  static List<ClipDef> ParseAnimFile(string path)
  {
    CultureInfo ci = CultureInfo.InvariantCulture;
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
          string mode = p[p.Length - 1];
          if (p.Length < 3 || (mode != "loop" && mode != "once"))
            throw new Exception("expected 'clip <name> <loop|once>'");
          string clipName = line.Substring(4, line.Length - 4 - mode.Length).Trim();
          clip = new ClipDef { name = clipName, loop = mode == "loop" };
          defs.Add(clip);
        }
        else if (p[0] == "bone")
        {
          if (p.Length < 2)
            throw new Exception("expected 'bone <name>'");
          keys = new List<Key>();
          clip.bones.Add(line.Substring(4).Trim(), keys);
        }
        else
        {
          if (p.Length != 4)
            throw new Exception("expected '<time> <pitch> <yaw> <roll>'");
          keys.Add(
            new Key
            {
              time = F(p[0], ci),
              pitch = F(p[1], ci),
              yaw = F(p[2], ci),
              roll = F(p[3], ci),
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

  // Bones rest with identity rotation, so each key is directly the bone's localRotation.
  // On Unity 5 or later, set clip.legacy = true here or the Animation component won't play it.
  static AnimationClip BuildClip(string animPath, ClipDef def)
  {
    AnimationClip clip = new AnimationClip();
    clip.name = def.name;
    clip.wrapMode = def.loop ? WrapMode.Loop : WrapMode.ClampForever;

    foreach (KeyValuePair<string, List<Key>> kv in def.bones)
    {
      int bone = Array.IndexOf(boneNames, kv.Key);
      if (bone < 0)
        throw new Exception(
          animPath
            + ": clip '"
            + def.name
            + "' animates bone '"
            + kv.Key
            + "' which is not in the skin file"
        );

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
        // roll on top of pitch on top of yaw (matches the exporter's 'YXZ' Euler order)
        Quaternion q =
          Quaternion.AngleAxis(k.roll, Vector3.forward)
          * Quaternion.AngleAxis(k.pitch, Vector3.right)
          * Quaternion.AngleAxis(k.yaw, Vector3.up);
        // same mirroring as ToUnity, applied to a rotation
        if (blenderSpace)
          q = new Quaternion(q.x, -q.z, q.y, q.w);

        // keep the quaternion in the same hemisphere as the previous key so it doesn't spin the long way
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

      string path = BonePath(bone);
      clip.SetCurve(path, typeof(Transform), "localRotation.x", c[0]);
      clip.SetCurve(path, typeof(Transform), "localRotation.y", c[1]);
      clip.SetCurve(path, typeof(Transform), "localRotation.z", c[2]);
      clip.SetCurve(path, typeof(Transform), "localRotation.w", c[3]);
    }
    return clip;
  }

  // Path of a bone relative to the model root
  static string BonePath(int bone)
  {
    string path = boneNames[bone];
    for (int p = boneParent[bone]; p >= 0; p = boneParent[p])
      path = boneNames[p] + "/" + path;
    return path;
  }

  // ---------------------------------------------------------------- spawning

  // Builds the model on the player, sized and placed to fit its CharacterController, and hides the
  // game's original player mesh.
  public static CustomPlayerAnim SpawnPlayer(GameObject player)
  {
    Transform old = player.transform.Find(modelName);
    if (old != null)
      UnityEngine.Object.Destroy(old.gameObject);
    foreach (SkinnedMeshRenderer smr in player.GetComponentsInChildren<SkinnedMeshRenderer>())
      smr.enabled = false;

    CharacterController cc = player.GetComponent<CharacterController>();
    Bounds capsule = cc.bounds;
    float worldScale = capsule.size.y * heightScale / mesh.bounds.size.y;
    Transform t = player.transform;
    placement.scale = worldScale / t.lossyScale.y;
    // model origin is between its feet: put its lowest point at the bottom of the capsule
    Vector3 feet = t.InverseTransformPoint(
      new Vector3(capsule.center.x, capsule.min.y, capsule.center.z)
    );
    placement.position = feet - new Vector3(0f, mesh.bounds.min.y, 0f) * placement.scale + offset;
    havePlacement = true;

    GameObject root = Build(t);
    CustomPlayerAnim driver = root.AddComponent<CustomPlayerAnim>();
    driver.InitPlayer(root.GetComponent<Animation>(), cc);
    return driver;
  }

  // Builds the model under ghostRoot (which should sit at the player's position/rotation/scale).
  // SpawnPlayer must have run first so the ghost gets the same fit.
  public static CustomPlayerAnim SpawnGhost(Transform ghostRoot)
  {
    if (!havePlacement)
      throw new Exception("SpawnGhost called before SpawnPlayer");
    GameObject root = Build(ghostRoot);
    CustomPlayerAnim driver = root.AddComponent<CustomPlayerAnim>();
    driver.InitGhost(root.GetComponent<Animation>());
    return driver;
  }

  static GameObject Build(Transform parent)
  {
    GameObject root = new GameObject(modelName);
    root.transform.parent = parent;
    root.transform.localPosition = placement.position;
    root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    root.transform.localScale = Vector3.one * placement.scale;

    Transform[] bt = new Transform[boneNames.Length];
    for (int i = 0; i < bt.Length; i++)
      bt[i] = new GameObject(boneNames[i]).transform;
    for (int i = 0; i < bt.Length; i++)
    {
      int p = boneParent[i];
      bt[i].parent = p < 0 ? root.transform : bt[p];
      bt[i].localPosition = p < 0 ? bonePos[i] : bonePos[i] - bonePos[p];
      bt[i].localRotation = Quaternion.identity;
      bt[i].localScale = Vector3.one;
    }

    Material mat = new Material(Shader.Find("Diffuse"));
    mat.mainTexture = tex;
    mat.color = Color.white;

    SkinnedMeshRenderer smr = root.AddComponent<SkinnedMeshRenderer>();
    smr.sharedMesh = mesh;
    smr.bones = bt;
    smr.quality = SkinQuality.Bone4;
    smr.updateWhenOffscreen = true;
    smr.sharedMaterial = mat;

    Animation anim = root.AddComponent<Animation>();
    anim.cullingType = AnimationCullingType.AlwaysAnimate;
    foreach (AnimationClip clip in clips)
      anim.AddClip(clip, clip.name);
    return root;
  }
}

// Picks which clip to play from the player's movement.
public class CustomPlayerAnim : MonoBehaviour
{
  enum PlayerState
  {
    Idle,
    Walk,
    Jump,
    Fall,
  }

  // Tune these to your game's units
  const float walkSpeed = 2f; // horizontal speed above this counts as walking
  const float jumpVel = 1f; // upward speed above this counts as jumping
  const float fallVel = -2f; // downward speed below this counts as falling
  const float walkCyclesPerUnit = 0.5f; // walk clip cycles per unit of distance traveled
  const float fadeTime = 0.1f; // crossfade between animations

  // Driving mode for Replay/Ghost
  public bool isReplayGhost = false;
  public Vector3 currentGhostVelocity;

  Animation anim;
  CharacterController controller; // null for the ghost
  float vy;
  string lastAnimation;

  public void InitPlayer(Animation a, CharacterController c)
  {
    anim = a;
    controller = c;
  }

  public void InitGhost(Animation a)
  {
    anim = a;
    isReplayGhost = true;
  }

  static PlayerState GetState(float hSpeed, float vy)
  {
    if (vy > jumpVel)
      return PlayerState.Jump;
    if (vy < fallVel)
      return PlayerState.Fall;
    if (hSpeed > walkSpeed)
      return PlayerState.Walk;
    return PlayerState.Idle;
  }

  // lastAnimation is the clip picked on the previous frame (null on the first frame)
  static string ChooseAnimation(PlayerState playerState, string lastAnimation)
  {
    switch (playerState)
    {
      case PlayerState.Jump:
        switch (lastAnimation)
        {
          case "jump":
          case "fly":
            return "fly";
          default:
            return "jump";
        }
      case PlayerState.Walk:
        return "walk";
      case PlayerState.Fall:
        return "fall";
      case PlayerState.Idle:
        return "idle";
    }
    throw new Exception("unhandled player state " + playerState);
  }

  void LateUpdate()
  {
    float dt = Time.deltaTime;
    if (dt <= 0f)
      return;

    Vector3 vel = isReplayGhost ? currentGhostVelocity : controller.velocity;
    float hSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
    vy = Mathf.Lerp(vy, vel.y, 1f - Mathf.Exp(-10f * dt));

    string next = ChooseAnimation(GetState(hSpeed, vy), lastAnimation);
    if (next != lastAnimation)
    {
      anim.CrossFade(next, fadeTime);
      lastAnimation = next;
    }

    // walk clip is one stride cycle: play it faster the faster we move
    if (lastAnimation == "walk")
      anim["walk"].speed = hSpeed * walkCyclesPerUnit * anim["walk"].length;
  }
}
