using UnityEngine;

/// <summary>
/// Displays the player's current world position and distances to specific points.
/// </summary>
public class PlayerCoordsUI : MonoBehaviour
{
  private Transform playerTransform;

  // Expanded window height to fit player pos, two distances, and the best-run delta
  private Rect _windowRect = new Rect(20, 260, 240, 155);
  private const int FontScale = 2;
  private static readonly Color TextColor = Color.white;
  private static readonly Color AheadColor = Color.green;
  private static readonly Color BehindColor = Color.red;
  private Texture2D _bgTexture;

  /// <summary>
  /// Optional manual hook if you want to explicitly start/end tracking from
  /// this component instead of relying on Class1.cs's WASD/level-clear
  /// detection. Not required for normal use.
  /// </summary>
  public void OnRunStarted(int level)
  {
    PathComparer.StartRun(level);
  }

  public void OnRunEnded(bool completed)
  {
    PathComparer.EndRun(completed);
  }

  // Define the two target points
  private readonly Vector3 point1 = new Vector3(47.3f, 70.1f, 641.7f);
  private readonly Vector3 point2 = new Vector3(149.7f, 18.4f, 906.0f);

  private void Start()
  {
    _bgTexture = new Texture2D(1, 1);
    _bgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
    _bgTexture.Apply();
  }

  private void Update()
  {
    if (playerTransform == null)
      return;
    PathComparer.Tick(playerTransform.position);
    UpdateGhost();
  }

  private GameObject ghost;

  private void UpdateGhost()
  {
    if (ghost == null && PathComparer.HasComparison)
    {
      ghost = CreateGhost(playerTransform.gameObject);
    }
    if (!PathComparer.IsRunning)
    {
      return;
    }

    if (ghost != null)
    {
      Vector3 pos;
      if (PathComparer.TryGetBestPosition(PathComparer.ElapsedTime, out pos))
      {
        ghost.transform.position = pos;
      }
    }
  }

  // Clones the player's visuals to trail the best-run path. Strips every
  // script/collider/camera/audio-listener so the clone can't act like a
  // second player, control input, or collide with anything - it's purely
  // a visual marker.
  private GameObject CreateGhost(GameObject player)
  {
    var clone = (GameObject)Instantiate(
      player,
      player.transform.position,
      player.transform.rotation
    );
    clone.name = "BestRunGhost";
    clone.tag = "Untagged";

    foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>())
    {
      Destroy(behaviour);
    }
    foreach (var collider in clone.GetComponentsInChildren<Collider>())
    {
      Destroy(collider);
    }
    foreach (var rb in clone.GetComponentsInChildren<Rigidbody>())
    {
      Destroy(rb);
    }
    foreach (var cam in clone.GetComponentsInChildren<Camera>())
    {
      Destroy(cam);
    }
    foreach (var listener in clone.GetComponentsInChildren<AudioListener>())
    {
      Destroy(listener);
    }

    MakeTransparent(clone, 0.35f);
    return clone;
  }

  private static void MakeTransparent(GameObject go, float alpha)
  {
    var transparentShader = Shader.Find("Transparent/Diffuse");
    foreach (var renderer in go.GetComponentsInChildren<Renderer>())
    {
      foreach (var mat in renderer.materials)
      {
        if (transparentShader != null)
        {
          mat.shader = transparentShader;
        }
        Color c = mat.color;
        c.a = alpha;
        mat.color = c;
      }
    }
  }

  private void OnGUI()
  {
    if (playerTransform == null)
    {
      GameObject player = GameObject.FindWithTag("Player");
      if (player != null)
      {
        playerTransform = player.transform;
      }
      else if (PathComparer.IsRunning)
      {
        // Log.LogInfo(
        //   "[PathComparer] no player detected, stopping run on level " + Application.loadedLevel
        // );
        PathComparer.EndRun(false);
      }
    }

    GUI.DrawTexture(_windowRect, _bgTexture, ScaleMode.StretchToFill);

    DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 8, 220, 18), "Player Position");

    if (playerTransform != null)
    {
      Vector3 pos = playerTransform.position;
      string coordText = string.Format("X: {0:F1}  Y: {1:F1}  Z: {2:F1}", pos.x, pos.y, pos.z);
      DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 32, 220, 20), coordText);

      if (PathComparer.HasComparison)
      {
        float delta = PathComparer.DeltaSeconds;
        string sign = delta >= 0 ? "+" : "";
        string deltaText = string.Format("Best Run: {0}{1:F2}s", sign, delta);
        Color deltaColor = delta <= 0 ? AheadColor : BehindColor;
        DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 56, 220, 20), deltaText, deltaColor);
      }
    }
    else
    {
      DrawText(
        new Rect(_windowRect.x + 10, _windowRect.y + 32, 220, 20),
        "Searching for player..."
      );
    }
  }

  private void DrawText(Rect rect, string text)
  {
    DrawText(rect, text, TextColor);
  }

  private void DrawText(Rect rect, string text, Color color)
  {
    var tex = TextRasterizer.GetTexture(text, FontScale, color);
    var drawRect = new Rect(rect.x, rect.y, Mathf.Min(tex.width, rect.width), rect.height);
    GUI.DrawTexture(drawRect, tex, ScaleMode.ScaleToFit);
  }
}
