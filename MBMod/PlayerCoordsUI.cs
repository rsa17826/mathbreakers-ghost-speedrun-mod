using UnityEngine;

/// <summary>
/// Displays the player's current world position and distances to specific points.
/// </summary>
public class PlayerCoordsUI : MonoBehaviour
{
  private Transform playerTransform;
  private bool gotEgg1 = false;
  private bool gotEgg2 = false;

  // Expanded window height to fit player pos, two distances, and the best-run delta
  private Rect _windowRect = new Rect(20, 260, 240, 155);
  private const int FontScale = 2;
  private static readonly Color TextColor = Color.white;
  private static readonly Color AheadColor = Color.green;
  private static readonly Color BehindColor = Color.red;
  private Texture2D _bgTexture;

  private readonly PathComparer pathComparer = new PathComparer();
  private float runStartRealtime = -1f;

  /// <summary>
  /// Call this from wherever the run/split actually starts (the same place
  /// that currently kicks off the split timer), passing the level index.
  /// </summary>
  public void OnRunStarted(int level)
  {
    runStartRealtime = Time.realtimeSinceStartup;
    pathComparer.StartRun(level);
  }

  /// <summary>
  /// Call this from wherever the run/split actually ends, passing whether
  /// the level was genuinely cleared (vs quit/reset) so a partial run never
  /// overwrites the saved best path.
  /// </summary>
  public void OnRunEnded(bool completed)
  {
    pathComparer.EndRun(completed);
    runStartRealtime = -1f;
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
    if (runStartRealtime < 0f || playerTransform == null) return;

    float elapsed = Time.realtimeSinceStartup - runStartRealtime;
    pathComparer.Sample(elapsed, playerTransform.position);
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
    }

    GUI.DrawTexture(_windowRect, _bgTexture, ScaleMode.StretchToFill);

    DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 8, 220, 18), "Player Position");

    if (playerTransform != null)
    {
      Vector3 pos = playerTransform.position;
      string coordText = string.Format("X: {0:F1}  Y: {1:F1}  Z: {2:F1}", pos.x, pos.y, pos.z);
      DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 32, 220, 20), coordText);

      if (pathComparer.HasComparison)
      {
        float delta = pathComparer.DeltaSeconds;
        string sign = delta >= 0 ? "+" : "";
        string deltaText = string.Format("Best Run: {0}{1:F2}s", sign, delta);
        Color deltaColor = delta <= 0 ? AheadColor : BehindColor;
        DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 56, 220, 20), deltaText, deltaColor);
      }

      if (Application.loadedLevel == 5)
      {
        // Calculate distances from player to both points
        float dist1 = Vector3.Distance(pos, point1);
        if (!gotEgg1 && dist1 < 10)
        {
          MBMod.SendNewLocationCheck("level" + Application.loadedLevel + " - egg:47.3 70.1 641.7");
          gotEgg1 = true;
        }
        float dist2 = Vector3.Distance(pos, point2);
        if (!gotEgg2 && dist2 < 10)
        {
          MBMod.SendNewLocationCheck("level" + Application.loadedLevel + " - egg:149.7 18.4 906.0");
          gotEgg2 = true;
        }

        string dist1Text = string.Format("Dist to Pt 1: {0:F1}m", dist1);
        string dist2Text = string.Format("Dist to Pt 2: {0:F1}m", dist2);

        DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 80, 220, 20), dist1Text);
        DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 104, 220, 20), dist2Text);
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
