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
    public Quaternion Rotation;
    public Vector3 Velocity;
    public bool Grounded;
    public bool Dying;
  }

  private static readonly List<PathSample> currentRun = new List<PathSample>();
  private static List<PathSample> bestRun;
  private static int bestRunSearchIndex;
  private static int currentLevel = -1;
  private static float lastRecordTime;
  private static float runStartTime;

  private const float RecordInterval = 0f;
  private const int BackwardSearchWindow = 120; // samples to look behind the last match (~2s at 60fps)
  private const int ForwardSearchWindow = 1200; // samples to look ahead of the last match (~20s at 60fps)

  /// <summary>Seconds behind (positive) or ahead (negative) of the best run's pace at the closest matching position. Zero if no best run is loaded yet.</summary>
  public static float DeltaSeconds { get; set; }

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

  /// <summary>Seconds since the current run started, or 0 if no run is active.</summary>
  public static float ElapsedTime
  {
    get { return IsRunning ? Time.time - runStartTime : 0f; }
  }

  /// <summary>
  /// Samples pose (Position, Rotation, Velocity) at the given elapsed time via linear interpolation.
  /// </summary>
  public static bool TryGetBestTransform(
    float elapsed,
    out Vector3 position,
    out Quaternion rotation,
    out Vector3 velocity,
    out bool grounded,
    out bool dying
  )
  {
    position = Vector3.zero;
    rotation = Quaternion.identity;
    velocity = Vector3.zero;
    grounded = true;
    dying = false;

    if (bestRun == null || bestRun.Count == 0)
      return false;

    if (elapsed <= bestRun[0].Time)
    {
      position = bestRun[0].Position;
      rotation = bestRun[0].Rotation;
      velocity = bestRun[0].Velocity;
      grounded = bestRun[0].Grounded;
      dying = bestRun[0].Dying;
      return true;
    }

    var last = bestRun[bestRun.Count - 1];
    if (elapsed >= last.Time)
    {
      position = last.Position;
      rotation = last.Rotation;
      velocity = last.Velocity;
      grounded = last.Grounded;
      dying = last.Dying;
      return true;
    }

    for (int i = 1; i < bestRun.Count; i++)
    {
      if (bestRun[i].Time >= elapsed)
      {
        var prev = bestRun[i - 1];
        var next = bestRun[i];
        float span = next.Time - prev.Time;
        float t = span > 0f ? (elapsed - prev.Time) / span : 0f;

        position = Vector3.Lerp(prev.Position, next.Position, t);
        rotation = Quaternion.Slerp(prev.Rotation, next.Rotation, t);
        velocity = Vector3.Lerp(prev.Velocity, next.Velocity, t);
        grounded = t < 0.5f ? prev.Grounded : next.Grounded;
        dying = t < 0.5f ? prev.Dying : next.Dying;
        return true;
      }
    }

    position = last.Position;
    rotation = last.Rotation;
    velocity = last.Velocity;
    grounded = last.Grounded;
    dying = last.Dying;
    return true;
  }

  public static bool TryGetBestTransform(
    float elapsed,
    out Vector3 position,
    out Quaternion rotation,
    out Vector3 velocity
  )
  {
    bool dummyGrounded;
    bool dummyDying;
    return TryGetBestTransform(
      elapsed,
      out position,
      out rotation,
      out velocity,
      out dummyGrounded,
      out dummyDying
    );
  }

  public static bool TryGetBestPosition(float elapsed, out Vector3 position)
  {
    Quaternion rot;
    Vector3 vel;
    return TryGetBestTransform(elapsed, out position, out rot, out vel);
  }

  private static string PathFileFor(int level)
  {
    return "bestpath_level" + level + ".dat";
  }

  public static void StartRun(int level)
  {
    currentLevel = level;
    currentRun.Clear();
    bestRunSearchIndex = 0;
    DeltaSeconds = 0f;
    lastRecordTime = float.NegativeInfinity;
    runStartTime = Time.time;
    bestRun = LoadBestPath(level);
    MBMod.Log.LogInfo(
      "[PathComparer] StartRun level="
        + level
        + " bestRunLoaded="
        + (bestRun != null ? bestRun.Count.ToString() : "none")
    );
  }

  public static void Tick(
    Vector3 position,
    Quaternion rotation,
    Vector3 velocity,
    bool grounded,
    bool dying
  )
  {
    if (!IsRunning)
      return;
    Sample(Time.time - runStartTime, position, rotation, velocity, grounded, dying);
  }

  public static void Sample(
    float elapsed,
    Vector3 position,
    Quaternion rotation,
    Vector3 velocity,
    bool grounded,
    bool dying
  )
  {
    if (currentLevel < 0)
      return;

    if (elapsed - lastRecordTime >= RecordInterval)
    {
      currentRun.Add(
        new PathSample
        {
          Time = elapsed,
          Position = position,
          Rotation = rotation,
          Velocity = velocity,
          Grounded = grounded,
          Dying = dying,
        }
      );
      lastRecordTime = elapsed;
    }

    if (HasComparison)
    {
      UpdateDelta(elapsed, position);
    }
  }

  public static void EndRun(bool completed)
  {
    MBMod.Log.LogInfo(
      "[PathComparer] EndRun completed="
        + completed
        + " sampleCount="
        + currentRun.Count
        + " level="
        + currentLevel
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
    if (MBMod.fast)
      return;
    float thisRunTime = run[run.Count - 1].Time;
    var existing = LoadBestPath(level);
    if (existing != null && existing.Count > 0 && existing[existing.Count - 1].Time <= thisRunTime)
    {
      MBMod.Log.LogInfo(
        "[PathComparer] Not saving: existing best ("
          + existing[existing.Count - 1].Time
          + "s) <= this run ("
          + thisRunTime
          + "s)"
      );
      return;
    }
    SavePath(level, run);
    MBMod.Log.LogInfo(
      "[PathComparer] Saved new best to "
        + PathFileFor(level)
        + " ("
        + thisRunTime
        + "s, "
        + run.Count
        + " samples)"
    );
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
        writer.Write(sample.Rotation.x);
        writer.Write(sample.Rotation.y);
        writer.Write(sample.Rotation.z);
        writer.Write(sample.Rotation.w);
        writer.Write(sample.Velocity.x);
        writer.Write(sample.Velocity.y);
        writer.Write(sample.Velocity.z);
        writer.Write(sample.Grounded);
        writer.Write(sample.Dying);
      }
    }
  }

  public static List<PathSample> LoadBestPath(int level)
  {
    string path = PathFileFor(level);
    if (!File.Exists(path))
      return null;

    var result = new List<PathSample>();
    bool needsUpgrade = false;

    using (var reader = new BinaryReader(File.OpenRead(path)))
    {
      int count = reader.ReadInt32();
      result.Capacity = count;
      long fileLength = reader.BaseStream.Length;

      long expectedBytesLegacyV1 = 4 + (long)count * 16; // Time + Pos (4 floats * 4)
      long expectedBytesLegacyV2 = 4 + (long)count * 44; // Time + Pos + Rot + Vel (11 floats * 4)

      bool isLegacyV1 = fileLength == expectedBytesLegacyV1;
      bool isLegacyV2 = fileLength == expectedBytesLegacyV2;

      if (isLegacyV1 || isLegacyV2)
      {
        needsUpgrade = true;
      }

      for (int i = 0; i < count; i++)
      {
        float t = reader.ReadSingle();
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();

        Quaternion rot = Quaternion.identity;
        Vector3 vel = Vector3.zero;
        bool grounded = true;
        bool dying = false;

        if (!isLegacyV1)
        {
          float rx = reader.ReadSingle();
          float ry = reader.ReadSingle();
          float rz = reader.ReadSingle();
          float rw = reader.ReadSingle();
          rot = new Quaternion(rx, ry, rz, rw);

          float vx = reader.ReadSingle();
          float vy = reader.ReadSingle();
          float vz = reader.ReadSingle();
          vel = new Vector3(vx, vy, vz);

          if (!isLegacyV2 && reader.BaseStream.Position < reader.BaseStream.Length)
          {
            grounded = reader.ReadBoolean();
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
              dying = reader.ReadBoolean();
            }
            else
            {
              needsUpgrade = true; // Missing 'dying' flag
            }
          }
        }

        result.Add(
          new PathSample
          {
            Time = t,
            Position = new Vector3(x, y, z),
            Rotation = rot,
            Velocity = vel,
            Grounded = grounded,
            Dying = dying,
          }
        );
      }

      // Infer missing values for older file versions
      if (isLegacyV1 || isLegacyV2 || needsUpgrade)
      {
        Quaternion lastValidRotation = Quaternion.identity;

        for (int i = 0; i < result.Count; i++)
        {
          var s = result[i];

          // Calculate inferred Velocity if missing
          if (isLegacyV1)
          {
            Vector3 vel = Vector3.zero;
            if (i > 0)
            {
              float dt = s.Time - result[i - 1].Time;
              if (dt > 0.0001f)
              {
                vel = (s.Position - result[i - 1].Position) / dt;
              }
            }
            s.Velocity = vel;
          }

          // Calculate inferred Facing Direction/Rotation if missing
          if (isLegacyV1)
          {
            Vector3 moveDirection = Vector3.zero;
            if (i < result.Count - 1)
            {
              moveDirection = result[i + 1].Position - s.Position;
            }
            else if (i > 0)
            {
              moveDirection = s.Position - result[i - 1].Position;
            }

            if (moveDirection.sqrMagnitude > 0.0001f)
            {
              s.Rotation = Quaternion.LookRotation(moveDirection.normalized);
              lastValidRotation = s.Rotation;
            }
            else
            {
              s.Rotation = lastValidRotation;
            }
          }

          result[i] = s;
        }
      }
    }

    // Overwrite old file with the upgraded latest format on disk
    if (needsUpgrade && result.Count > 0)
    {
      try
      {
        SavePath(level, result);
        MBMod.Log.LogInfo(
          "[PathComparer] Upgraded legacy file path for level " + level + " to latest format."
        );
      }
      catch (Exception ex)
      {
        MBMod.Log.LogWarning("[PathComparer] Failed to save upgraded path file: " + ex.Message);
      }
    }

    return result;
  }
}
