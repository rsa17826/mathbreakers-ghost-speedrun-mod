using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shows a BoxCollider as a real semi-transparent cube in the scene (same
/// look as the death markers) instead of drawing it with GL from the camera.
/// </summary>
public static class HitboxMarkers
{
  private const string TransparentShaderName = "Transparent/Diffuse";

  public static bool isEnabled = false;
  private static readonly List<Renderer> renderers = new List<Renderer>();

  public static void Toggle()
  {
    isEnabled = !isEnabled;
    // Markers are destroyed with their hoop/level, so drop the dead ones.
    renderers.RemoveAll(r => r == null);
    foreach (var r in renderers)
    {
      r.enabled = isEnabled;
    }
    MBMod.Log.LogInfo("[HitboxMarkers] Hitboxes shown: " + isEnabled);
  }

  public static void Create(BoxCollider box, Color color)
  {
    Shader transparentShader = Shader.Find(TransparentShaderName);
    if (transparentShader == null)
    {
      MBMod.Log.LogError(
        "[HitboxMarkers] Shader '"
          + TransparentShaderName
          + "' not found in this build; hitbox not shown."
      );
      return;
    }

    var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
    marker.name = "HitboxMarker";

    // Parent to the collider's object so it inherits position/rotation/scale
    // exactly like the BoxCollider does, and is destroyed along with it.
    // (Old Unity has no SetParent; the local values below are set after
    // parenting, so they override whatever the parent assignment preserved.)
    // A unit cube scaled by box.size in that local space matches the collider.
    marker.transform.parent = box.transform;
    marker.transform.localPosition = box.center;
    marker.transform.localRotation = Quaternion.identity;
    marker.transform.localScale = box.size;

    // Purely visual: must not collide with or trigger anything.
    Object.Destroy(marker.GetComponent<Collider>());

    Renderer renderer = marker.GetComponent<Renderer>();
    renderer.enabled = isEnabled;
    renderers.Add(renderer);

    Material mat = renderer.material;
    mat.shader = transparentShader;
    mat.color = color;
  }
}
