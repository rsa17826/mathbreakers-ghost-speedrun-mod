using System.IO;
using BepInEx.Logging;
using UnityEngine;

/// <summary>
/// Detects the correct sphere/ball component (NumberInfo) closest to the
/// specified coordinates and shows a percentage rating (-100% to 100%)
/// comparing proximity to the good side vs the bad side.
/// Automatically reloads level 4 if under 0% after 0.3 seconds.
/// </summary>
public class BallDetectorUI : MonoBehaviour
{
  public static ManualLogSource Log;

  // Define the target points
  private readonly Vector3 goodSide = new Vector3(774.9f, 78.5f, 737.5f);
  private readonly Vector3 badSide = new Vector3(789.6f, 78.5f, 723.2f);

  // UI Window layout
  private Rect _windowRect = new Rect(280, 260, 260, 110);
  private const int FontScale = 2;
  private static readonly Color TextColor = Color.white;
  private static readonly Color GoodColor = Color.green;
  private static readonly Color BadColor = Color.red;
  private Texture2D _bgTexture;

  private float levelLoadTime = -1f;
  private bool checkedLevel4 = true;

  private void Start()
  {
    _bgTexture = new Texture2D(1, 1);
    _bgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
    _bgTexture.Apply();

    if (Application.loadedLevel == 4)
    {
      levelLoadTime = Time.time;
      checkedLevel4 = false;
    }
  }

  private void OnLevelWasLoaded(int level)
  {
    if (level == 4 && File.Exists("resetOnBadLV4Luck"))
    {
      levelLoadTime = Time.time;
      checkedLevel4 = false;
    }
    else
    {
      levelLoadTime = -1f;
      checkedLevel4 = true;
    }
  }

  private void Update()
  {
    // Check level 4 condition 0.3 seconds after load
    if (!checkedLevel4 && levelLoadTime > 0f && Application.loadedLevel == 4)
    {
      if (Time.time - levelLoadTime >= 0.3f)
      {
        checkedLevel4 = true;
        Transform closestBall = FindClosestBall();
        if (closestBall != null)
        {
          float distToGood = Vector3.Distance(closestBall.position, goodSide);
          float distToBad = Vector3.Distance(closestBall.position, badSide);
          float totalDist = distToGood + distToBad;
          float percentage = totalDist == 0f ? 0f : ((distToBad - distToGood) / totalDist) * 100f;

          if (percentage < 0f)
          {
            Debug.Log(
              string.Format(
                "[BallDetectorUI] Level 4 check: {0:F1}% (< 0%). Reloading level 4.",
                percentage
              )
            );
            Application.LoadLevel(4);
          }
        }
      }
    }
  }

  private void OnGUI()
  {
    if (checkedLevel4)
    {
      return;
    }
    GUI.DrawTexture(_windowRect, _bgTexture, ScaleMode.StretchToFill);

    DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 8, 240, 18), "Ball Proximity Tracker");

    Transform closestBall = FindClosestBall();

    if (closestBall != null)
    {
      Vector3 ballPos = closestBall.position;
      float distToGood = Vector3.Distance(ballPos, goodSide);
      float distToBad = Vector3.Distance(ballPos, badSide);

      // Calculate percentage from -100% (at bad side) to +100% (at good side)
      float totalDist = distToGood + distToBad;
      float percentage = totalDist == 0f ? 0f : ((distToBad - distToGood) / totalDist) * 100f;

      string posText = string.Format(
        "Ball Pos: {0:F1}, {1:F1}, {2:F1}",
        ballPos.x,
        ballPos.y,
        ballPos.z
      );
      string ratingText = string.Format("Rating: {0:+0.0;-0.0;0.0}%", percentage);
      Color ratingColor = percentage >= 0f ? GoodColor : BadColor;

      DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 32, 240, 20), posText);
      DrawText(new Rect(_windowRect.x + 10, _windowRect.y + 56, 240, 20), ratingText, ratingColor);
    }
    else
    {
      DrawText(
        new Rect(_windowRect.x + 10, _windowRect.y + 32, 240, 20),
        "No ball found in scene."
      );
    }
  }

  /// <summary>
  /// Efficiently searches for active NumberInfo components (SpherePrefab balls)
  /// without lagging the game.
  /// </summary>
  private Transform FindClosestBall()
  {
    Object[] allNumbers = FindObjectsOfType(typeof(NumberInfo));
    Transform closest = null;
    float minDistance = float.MaxValue;

    foreach (Object obj in allNumbers)
    {
      NumberInfo info = obj as NumberInfo;
      if (info != null && info.transform != null)
      {
        float dist = Vector3.Distance(info.transform.position, goodSide);
        if (dist < minDistance)
        {
          minDistance = dist;
          closest = info.transform;
        }
      }
    }
    return closest;
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
