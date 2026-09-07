using ArchipelagoNet;
using UnityEngine;

/// <summary>
/// Minimal on-screen connect form (host/port/slot/password) for the
/// ArchipelagoClient. Uses OnGUI so it needs no Canvas/prefab setup -
/// just add this component once (e.g. from MBMod.Awake) and it'll draw
/// itself every frame until the user connects.
///
/// This game's text pipeline (Cocos2d-derived CCText/CCFont) replaced
/// Unity's built-in Font system, so no Unity Font in this build actually
/// has glyphs loaded - GUI.Label/GUI.TextField text is invisible no
/// matter which Font we assign. To work around this, all visible text
/// here is rendered via TextRasterizer (System.Drawing bitmaps drawn as
/// textures) instead of relying on GUIStyle.font. Text fields keep using
/// GUI.TextField/GUI.PasswordField underneath (fully transparent) purely
/// to capture keyboard input/caret/selection; the visible text is a
/// TextRasterizer texture drawn on top of them.
///
/// Usage from your plugin:
///   var uiObj = new GameObject("APConnectUI");
///   UnityEngine.Object.DontDestroyOnLoad(uiObj);
///   var ui = uiObj.AddComponent&lt;APConnectUI&gt;();
///   ui.OnConnectRequested += (hostname, port, game, playerName, password) => {
///       var client = new ArchipelagoClient(hostname, port, game, playerName, password);
///       client.OnLog += msg => Log.LogInfo(msg);
///       client.OnError += msg => Log.LogError(msg);
///       client.Connect();
///   };
/// </summary>
public class APConnectUI : MonoBehaviour
{
  public delegate void ConnectRequestedHandler(
    string hostname,
    int? port,
    string game,
    string playerName,
    string password
  );
  private bool first = false;
  public event ConnectRequestedHandler OnConnectRequested;

  // Toggle with a hotkey so it doesn't sit on screen once connected.
  public KeyCode ToggleKey = KeyCode.F8;
  public bool Visible = true;

  private string _host = "ap.localhost";
  private string _port = "80";
  private string _game = "Mathbreakers";
  private string _playerName = "nyix";
  private string _password = "";
  private string _status = "";

  private Rect _windowRect = new Rect(20, 20, 340, 220);

  private const int FontScale = 2; // each glyph renders at 5x7 pixels times this
  private static readonly Color TextColor = Color.white;

  // Transparent style for the real input controls: no visible (broken)
  // glyphs, no visible caret color clash - just keyboard/caret behavior.
  private GUIStyle _invisibleFieldStyle;
  private GUIStyle _invisibleButtonStyle;
  private GUIStyle _windowStyle;
  private Texture2D _fieldBackground;

  private void Update()
  {
    if (Input.GetKeyDown(ToggleKey))
    {
      Visible = !Visible;
    }
  }

  private void EnsureStyles()
  {
    if (_invisibleFieldStyle != null)
      return;

    _fieldBackground = new Texture2D(1, 1);
    _fieldBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.35f));
    _fieldBackground.Apply();

    _invisibleFieldStyle = new GUIStyle(GUI.skin.textField);
    _invisibleFieldStyle.normal.textColor = Color.clear;
    _invisibleFieldStyle.focused.textColor = Color.clear;
    _invisibleFieldStyle.hover.textColor = Color.clear;
    _invisibleFieldStyle.normal.background = _fieldBackground;
    _invisibleFieldStyle.focused.background = _fieldBackground;

    _invisibleButtonStyle = new GUIStyle(GUI.skin.button);
    _invisibleButtonStyle.normal.textColor = Color.clear;
    _invisibleButtonStyle.hover.textColor = Color.clear;
    _invisibleButtonStyle.active.textColor = Color.clear;

    _windowStyle = new GUIStyle(GUI.skin.window);
  }

  private void OnGUI()
  {
    if (!Visible)
      return;

    try
    {
      EnsureStyles();
    }
    catch (System.Exception ex)
    {
      if (!_loggedError)
      {
        _loggedError = true;
        Debug.LogError("[APConnectUI] EnsureStyles threw: " + ex);
      }
      return;
    }

    // Pass an empty title - the title bar's own text is drawn through
    // the same broken font pipeline, so we render our own label inside
    // DrawWindow instead.
    _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, "", _windowStyle);
  }

  private bool _loggedError = false;

  private void DrawWindow(int id)
  {
    try
    {
      DrawWindowContents();
    }
    catch (System.TypeLoadException tex)
    {
      if (!_loggedError)
      {
        _loggedError = true;
        // Mono's TypeLoadException.ToString() often omits the one
        // piece of info that actually says what failed - the name
        // of the type/assembly it couldn't load - so log it directly.
        Debug.LogError(
          "[APConnectUI] DrawWindow threw TypeLoadException. TypeName='"
            + tex.TypeName
            + "'. Full: "
            + tex
        );
      }
    }
    catch (System.Exception ex)
    {
      // GUI.Window callback exceptions are easy to lose (they can
      // abort mid-frame without a clear log entry), so force this
      // into the log explicitly the first time it happens.
      if (!_loggedError)
      {
        _loggedError = true;
        Debug.LogError("[APConnectUI] DrawWindow threw: " + ex);
      }
    }
  }

  private void DrawWindowContents(int _ = 0)
  {
    DrawText(new Rect(10, 4, 300, 18), "Connect to Archipelago");

    DrawText(new Rect(10, 26, 75, 20), "Host");
    _host = DrawTextField(new Rect(90, 26, 230, 20), _host, false);

    DrawText(new Rect(10, 51, 75, 20), "Port");
    _port = DrawTextField(new Rect(90, 51, 230, 20), _port, false);

    DrawText(new Rect(10, 76, 75, 20), "Game");
    _game = DrawTextField(new Rect(90, 76, 230, 20), _game, false);

    DrawText(new Rect(10, 101, 75, 20), "Slot Name");
    _playerName = DrawTextField(new Rect(90, 101, 230, 20), _playerName, false);

    DrawText(new Rect(10, 126, 75, 20), "Password");
    _password = DrawTextField(new Rect(90, 126, 230, 20), _password, true);

    var buttonRect = new Rect(90, 156, 150, 30);
    if (GUI.Button(buttonRect, "", _invisibleButtonStyle) || first)
    {
      first = false;
      TryConnect();
    }
    DrawTextCentered(buttonRect, "Connect");

    if (!string.IsNullOrEmpty(_status))
    {
      DrawText(new Rect(10, 191, 320, 30), _status);
    }

    GUI.DragWindow(new Rect(0, 0, 10000, 20));
  }

  // ---- text-as-texture drawing helpers ----
  private void DrawText(Rect rect, string text)
  {
    var tex = TextRasterizer.GetTexture(text, FontScale, TextColor);
    var drawRect = new Rect(rect.x, rect.y, Mathf.Min(tex.width, rect.width), rect.height);
    GUI.DrawTexture(drawRect, tex, ScaleMode.ScaleToFit);
  }

  private void DrawTextCentered(Rect rect, string text)
  {
    var tex = TextRasterizer.GetTexture(text, FontScale, TextColor);
    var w = Mathf.Min(tex.width, rect.width);
    var h = Mathf.Min(tex.height, rect.height);
    var drawRect = new Rect(rect.x + (rect.width - w) / 2f, rect.y + (rect.height - h) / 2f, w, h);
    GUI.DrawTexture(drawRect, tex, ScaleMode.ScaleToFit);
  }

  /// <summary>Draws a real (invisible) GUI.TextField/PasswordField to capture
  /// input, with the current value rendered visibly on top via TextRasterizer.</summary>
  private string DrawTextField(Rect rect, string value, bool isPassword)
  {
    string newValue = isPassword
      ? GUI.PasswordField(rect, value, '*', _invisibleFieldStyle)
      : GUI.TextField(rect, value, _invisibleFieldStyle);

    var display = isPassword ? new string('*', newValue.Length) : newValue;
    var tex = TextRasterizer.GetTexture(
      string.IsNullOrEmpty(display) ? " " : display,
      FontScale,
      TextColor
    );

    var textRect = new Rect(
      rect.x + 4,
      rect.y + (rect.height - tex.height) / 2f,
      Mathf.Min(tex.width, rect.width - 8),
      tex.height
    );
    GUI.DrawTexture(textRect, tex, ScaleMode.ScaleToFit);

    return newValue;
  }

  private void TryConnect()
  {
    if (string.IsNullOrEmpty(_host))
    {
      _status = "Host is required.";
      return;
    }
    if (string.IsNullOrEmpty(_playerName))
    {
      _status = "Slot name is required.";
      return;
    }

    int? port = null;
    if (!string.IsNullOrEmpty(_port))
    {
      int parsedPort;
      if (!int.TryParse(_port, out parsedPort))
      {
        _status = "Port must be a number.";
        return;
      }
      port = parsedPort;
    }

    _status = "Connecting...";

    if (OnConnectRequested != null)
    {
      OnConnectRequested(_host, port, _game, _playerName, _password);
    }
  }

  /// <summary>Call this from your OnLog/OnError handlers to reflect connection
  /// state back into the window (marshal to the main thread first).</summary>
  public void SetStatus(string status)
  {
    _status = status;
  }
}
