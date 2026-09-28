using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persists a list of death positions per level and spawns a visual marker
/// for each one when the level loads.
/// </summary>
public static class DeathMarkers
{
  private const float MarkerScale = 1.5f;
  private const float MergeRadius = 5f; // deaths closer than this to a cluster center merge into it
  private const string TransparentShaderName = "Transparent/Diffuse";
  private static readonly Color MarkerColor = new Color(1f, 0f, 0f, 0.5f);

  private static string FileFor(int level)
  {
    return "deaths_level" + level + ".dat";
  }

  private static List<Vector3> Load(int level)
  {
    var result = new List<Vector3>();
    string path = FileFor(level);
    // No file simply means no deaths recorded on this level yet.
    if (!File.Exists(path))
      return result;

    using (var reader = new BinaryReader(File.OpenRead(path)))
    {
      int count = reader.ReadInt32();
      for (int i = 0; i < count; i++)
      {
        float x = reader.ReadSingle();
        float y = reader.ReadSingle();
        float z = reader.ReadSingle();
        result.Add(new Vector3(x, y, z));
      }
    }
    return result;
  }

  private static void Save(int level, List<Vector3> positions)
  {
    using (var writer = new BinaryWriter(File.Create(FileFor(level))))
    {
      writer.Write(positions.Count);
      foreach (var p in positions)
      {
        writer.Write(p.x);
        writer.Write(p.y);
        writer.Write(p.z);
      }
    }
  }

  /// <summary>Appends a death position to the level's saved list.</summary>
  public static void Add(int level, Vector3 position)
  {
    var positions = Load(level);
    positions.Add(position);
    Save(level, positions);
    MBMod.Log.LogInfo(
      "[DeathMarkers] Stored death #" + positions.Count + " on level " + level + " at " + position
    );
  }

  private class Cluster
  {
    public Vector3 Center;
    public int Count;
  }

  /// <summary>
  /// Groups deaths within MergeRadius of a cluster's center into that cluster
  /// (center is the running average of its members). Raw deaths stay saved
  /// individually, so the merge radius can be retuned without losing data.
  /// </summary>
  private static List<Cluster> BuildClusters(List<Vector3> positions)
  {
    var clusters = new List<Cluster>();
    foreach (var p in positions)
    {
      Cluster match = null;
      foreach (var c in clusters)
      {
        if ((c.Center - p).sqrMagnitude <= MergeRadius * MergeRadius)
        {
          match = c;
          break;
        }
      }

      if (match == null)
      {
        clusters.Add(new Cluster { Center = p, Count = 1 });
      }
      else
      {
        match.Center = (match.Center * match.Count + p) / (match.Count + 1);
        match.Count++;
      }
    }
    return clusters;
  }

  /// <summary>Spawns one marker per cluster of saved deaths on the level, sized by how many deaths it holds.</summary>
  public static void SpawnAll(int level)
  {
    var positions = Load(level);
    var clusters = BuildClusters(positions);

    Shader transparentShader = Shader.Find(TransparentShaderName);
    if (transparentShader == null)
    {
      MBMod.Log.LogError(
        "[DeathMarkers] Shader '"
          + TransparentShaderName
          + "' not found in this build; no markers spawned."
      );
      return;
    }

    foreach (var c in clusters)
    {
      SpawnMarker(c, transparentShader);
    }
    MBMod.Log.LogInfo(
      "[DeathMarkers] Restored "
        + clusters.Count
        + " markers ("
        + positions.Count
        + " deaths) on level "
        + level
    );
  }

  private static void SpawnMarker(Cluster cluster, Shader transparentShader)
  {
    var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    marker.name = "DeathMarker";
    marker.transform.position = cluster.Center;
    // Cube root so the marker's volume (not its diameter) grows with death count.
    marker.transform.localScale = Vector3.one * MarkerScale * ((2 + cluster.Count) / 3);

    // Purely visual: must not collide with or trigger anything.
    Object.Destroy(marker.GetComponent<Collider>());

    Material mat = marker.GetComponent<Renderer>().material;
    mat.shader = transparentShader;
    mat.color = MarkerColor;
  }
}
