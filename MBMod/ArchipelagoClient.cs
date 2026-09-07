using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WebSocketSharp;

namespace ArchipelagoNet
{
  /// <summary>
  /// A .NET 3.5 / Unity-Mono-compatible port of the JS ArchipelagoClient
  /// (native WebSocket implementation of the Archipelago Network Protocol).
  ///
  /// JSON is handled via MiniJSON (plain Dictionary&lt;string, object&gt; /
  /// List&lt;object&gt;), not Newtonsoft.Json.Linq. Newtonsoft's JObject/
  /// JArray implement IDynamicMetaObjectProvider (for their dynamic
  /// property support), which pulls in the .NET DLR - and that fails to
  /// load entirely on Unity 4.2's very old embedded Mono runtime
  /// (TypeLoadException as soon as JObject is touched). MiniJSON has no
  /// such dependency.
  ///
  /// .NET 3.5 has no async/await, no Task, no System.Net.WebSockets, and no
  /// ConcurrentDictionary, so this version is fully event-driven (like the
  /// original JS) instead of async: call Connect(), then subscribe to the
  /// OnXxx events. All socket callbacks run on websocket-sharp's own thread;
  /// marshal back to the Unity main thread yourself if you touch UnityEngine
  /// APIs from a handler (e.g. via a queue drained in Update()).
  ///
  /// Requires:
  ///   - websocket-sharp (NuGet: WebSocketSharp, targets net35)
  ///   - MiniJson.cs (bundled alongside this file, no external dependency)
  /// </summary>
  public class ArchipelagoClient
  {
    // ---- Public events (replace window.onX arrays) ----
    public event Action OnConnectedEvent;
    public event Action OnReceivedItemsEvent;
    public event Action OnRoomUpdateEvent;
    public event Action<string> OnLog;
    public event Action<string> OnWarn;
    public event Action<string> OnError;
    public event Action<string> OnDeathLinkReceived;

    /// <summary>The raw hint list (List&lt;object&gt;, each entry a Dictionary&lt;string, object&gt;).</summary>
    public event Action<List<object>> OnHintsUpdated;

    /// <summary>Fired once per newly-received item: (itemName, itemPayload as Dictionary&lt;string, object&gt;).</summary>
    public event Action<string, Dictionary<string, object>> OnGiveItem;

    // ---- Connection config ----
    public string Hostname { get; private set; }
    public int? Port { get; private set; }
    public string Game { get; private set; }
    public string PlayerName { get; private set; }
    public string Password { get; private set; }
    public string Url { get; private set; }
    public bool UseWss { get; private set; }

    // ---- Protocol state ----
    public bool IsAuthenticated { get; private set; }
    public bool DoneConnecting { get; private set; }
    public int Team { get; private set; }
    public int Slot { get; private set; }
    public List<int> MissingLocations = new List<int>();
    public List<int> CheckedLocations = new List<int>();
    public Dictionary<int, SlotInfo> SlotInfoBySlot = new Dictionary<int, SlotInfo>();
    public List<PlayerInfo> Players = new List<PlayerInfo>();
    public Dictionary<string, object> SlotData = new Dictionary<string, object>();

    public Dictionary<string, Dictionary<long, string>> ItemIdToName =
      new Dictionary<string, Dictionary<long, string>>();
    public Dictionary<string, Dictionary<long, string>> LocationIdToName =
      new Dictionary<string, Dictionary<long, string>>();
    public Dictionary<int, ScoutedItem> ScoutedItems = new Dictionary<int, ScoutedItem>();

    public bool DeathLinkEnabled = true;

    public int LastProcessedIndex = 0;
    public int ItemCount = 0;
    public int LastReceivedItem = 0;
    private bool _goalCompleteSent = false;

    /// <summary>Set this to true once your player/save data has finished loading.
    /// Packets that arrive before that are queued in WaitingPackets.</summary>
    public bool PlayerLoaded = false;
    public readonly List<Dictionary<string, object>> WaitingPackets =
      new List<Dictionary<string, object>>();

    /// <summary>Call this once your game/save state is ready to receive
    /// items and room updates. Anything that arrived before this point
    /// (queued in WaitingPackets) gets processed immediately.</summary>
    public void MarkPlayerLoaded()
    {
      if (PlayerLoaded)
        return;
      PlayerLoaded = true;

      // Copy first: HandlePacket may itself queue new packets into
      // WaitingPackets if something resets PlayerLoaded mid-drain,
      // and mutating a list while foreach-ing it throws.
      var queued = new List<Dictionary<string, object>>(WaitingPackets);
      WaitingPackets.Clear();

      foreach (var packet in queued)
      {
        HandlePacket(packet);
      }
    }

    // Locations checked locally but not yet acknowledged by the server.
    // Plain Dictionary + lock, since .NET 3.5 has no ConcurrentDictionary.
    private readonly Dictionary<int, byte> _checksInFlight = new Dictionary<int, byte>();
    private readonly object _checksInFlightLock = new object();

    private WebSocket _socket;
    private bool _isFallbackMode;
    private double _lastDeathLinkReceivedTime = -1;
    private readonly Random _rng = new Random();

    public ArchipelagoClient(
      string hostname,
      int? port,
      string game,
      string playerName,
      string password
    )
    {
      Hostname = hostname;
      Port = port;
      Game = game;
      PlayerName = playerName;
      Password = password ?? "";
      UseWss = false;
      Url = BuildUrl(UseWss);
    }

    private string BuildUrl(bool wss)
    {
      var scheme = wss ? "wss" : "ws";
      var portPart = (Port.HasValue && Port.Value != 0) ? (":" + Port.Value) : "";
      return scheme + "://" + Hostname + portPart;
    }

    public void AddToChecksInFlight(int locationId)
    {
      lock (_checksInFlightLock)
      {
        _checksInFlight[locationId] = 1;
      }
    }

    private void RemoveFromChecksInFlight(int locationId)
    {
      lock (_checksInFlightLock)
      {
        _checksInFlight.Remove(locationId);
      }
    }

    // ---------------------------------------------------------------
    // Small MiniJSON access helpers (Dictionary<string,object> / List<object>
    // stand in for JObject/JArray; object stands in for JToken).
    // ---------------------------------------------------------------

    private static object Get(Dictionary<string, object> obj, string key)
    {
      if (obj == null)
        return null;
      object v;
      return obj.TryGetValue(key, out v) ? v : null;
    }

    private static Dictionary<string, object> AsObj(object o)
    {
      return o as Dictionary<string, object>;
    }

    private static List<object> AsArr(object o)
    {
      return o as List<object>;
    }

    private static string AsStr(object o)
    {
      return o == null ? null : o.ToString();
    }

    private static int AsInt(object o)
    {
      return o == null ? 0 : Convert.ToInt32(o);
    }

    private static long AsLong(object o)
    {
      return o == null ? 0 : Convert.ToInt64(o);
    }

    private static double AsDouble(object o)
    {
      return o == null ? 0 : Convert.ToDouble(o);
    }

    private static List<int> AsIntList(object o)
    {
      var arr = AsArr(o);
      var result = new List<int>();
      if (arr == null)
        return result;
      foreach (var item in arr)
        result.Add(AsInt(item));
      return result;
    }

    private static List<string> AsStringList(object o)
    {
      var arr = AsArr(o);
      var result = new List<string>();
      if (arr == null)
        return result;
      foreach (var item in arr)
        result.Add(AsStr(item));
      return result;
    }

    // ---------------------------------------------------------------
    // Connection lifecycle
    // ---------------------------------------------------------------

    /// <summary>Opens the socket. Non-blocking: connection/handshake progress
    /// is reported via the OnLog/OnConnectedEvent/OnError events.</summary>
    public void Connect()
    {
      _socket = new WebSocket(Url);

      _socket.OnOpen += (sender, e) =>
      {
        Log(
          "WebSocket connection established ("
            + Url.Split(':')[0]
            + "). Awaiting 'RoomInfo' from server..."
        );
      };

      _socket.OnMessage += (sender, e) =>
      {
        try
        {
          var parsed = Json.Deserialize(e.Data) as List<object>;
          if (parsed == null)
          {
            Error("Expected a JSON array of packets, got something else.");
            return;
          }

          foreach (var packetObj in parsed)
          {
            var packet = AsObj(packetObj);
            if (packet != null)
              HandlePacket(packet);
          }
        }
        catch (Exception ex)
        {
          Error("Failed to parse incoming JSON payload: " + ex.Message);
        }
      };

      _socket.OnClose += (sender, e) =>
      {
        Warn("[WARNING] Disconnected from Archipelago server. Code: " + e.Code);
      };

      _socket.OnError += (sender, e) =>
      {
        Error("WebSocket network error: " + e.Message);

        // If secure connection fails and we haven't shifted to ws:// yet,
        // flip once and retry (mirrors the JS client's fallback logic).
        if (!_isFallbackMode)
        {
          UseWss = !UseWss;
          _isFallbackMode = true;
          Url = BuildUrl(UseWss);
          Warn("Retrying with " + Url);

          // Connect() below creates a brand-new WebSocket instance,
          // so the old socket's handlers are simply abandoned here
          // (events can't be cleared with '=' from outside the class).
          Connect();
        }
        else
        {
          _isFallbackMode = false;
        }
      };

      _socket.ConnectAsync();
    }

    public void Disconnect()
    {
      if (_socket != null && _socket.IsAlive)
      {
        _socket.Close();
      }
    }

    public void SendPackets(params object[] packetsArray)
    {
      var json = Json.Serialize(new List<object>(packetsArray));
      Log("SENDING TO SERVER: " + json);

      if (_socket != null && _socket.IsAlive)
      {
        _socket.Send(json);
      }
      else
      {
        Error("Cannot send packet; WebSocket connection is closed.");
      }
    }

    // ---------------------------------------------------------------
    // Packet routing
    // ---------------------------------------------------------------

    private void HandlePacket(Dictionary<string, object> packet)
    {
      var cmd = AsStr(Get(packet, "cmd"));
      Log("asdasdasdasdasd: " + cmd + Json.Serialize(packet));
      switch (cmd)
      {
        case "RoomInfo":
          OnRoomInfo(packet);
          break;
        case "Connected":
          OnConnected(packet);
          break;
        case "DataPackage":
          OnDataPackage(packet);
          break;
        case "ConnectionRefused":
          OnConnectionRefused(packet);
          break;
        case "ReceivedItems":
          OnReceivedItems(packet);
          break;
        case "PrintJSON":
          OnPrintJSON(packet);
          break;
        case "RoomUpdate":
          OnRoomUpdate(packet);
          break;
        case "LocationInfo":
          OnLocationInfo(packet);
          break;
        case "Bounced":
          OnBounced(packet);
          break;
        case "Retrieved":
          OnRetrieved(packet);
          break;
        case "SetReply":
          OnSetReply(packet);
          break;
        case "InvalidPacket":
          Error(
            "Archipelago Server rejected payload: type="
              + Get(packet, "type")
              + ", reason="
              + Get(packet, "text")
              + ", originalCommand="
              + Get(packet, "original_cmd")
          );
          break;
        default:
          Log("Received unhandled protocol command: " + cmd);
          break;
      }
    }

    private void OnRoomUpdate(Dictionary<string, object> packet)
    {
      if (!PlayerLoaded)
      {
        WaitingPackets.Add(packet);
        return;
      }

      Log("[Archipelago] Room state updated by server.");

      var checkedLocationsToken = Get(packet, "checked_locations");
      if (checkedLocationsToken != null)
      {
        var checkedLocations = AsIntList(checkedLocationsToken);
        foreach (var loc in checkedLocations)
        {
          if (!CheckedLocations.Contains(loc))
            CheckedLocations.Add(loc);

          MissingLocations.RemoveAll(m => m == loc);
          RemoveFromChecksInFlight(loc);
        }
      }

      if (OnRoomUpdateEvent != null)
        OnRoomUpdateEvent();
    }

    private void OnRoomInfo(Dictionary<string, object> packet)
    {
      var seedName = AsStr(Get(packet, "seed_name"));
      Log("RoomInfo received. Multiworld Seed: " + seedName);

      var version = new Dictionary<string, object>();
      version["major"] = 0;
      version["minor"] = 6;
      version["build"] = 8;
      version["class"] = "Version";

      var connectPayload = new Dictionary<string, object>();
      connectPayload["cmd"] = "Connect";
      connectPayload["password"] = Password;
      connectPayload["game"] = Game;
      connectPayload["name"] = PlayerName;
      connectPayload["uuid"] = GenerateUUID();
      connectPayload["version"] = version;
      connectPayload["items_handling"] = 7;
      connectPayload["tags"] = DeathLinkEnabled
        ? new List<object> { "DeathLink" }
        : new List<object>();
      connectPayload["slot_data"] = true;

      Log("Authenticating with server...");

      var getDataPackage = new Dictionary<string, object>();
      getDataPackage["cmd"] = "GetDataPackage";
      getDataPackage["games"] = Get(packet, "games");

      SendPackets(getDataPackage, connectPayload);
    }

    private void OnDataPackage(Dictionary<string, object> packet)
    {
      var data = AsObj(Get(packet, "data"));
      var games = data != null ? AsObj(Get(data, "games")) : null;
      if (games == null)
        return;

      foreach (var gameEntry in games)
      {
        var game = gameEntry.Key;
        var gameData = AsObj(gameEntry.Value);
        if (gameData == null)
          continue;

        var itemMap = new Dictionary<long, string>();
        var itemNameToId = AsObj(Get(gameData, "item_name_to_id"));
        if (itemNameToId != null)
        {
          foreach (var p in itemNameToId)
            itemMap[AsLong(p.Value)] = p.Key;
        }
        ItemIdToName[game] = itemMap;

        var locMap = new Dictionary<long, string>();
        var locNameToId = AsObj(Get(gameData, "location_name_to_id"));
        if (locNameToId != null)
        {
          foreach (var p in locNameToId)
            locMap[AsLong(p.Value)] = p.Key;
        }
        LocationIdToName[game] = locMap;
      }

      Log(
        "[Archipelago] Received DataPackage for games: " + string.Join(", ", games.Keys.ToArray())
      );
    }

    /// <summary>Resolve an item id to its display name, given which slot sent it.</summary>
    public string GetItemName(long itemId, int sendingSlot)
    {
      SlotInfo info;
      var game = SlotInfoBySlot.TryGetValue(sendingSlot, out info) ? info.Game : null;
      Dictionary<long, string> map;
      string name;
      if (
        game != null
        && ItemIdToName.TryGetValue(game, out map)
        && map.TryGetValue(itemId, out name)
      )
        return name;
      return "Unknown Item " + game + " - (" + itemId + ")";
    }

    private void OnConnected(Dictionary<string, object> packet)
    {
      Log(
        "Successfully connected! Team: " + Get(packet, "team") + ", Slot ID: " + Get(packet, "slot")
      );

      IsAuthenticated = true;
      DoneConnecting = true;
      Team = AsInt(Get(packet, "team"));
      Slot = AsInt(Get(packet, "slot"));
      MissingLocations = AsIntList(Get(packet, "missing_locations"));
      CheckedLocations = AsIntList(Get(packet, "checked_locations"));

      SlotInfoBySlot.Clear();
      var slotInfo = AsObj(Get(packet, "slot_info"));
      if (slotInfo != null)
      {
        foreach (var p in slotInfo)
        {
          var entry = AsObj(p.Value);
          if (entry == null)
            continue;

          var si = new SlotInfo();
          si.Name = AsStr(Get(entry, "name"));
          si.Game = AsStr(Get(entry, "game"));
          si.Type = AsInt(Get(entry, "type"));
          SlotInfoBySlot[int.Parse(p.Key)] = si;
        }
      }

      Players.Clear();
      var playersArr = AsArr(Get(packet, "players"));
      if (playersArr != null)
      {
        foreach (var pObj in playersArr)
        {
          var p = AsObj(pObj);
          if (p == null)
            continue;

          var info = new PlayerInfo();
          info.Team = AsInt(Get(p, "team"));
          info.Slot = AsInt(Get(p, "slot"));
          info.Alias = AsStr(Get(p, "alias"));
          info.Name = AsStr(Get(p, "name"));
          Players.Add(info);
        }
      }

      SlotData = AsObj(Get(packet, "slot_data")) ?? new Dictionary<string, object>();

      if (OnConnectedEvent != null)
        OnConnectedEvent();

      SendStatusUpdate(10); // CLIENT_READY
      RequestHints();
    }

    private void OnConnectionRefused(Dictionary<string, object> packet)
    {
      DoneConnecting = true;
      Error("Authentication rejected by server. Errors: " + Get(packet, "errors"));
    }

    /// <summary>Scout locations to see what item they contain, optionally creating a hint.</summary>
    public void SendLocationScouts(IEnumerable<int> locationIds, int createAsHint)
    {
      if (!IsAuthenticated)
      {
        Error("Cannot scout locations yet. Waiting for authentication.");
        return;
      }

      var payload = new Dictionary<string, object>();
      payload["cmd"] = "LocationScouts";
      payload["locations"] = locationIds.Cast<object>().ToList();
      payload["create_as_hint"] = createAsHint;

      SendPackets(payload);
    }

    private void OnLocationInfo(Dictionary<string, object> packet)
    {
      SlotInfo mySlotInfo;
      var myGame = SlotInfoBySlot.TryGetValue(Slot, out mySlotInfo) ? mySlotInfo.Game : null;

      var locations = AsArr(Get(packet, "locations"));
      if (locations == null)
        return;

      foreach (var entryObj in locations)
      {
        var entry = AsObj(entryObj);
        if (entry == null)
          continue;

        var location = AsInt(Get(entry, "location"));
        var item = AsLong(Get(entry, "item"));
        var player = AsInt(Get(entry, "player"));
        var flags = AsInt(Get(entry, "flags"));

        var itemName = GetItemName(item, player);
        string locationName;
        Dictionary<long, string> locMap;
        string ln;
        if (
          myGame != null
          && LocationIdToName.TryGetValue(myGame, out locMap)
          && locMap.TryGetValue(location, out ln)
        )
          locationName = ln;
        else
          locationName = "Unknown Location (" + location + ")";

        var scouted = new ScoutedItem();
        scouted.ItemName = itemName;
        scouted.ItemPlayer = player;
        scouted.LocationName = locationName;
        scouted.Flags = flags;
        ScoutedItems[location] = scouted;
      }
    }

    /// <summary>Sends a DeathLink to every other connected client that opted in.</summary>
    public void SendDeathLink(string cause)
    {
      if (!DeathLinkEnabled)
        return;

      var time = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

      var data = new Dictionary<string, object>();
      data["time"] = time;
      data["cause"] = cause;
      data["source"] = PlayerName;

      var payload = new Dictionary<string, object>();
      payload["cmd"] = "Bounce";
      payload["tags"] = new List<object> { "DeathLink" };
      payload["data"] = data;

      SendPackets(payload);
    }

    private void OnBounced(Dictionary<string, object> packet)
    {
      var tags = AsStringList(Get(packet, "tags"));
      if (tags == null || !tags.Contains("DeathLink"))
        return;
      if (!DeathLinkEnabled)
        return;

      var data = AsObj(Get(packet, "data"));
      if (data == null)
        return;

      var time = AsDouble(Get(data, "time"));
      var cause = AsStr(Get(data, "cause"));
      var source = AsStr(Get(data, "source"));

      if (_lastDeathLinkReceivedTime == time)
        return;
      _lastDeathLinkReceivedTime = time;

      Log("[DeathLink] " + (string.IsNullOrEmpty(cause) ? (source + " died mysteriously") : cause));

      if (source == PlayerName)
        return;
      if (OnDeathLinkReceived != null)
        OnDeathLinkReceived(cause);
    }

    /// <summary>Requests a hint from the server using the in-game text command system.</summary>
    public void RequestItemHint(string searchString)
    {
      if (!IsAuthenticated)
      {
        Error("Cannot request hint yet. Waiting for authentication.");
        return;
      }

      var payload = new Dictionary<string, object>();
      payload["cmd"] = "Say";
      payload["text"] = "!hint " + searchString;
      SendPackets(payload);
    }

    private void OnReceivedItems(Dictionary<string, object> packet)
    {
      var items = AsArr(Get(packet, "items"));
      if (items == null)
        return;

      Log("Received packet containing " + items.Count + " items.");

      if (!PlayerLoaded)
      {
        WaitingPackets.Add(packet);
        return;
      }

      var index = AsInt(Get(packet, "index"));

      for (int offset = 0; offset < items.Count; offset++)
      {
        var item = AsObj(items[offset]);
        if (item == null)
          continue;

        ItemCount += 1;

        var itemId = AsLong(Get(item, "item"));
        var itemPlayer = AsInt(Get(item, "player"));
        var itemName = GetItemName(itemId, Slot);
        var senderPlayer = Players.FirstOrDefault(p => p.Slot == itemPlayer);
        var senderName = senderPlayer != null ? senderPlayer.Alias : null;
        var globalIndex = index + offset;

        bool isNew = ItemCount > LastReceivedItem;
        Log(
          "[Item Received] ID: "
            + itemId
            + " ("
            + itemName
            + ")"
            + (isNew ? "" : " - already received")
            + " - sent by "
            + senderName
        );

        if (isNew)
        {
          if (ItemCount - 1 == LastReceivedItem)
          {
            if (OnGiveItem != null)
              OnGiveItem(itemName, item);
          }
          else
          {
            Warn("something went wrong with sending items!! " + LastReceivedItem + " " + ItemCount);
          }
          LastReceivedItem = ItemCount;
        }

        LastProcessedIndex = globalIndex + 1;
      }

      if (OnReceivedItemsEvent != null)
        OnReceivedItemsEvent();
    }

    /// <summary>
    /// Turns a single JSONMessagePart into displayable text, resolving id-based
    /// parts (player_id / item_id / location_id) to names.
    /// </summary>
    private string ResolveMessagePart(Dictionary<string, object> part)
    {
      var type = AsStr(Get(part, "type"));
      var text = AsStr(Get(part, "text")) ?? "";

      if (type == "player_id")
      {
        var player = Players.FirstOrDefault(p => p.Slot.ToString() == text);
        return player != null ? (player.Alias ?? player.Name) : ("Player " + text);
      }
      if (type == "item_id")
      {
        var playerToken = Get(part, "player");
        var playerSlot = playerToken != null ? AsInt(playerToken) : -1;
        SlotInfo info;
        var game = SlotInfoBySlot.TryGetValue(playerSlot, out info) ? info.Game : null;
        long id;
        Dictionary<long, string> map;
        string name;
        if (
          game != null
          && ItemIdToName.TryGetValue(game, out map)
          && long.TryParse(text, out id)
          && map.TryGetValue(id, out name)
        )
          return name;
        return "Item #" + text;
      }
      if (type == "location_id")
      {
        var playerToken = Get(part, "player");
        var playerSlot = playerToken != null ? AsInt(playerToken) : -1;
        SlotInfo info;
        var game = SlotInfoBySlot.TryGetValue(playerSlot, out info) ? info.Game : null;
        long id;
        Dictionary<long, string> map;
        string name;
        if (
          game != null
          && LocationIdToName.TryGetValue(game, out map)
          && long.TryParse(text, out id)
          && map.TryGetValue(id, out name)
        )
          return name;
        return "Location #" + text;
      }
      return text;
    }

    private void OnPrintJSON(Dictionary<string, object> packet)
    {
      var data = AsArr(Get(packet, "data"));
      if (data == null)
        return;

      var sb = new System.Text.StringBuilder();
      foreach (var partObj in data)
      {
        var part = AsObj(partObj);
        if (part != null)
          sb.Append(ResolveMessagePart(part));
      }

      Log("[Archipelago] " + sb.ToString());
    }

    /// <summary>Send items checked inside the game client to the multiworld server.</summary>
    public void SendLocationChecks(IEnumerable<int> locationIds)
    {
      if (!IsAuthenticated)
      {
        Error("Cannot send checks yet. Waiting for server authentication handshake to complete.");
        return;
      }

      var payload = new Dictionary<string, object>();
      payload["cmd"] = "LocationChecks";
      payload["locations"] = locationIds.Cast<object>().ToList();

      SendPackets(payload);
    }

    /// <summary>Report this client's ClientStatus to the server. 10=ready, 20=playing, 30=goal complete.</summary>
    public void SendStatusUpdate(int status)
    {
      if (!IsAuthenticated)
      {
        Error(
          "Cannot send status update yet. Waiting for server authentication handshake to complete."
        );
        return;
      }

      if (status == 30)
      {
        if (_goalCompleteSent)
          return;
        _goalCompleteSent = true;
      }

      var payload = new Dictionary<string, object>();
      payload["cmd"] = "StatusUpdate";
      payload["status"] = status;
      SendPackets(payload);
    }

    private string GenerateUUID()
    {
      var bytes = new byte[8];
      _rng.NextBytes(bytes);
      var s = Convert
        .ToBase64String(bytes)
        .Replace("+", "")
        .Replace("/", "")
        .Replace("=", "")
        .ToLowerInvariant();
      return s.Length > 13 ? s.Substring(0, 13) : s;
    }

    // Ask for + subscribe to this slot's hint list right after connecting.
    public void RequestHints()
    {
      var key = "_read_hints_" + Team + "_" + Slot;

      var getPayload = new Dictionary<string, object>();
      getPayload["cmd"] = "Get";
      getPayload["keys"] = new List<object> { key };

      var notifyPayload = new Dictionary<string, object>();
      notifyPayload["cmd"] = "SetNotify";
      notifyPayload["keys"] = new List<object> { key };

      SendPackets(getPayload, notifyPayload);
    }

    private void OnRetrieved(Dictionary<string, object> packet)
    {
      var key = "_read_hints_" + Team + "_" + Slot;
      var keys = AsObj(Get(packet, "keys"));
      if (keys != null && keys.ContainsKey(key))
      {
        if (OnHintsUpdated != null)
          OnHintsUpdated(AsArr(keys[key]) ?? new List<object>());
      }
    }

    private void OnSetReply(Dictionary<string, object> packet)
    {
      var key = "_read_hints_" + Team + "_" + Slot;
      if (AsStr(Get(packet, "key")) == key)
      {
        if (OnHintsUpdated != null)
          OnHintsUpdated(AsArr(Get(packet, "value")) ?? new List<object>());
      }
    }

    // ---- logging helpers ----
    private void Log(string msg)
    {
      if (OnLog != null)
        OnLog(msg);
    }

    private void Warn(string msg)
    {
      if (OnWarn != null)
        OnWarn(msg);
    }

    private void Error(string msg)
    {
      if (OnError != null)
        OnError(msg);
    }
  }

  public class SlotInfo
  {
    public string Name;
    public string Game;
    public int Type;
  }

  public class PlayerInfo
  {
    public int Team;
    public int Slot;
    public string Alias;
    public string Name;
  }

  public class ScoutedItem
  {
    public string ItemName;
    public int ItemPlayer;
    public string LocationName;
    public int Flags;
  }
}
