using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Records (time, position) samples for the current run and compares them
/// against the best saved run for the same level, producing a live
/// "seconds ahead/behind" delta based on the closest point on the best path
/// to the player's current position (not just elapsed-time lookup), so
/// deltas stay meaningful even if the two runs take slightly different lines.
///
/// Static because there is exactly one active run at a time and this is
/// called both from PlayerCoordsUI (per-frame sampling/display) and from
/// the Harmony patch in Class1.cs (run-end signal), which have no shared
/// instance to call through.
/// </summary>
public static class PathComparer
{
  public struct PathSample
  {
    public float Time;
    public Vector3 Position;
  }

  private static readonly List<PathSample> currentRun = new List<PathSample>();
  private static List<PathSample> bestRun;
  private static int bestRunSearchIndex;
  private static int currentLevel = -1;
  private static float lastRecordTime;
  private static float runStartRealtime;

  private const float RecordInterval = 0.1f; // seconds between recorded samples
  private const int BackwardSearchWindow = 20; // samples to look behind the last match
  private const int ForwardSearchWindow = 200; // samples to look ahead of the last match

  /// <summary>Seconds behind (positive) or ahead (negative) of the best run's pace at the closest matching position. Zero if no best run is loaded yet.</summary>
  public static float DeltaSeconds { get; private set; }

  /// <summary>True while a run is actively being recorded (between StartRun and EndRun).</summary>
  public static bool IsRunning
  {
    get { return currentLevel >= 0; }
  }

  /// <summary>True once a best-run path has been loaded for the active level, i.e. DeltaSeconds is meaningful.</summary>
  public static bool HasComparison
  {
    get { return bestRun != null && bestRun.Count > 0; }
  }

  private static string PathFileFor(int level)
  {
    return Path.Combine(Application.persistentDataPath, "bestpath_level" + level + ".dat");
  }

  /// <summary>Call when a run starts (e.g. from the same code that currently starts the split timer).</summary>
  public static void StartRun(int level)
  {
    currentLevel = level;
    currentRun.Clear();
    bestRunSearchIndex = 0;
    DeltaSeconds = 0f;
    lastRecordTime = float.NegativeInfinity;
    runStartRealtime = Time.realtimeSinceStartup;
    bestRun = LoadBestPath(level);
    MBMod.Log.LogInfo(
      "[PathComparer] StartRun level=" + level + " bestRunLoaded=" + (bestRun != null ? bestRun.Count.ToString() : "none")
    );
  }

  /// <summary>Call every frame with the player's current position; does nothing if no run is active. Elapsed time is measured internally from StartRun.</summary>
  public static void Tick(Vector3 position)
  {
    if (!IsRunning) return;
    Sample(Time.realtimeSinceStartup - runStartRealtime, position);
  }

  /// <summary>Call every frame (e.g. from Update) with seconds elapsed since the run/split started and the player's current position.</summary>
  public static void Sample(float elapsed, Vector3 position)
  {
    if (currentLevel < 0) return;

    if (elapsed - lastRecordTime >= RecordInterval)
    {
      currentRun.Add(new PathSample { Time = elapsed, Position = position });
      lastRecordTime = elapsed;
    }

    if (HasComparison)
    {
      UpdateDelta(elapsed, position);
    }
  }

  /// <summary>Call when a run ends. Pass completed=true only if the level was actually cleared, so aborted runs never overwrite the best path.</summary>
  public static void EndRun(bool completed)
  {
    MBMod.Log.LogInfo(
      "[PathComparer] EndRun completed=" + completed + " sampleCount=" + currentRun.Count + " level=" + currentLevel
    );
    if (completed && currentRun.Count > 0)
    {
      SaveIfBest(currentLevel, currentRun);
    }
    currentLevel = -1;
    bestRun = null;
  }

  private static void UpdateDelta(float elapsed, Vector3 position)
  {
    int searchStart = Math.Max(0, bestRunSearchIndex - BackwardSearchWindow);
    int searchEnd = Math.Min(bestRun.Count - 1, bestRunSearchIndex + ForwardSearchWindow);

    float bestDistSq = float.MaxValue;
    int bestIndex = bestRunSearchIndex;

    for (int i = searchStart; i <= searchEnd; i++)
    {
      float distSq = (bestRun[i].Position - position).sqrMagnitude;
      if (distSq < bestDistSq)
      {
        bestDistSq = distSq;
        bestIndex = i;
      }
    }

    bestRunSearchIndex = bestIndex;
    DeltaSeconds = elapsed - bestRun[bestIndex].Time;
  }

  private static void SaveIfBest(int level, List<PathSample> run)
  {
    float thisRunTime = run[run.Count - 1].Time;
    var existing = LoadBestPath(level);
    if (existing != null && existing.Count > 0 && existing[existing.Count - 1].Time <= thisRunTime)
    {
      MBMod.Log.LogInfo(
        "[PathComparer] Not saving: existing best (" + existing[existing.Count - 1].Time + "s) <= this run (" + thisRunTime + "s)"
      );
      return; // existing best is still equal or faster
    }
    SavePath(level, run);
    MBMod.Log.LogInfo("[PathComparer] Saved new best to " + PathFileFor(level) + " (" + thisRunTime + "s, " + run.Count + " samples)");
  }

  private static void SavePath(int level, List<PathSample> run)
  {
    using (var writer = new BinaryWriter(File.Create(PathFileFor(level))))
    {
      writer.Write(run.Count);
      foreach (var sample in run)
      {
        writer.Write(sample.Time);
        writer.Write(sample.Position.x);
        writer.Write(sample.Position.y);
        writer.Write(sample.Position.z);
      }
    }
  }

  private static List<PathSample> LoadBestPath(int level)
  {
    string path = PathFileFor(level);
    if (!File.Exists(path)) return null;

    using (var reader = new BinaryReader(File.OpenRead(path)))
    {
      int count = reader.ReadInt32();
      var result = new List<PathSample>(count);
      for (int i = 0; i < count; i++)
      {
        float t = reader.ReadSingle();
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();
        result.Add(new PathSample { Time = t, Position = new Vector3(x, y, z) });
      }
      return result;
    }
  }
}
