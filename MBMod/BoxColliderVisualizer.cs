using System.Collections.Generic;
using UnityEngine;

// Attach ONE instance of this to the main Camera. It draws every
// registered BoxCollider each frame. Register colliders via Register().
public class BoxColliderVisualizer : MonoBehaviour
{
  private static readonly List<BoxCollider> targets = new List<BoxCollider>();
  private static readonly List<Color> targetColors = new List<Color>();

  private static Material lineMaterial;

  // Reused every frame instead of allocated per-draw.
  private static readonly Vector3[] p = new Vector3[8];

  public static void Register(BoxCollider box, Color color)
  {
    if (box == null)
    {
      Debug.LogError("[BoxColliderVisualizer] Register called with a null BoxCollider.");
      return;
    }
    if (targets.Contains(box))
      return;
    targets.Add(box);
    targetColors.Add(color);
  }

  public static void Unregister(BoxCollider box)
  {
    int idx = targets.IndexOf(box);
    if (idx < 0)
      return;
    targets.RemoveAt(idx);
    targetColors.RemoveAt(idx);
  }

  private void OnRenderObject()
  {
    if (targets.Count == 0)
      return;

    CreateLineMaterial();
    if (lineMaterial == null)
      return; // logged in CreateLineMaterial
    lineMaterial.SetPass(0);

    for (int t = targets.Count - 1; t >= 0; t--)
    {
      BoxCollider box = targets[t];
      if (box == null)
      {
        // Object was destroyed without unregistering; drop it here
        // instead of pretending everything is fine.
        targets.RemoveAt(t);
        targetColors.RemoveAt(t);
        continue;
      }

      DrawBox(box, targetColors[t]);
    }
  }

  // Each face as two triangles, referencing the 8 corner indices in p[].
  private static readonly int[] faces = new int[36]
  {
    0,
    1,
    2,
    0,
    2,
    3, // bottom
    4,
    6,
    5,
    4,
    7,
    6, // top
    0,
    4,
    5,
    0,
    5,
    1, // front (-z)
    3,
    2,
    6,
    3,
    6,
    7, // back (+z)
    0,
    3,
    7,
    0,
    7,
    4, // left (-x)
    1,
    5,
    6,
    1,
    6,
    2, // right (+x)
  };

  private static void DrawBox(BoxCollider box, Color fillColor)
  {
    Transform tr = box.transform;

    GL.PushMatrix();
    GL.MultMatrix(tr.localToWorldMatrix);
    GL.Begin(GL.TRIANGLES);
    GL.Color(fillColor);

    Vector3 c = box.center;
    Vector3 e = box.size * 0.5f;

    p[0] = c + new Vector3(-e.x, -e.y, -e.z);
    p[1] = c + new Vector3(e.x, -e.y, -e.z);
    p[2] = c + new Vector3(e.x, -e.y, e.z);
    p[3] = c + new Vector3(-e.x, -e.y, e.z);
    p[4] = c + new Vector3(-e.x, e.y, -e.z);
    p[5] = c + new Vector3(e.x, e.y, -e.z);
    p[6] = c + new Vector3(e.x, e.y, e.z);
    p[7] = c + new Vector3(-e.x, e.y, e.z);

    for (int i = 0; i < faces.Length; i++)
    {
      GL.Vertex(p[faces[i]]);
    }

    GL.End();
    GL.PopMatrix();
  }

  // Runtime shader compilation isn't available outside the editor, so we
  // can't synthesize a shader on the fly in a build. Instead, probe a
  // handful of shader names that are commonly still present, and fail
  // loudly (once) if none of them made it into this stripped build.
  private static readonly string[] CandidateShaderNames =
  {
    "Hidden/Internal-Colored",
    "Sprites/Default",
    "GUI/Text Shader",
    "Legacy Shaders/VertexLit",
    "Unlit/Color",
    "Particles/Alpha Blended",
  };

  private static bool loggedShaderFailure;

  private static void CreateLineMaterial()
  {
    if (lineMaterial != null)
      return;

    Shader shader = null;
    foreach (string name in CandidateShaderNames)
    {
      shader = Shader.Find(name);
      if (shader != null)
        break;
    }

    if (shader == null)
    {
      if (!loggedShaderFailure)
      {
        Debug.LogError(
          "[BoxColliderVisualizer] None of the candidate shaders exist in this build: "
            + string.Join(", ", CandidateShaderNames)
            + ". Box drawing disabled until a valid shader name is added."
        );
        loggedShaderFailure = true;
      }
      return;
    }

    lineMaterial = new Material(shader);
  }
}
