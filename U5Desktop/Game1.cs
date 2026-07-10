using System;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using Ultima5Redux;
using Ultima5Redux.Dialogue;
using Ultima5Redux.Maps;
using Ultima5Redux.MapUnits;
using Ultima5Redux.MapUnits.NonPlayerCharacters;
using Ultima5Redux.MapUnits.TurnResults;
using Ultima5Redux.MapUnits.TurnResults.SpecificTurnResults;
using Ultima5Redux.References;
using Ultima5Redux.References.Maps;

namespace U5Desktop;

public class Game1 : Game
{
    private const int TILE = 16;
    private const int MapTiles = 11;     // authentic U5 viewport: odd, so the avatar sits dead-centre
    private const int AVATAR_TILE = 284; // U5 avatar-on-foot sprite

    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private Texture2D[] _tiles;
    private World _world;
    private Map _map;
    private int _scale = 4;       // internal px multiplier for 16px tiles inside the canvas
    private int _viewX, _viewY;   // visible tiles across/down
    private int _mapPx, _mapX, _mapY;   // the square map window (canvas-internal coords)
    private int _panelX, _panelW;       // the right-hand "console" panel (canvas-internal coords)

    // The authentic U5 screen (map + console) is drawn to this fixed-size offscreen
    // canvas, then blitted into whatever screen space is free. That makes the whole
    // game shrink to fit — e.g. when the keyboard rises — without ever being cropped.
    private RenderTarget2D _canvas;
    private int _canvasW, _canvasH;
    private Rectangle _gameDest;         // where the canvas lands on screen (letterboxed)
    private int _kbH;                    // keyboard band height when shown
    private int _safeL, _safeR, _safeT, _safeB; // device safe-area insets (notch / home indicator)
    private KeyboardState _prevKb;

    private Texture2D _pixel;                              // 1x1 for drawing the D-pad
    private Rectangle _btnUp, _btnDown, _btnLeft, _btnRight;
    private double _moveTimer;                             // hold-to-move repeat
    private SoundEffect _step, _bonk;                      // movement SFX
    private SoundEffectInstance _music;                   // looping background theme

    private Font _font;
    private readonly System.Collections.Generic.List<string> _log = new();
    private bool _kbShown;
    private Rectangle _kbToggle;
    private readonly System.Collections.Generic.List<(Rectangle r, char c, string label)> _keys = new();
    private char _pendingCmd;                              // directional command awaiting a direction
    private bool _onLargeMap = true;                       // false while inside a town/castle/dungeon
    private Point2D _returnLargePos;                       // overworld tile to drop back onto when exiting
    private bool _titleShown = true;                       // start on the title/menu screen
    private double _titleBlink;                            // pulse timer for the prompt
    private bool _musicOn = true;                          // settings: background music
    private readonly System.Collections.Generic.List<(Rectangle r, string label)> _menuItems = new();
    private bool _prevMousePressed;

    // NPC conversation state. The engine runs a conversation as an async task that
    // emits script items via a callback and blocks on our typed keyword responses.
    private Conversation _convo;
    private System.Threading.Tasks.Task _convoTask;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _convoOut = new();
    private bool _inConversation;
    private string _convoInput = "";

    private string _dataDir;                               // U5 game data location (for reloading on Continue)
    private static string SaveDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "u5save");
    private static bool HasSave => File.Exists(Path.Combine(SaveDir, "save.json"));

    // Standard EGA 16-colour palette.
    private static readonly Color[] Ega =
    {
        new(0, 0, 0),       new(0, 0, 0xAA),    new(0, 0xAA, 0),    new(0, 0xAA, 0xAA),
        new(0xAA, 0, 0),    new(0xAA, 0, 0xAA), new(0xAA, 0x55, 0), new(0xAA, 0xAA, 0xAA),
        new(0x55, 0x55, 0x55), new(0x55, 0x55, 0xFF), new(0x55, 0xFF, 0x55), new(0x55, 0xFF, 0xFF),
        new(0xFF, 0x55, 0x55), new(0xFF, 0x55, 0xFF), new(0xFF, 0xFF, 0x55), new(0xFF, 0xFF, 0xFF)
    };

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Ultima V (Redux engine) — desktop prototype";
    }

    protected override void Initialize()
    {
#if IOS
        // Fill the device screen in landscape.
        _graphics.SupportedOrientations = DisplayOrientation.LandscapeLeft | DisplayOrientation.LandscapeRight;
        _graphics.IsFullScreen = true;
        var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        int sw = System.Math.Max(dm.Width, dm.Height), sh = System.Math.Min(dm.Width, dm.Height);
        _graphics.PreferredBackBufferWidth = sw;
        _graphics.PreferredBackBufferHeight = sh;
#else
        _graphics.PreferredBackBufferWidth = 21 * TILE * 3;
        _graphics.PreferredBackBufferHeight = 15 * TILE * 3;
#endif
        _graphics.ApplyChanges();
        RecomputeCanvas();   // must precede base.Initialize (which calls LoadContent → creates the canvas)
        base.Initialize();
        RecomputeView();
    }

    // The fixed internal layout of the authentic U5 screen: a square map window on
    // the left (avatar dead-centre) framed by a border, and a "console" panel on the
    // right holding party / stats / command-log boxes. This never changes — it's
    // rendered to a fixed-size offscreen canvas and then scaled to the screen.
    private void RecomputeCanvas()
    {
        _scale = 6;                              // internal tiles are crisp at 6x
        _mapPx = TILE * MapTiles * _scale;       // square map side
        _viewX = _viewY = MapTiles;
        int m = _scale * 3;                      // margin (room for the gold border)
        _mapX = m;
        _mapY = m;
        int gap = m;
        _panelX = _mapX + _mapPx + gap;
        _panelW = _mapPx * 72 / 100;             // console a bit narrower than the map
        _canvasW = _panelX + _panelW + m;
        _canvasH = _mapPx + 2 * m;
    }

    // Lay out the SCREEN: fit the game canvas into the free space and place the
    // touch controls in a bezel OUTSIDE it. When the keyboard is up it claims the
    // bottom band and the game shrinks to fit above it (never cropped).
    private void RecomputeView()
    {
        UpdateSafeArea();
        // Usable region = full viewport minus the device safe-area insets, so nothing
        // (keyboard, D-pad, game) is drawn under the notch or the home indicator.
        int x0 = _safeL, y0 = _safeT;
        int w = GraphicsDevice.Viewport.Width - _safeL - _safeR;
        int h = GraphicsDevice.Viewport.Height - _safeT - _safeB;
        _kbH = (int)(h * 0.42f);                        // keyboard band height

        // The right bezel (D-pad + KEYS/HIDE button) is ALWAYS present, in both
        // states, so the D-pad stays usable while the keyboard is up.
        int b = System.Math.Max(56, h / 6);            // D-pad button size
        int pad = System.Math.Max(10, h / 30);
        int stripW = 3 * b + 2 * pad;
        int leftW = w - stripW;                        // game + keyboard live here

        // Game fills the left area, shrinking above the keyboard band when it's up.
        int gameAreaH = _kbShown ? h - _kbH : h;
        _gameDest = Fit(_canvasW, _canvasH, new Rectangle(x0, y0, leftW, gameAreaH));

        int crossLeft = x0 + w - stripW + pad;
        int blockH = 3 * b + pad + b;                  // cross + toggle button
        int top = y0 + System.Math.Max(pad, (h - blockH) / 2);
        _btnUp = new Rectangle(crossLeft + b, top, b, b);
        _btnLeft = new Rectangle(crossLeft, top + b, b, b);
        _btnRight = new Rectangle(crossLeft + 2 * b, top + b, b, b);
        _btnDown = new Rectangle(crossLeft + b, top + 2 * b, b, b);
        _kbToggle = new Rectangle(crossLeft, top + 3 * b + pad, 3 * b, b);

        // Keyboard fills the bottom band of the LEFT area only (never under the bezel).
        BuildKeyboard(x0, leftW, y0 + h);
    }

    // Query the device safe-area insets (notch / rounded corners / home indicator),
    // converted from points to back-buffer pixels. No-op off iOS.
    private void UpdateSafeArea()
    {
        _safeL = _safeR = _safeT = _safeB = 0;
#if IOS
        try
        {
            UIKit.UIWindow win = null;
            foreach (var candidate in UIKit.UIApplication.SharedApplication.Windows)
                if (candidate.IsKeyWindow) { win = candidate; break; }
            win ??= UIKit.UIApplication.SharedApplication.Windows.Length > 0
                    ? UIKit.UIApplication.SharedApplication.Windows[0] : null;
            if (win != null)
            {
                var ins = win.SafeAreaInsets;
                float sc = (float)UIKit.UIScreen.MainScreen.Scale;
                _safeL = (int)(ins.Left * sc); _safeR = (int)(ins.Right * sc);
                _safeT = (int)(ins.Top * sc); _safeB = (int)(ins.Bottom * sc);
            }
        }
        catch { /* fall back to no insets */ }
#endif
    }

    // Uniformly scale (cw x ch) to fit inside area, centred (letterboxed).
    private static Rectangle Fit(int cw, int ch, Rectangle area)
    {
        float s = System.Math.Min(area.Width / (float)cw, area.Height / (float)ch);
        int dw = (int)(cw * s), dh = (int)(ch * s);
        return new Rectangle(area.X + (area.Width - dw) / 2, area.Y + (area.Height - dh) / 2, dw, dh);
    }

    // On-screen keyboard grid across the bottom band of the left area, starting at
    // x0 (inside the safe area), width kbW, bottom edge at bottomY.
    private void BuildKeyboard(int x0, int kbW, int bottomY)
    {
        _keys.Clear();
        string[] rows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
        int cols = 10;                                   // widest row (row 1)
        int kw = kbW / cols, kh = _kbH / 4;
        int ky0 = bottomY - _kbH;
        for (int r = 0; r < rows.Length; r++)
        {
            int startX = x0 + (kbW - rows[r].Length * kw) / 2; // centre each staggered row
            for (int c = 0; c < rows[r].Length; c++)
            {
                char ch = rows[r][c];
                _keys.Add((new Rectangle(startX + c * kw, ky0 + r * kh, kw - 3, kh - 3), ch, ch.ToString()));
            }
        }
        int y4 = ky0 + 3 * kh;
        _keys.Add((new Rectangle(x0, y4, kw * 4 - 3, kh - 3), ' ', "SPACE"));
        _keys.Add((new Rectangle(x0 + kw * 4, y4, kw * 2 - 3, kh - 3), '\r', "ENTER"));
        _keys.Add((new Rectangle(x0 + kw * 6, y4, kw * 2 - 3, kh - 3), '\b', "DEL"));
        _keys.Add((new Rectangle(x0 + kw * 8, y4, kw * 2 - 3, kh - 3), (char)27, "ESC"));
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _font = new Font(GraphicsDevice);
        _canvas = new RenderTarget2D(GraphicsDevice, _canvasW, _canvasH);
        if (Environment.GetEnvironmentVariable("U5_KB") != null) _kbShown = true; // debug: keyboard-up shot
        Log("Welcome to Britannia!");
        Log("Tap KEYS for commands.");

        try
        {
            _step = Audio.Step();
            _bonk = Audio.Bonk();
            _music = Audio.MusicLoop().CreateInstance();
            _music.IsLooped = true;
            _music.Volume = 0.4f;
            _music.Play();
        }
        catch { /* audio is optional; never let it break the game */ }
        _dataDir = Environment.GetEnvironmentVariable("U5_DATA")
                   ?? Path.Combine(AppContext.BaseDirectory, "u5data");
        string dataDir = _dataDir;

        // Decode TILES.16 (U6-style LZW) into one Texture2D per 16x16 tile.
        byte[] raw = U6Lzw.Decompress(File.ReadAllBytes(Path.Combine(dataDir, "TILES.16")));
        int nTiles = raw.Length / (TILE * TILE / 2);
        _tiles = new Texture2D[nTiles];
        var px = new Color[TILE * TILE];
        for (int t = 0; t < nTiles; t++)
        {
            for (int y = 0; y < TILE; y++)
                for (int x = 0; x < TILE; x++)
                {
                    int bp = t * 128 + y * 8 + x / 2;
                    int nib = (x % 2 == 0) ? (raw[bp] >> 4) & 0xF : raw[bp] & 0xF;
                    // index 0 (black) is the transparency mask for overlay sprites
                    px[y * TILE + x] = nib == 0 ? Color.Transparent : Ega[nib];
                }
            _tiles[t] = new Texture2D(GraphicsDevice, TILE, TILE);
            _tiles[t].SetData(px);
        }

        // Fresh game (embedded INIT.GAM) on the Britannia overworld.
        _world = new World(true, dataDir, dataDir, true, true);
        _world.State.TheVirtualMap.LoadLargeMap(LargeMapLocationReferences.LargeMapType.Overworld);
        RefreshMap();

        // Runtime proof (visible even when running headless).
        Console.WriteLine($"U5PROTO: decoded {_tiles.Length} tiles from TILES.16");
        Point2D p = _map.CurrentPosition.XY;
        Console.WriteLine($"U5PROTO: overworld {_map.NumOfXTiles}x{_map.NumOfYTiles}, avatar at ({p.X},{p.Y})");
        if (Environment.GetEnvironmentVariable("U5_GRID") != null)
        {
            int halfXd = _viewX / 2, halfYd = _viewY / 2;
            for (int sy = 0; sy < _viewY; sy++)
            {
                for (int sx = 0; sx < _viewX; sx++)
                {
                    int mx = ((p.X - halfXd + sx) % _map.NumOfXTiles + _map.NumOfXTiles) % _map.NumOfXTiles;
                    int my = ((p.Y - halfYd + sy) % _map.NumOfYTiles + _map.NumOfYTiles) % _map.NumOfYTiles;
                    var tr = _map.GetTileReference(new Point2D(mx, my));
                    Console.Write($"({sx},{sy})={tr.Index}:{tr.Name}  ");
                }
                Console.WriteLine();
            }
        }
        // For headless gameplay screenshots, skip the title unless U5_TITLE is set.
        if (Environment.GetEnvironmentVariable("U5_SCREENSHOT") != null &&
            Environment.GetEnvironmentVariable("U5_TITLE") == null)
            _titleShown = false;
        Console.Out.Flush();
    }

    private bool Pressed(KeyboardState kb, Keys k) => kb.IsKeyDown(k) && _prevKb.IsKeyUp(k);

    protected override void Update(GameTime gameTime)
    {
        var kb = Keyboard.GetState();

        // Title / main-menu screen: pick New Game, Continue, or a setting.
        if (_titleShown)
        {
            _titleBlink += gameTime.ElapsedGameTime.TotalSeconds;
            if (_menuItems.Count == 0) RecomputeMenu();
            var pts = new System.Collections.Generic.List<Point>();
            foreach (var t in TouchPanel.GetState())
                if (t.State == TouchLocationState.Pressed) pts.Add(new Point((int)t.Position.X, (int)t.Position.Y));
            var ms = Mouse.GetState();
            if (ms.LeftButton == ButtonState.Pressed && !_prevMousePressed) pts.Add(new Point(ms.X, ms.Y));
            _prevMousePressed = ms.LeftButton == ButtonState.Pressed;
            if (_titleBlink > 0.3)
                foreach (var pt in pts)
                    foreach (var (r, label) in _menuItems)
                        if (r.Contains(pt)) { MenuSelect(label); break; }
            _prevKb = kb;
            base.Update(gameTime);
            return;
        }
#if !IOS
        if (kb.IsKeyDown(Keys.Escape)) Exit();
#endif

        // Pump NPC conversation output (produced on the conversation's task thread).
        while (_convoOut.TryDequeue(out var line)) Log(line);
        if (_inConversation && _convoTask != null && _convoTask.IsCompleted)
        {
            _inConversation = false; _convoInput = "";
            Log("Bye.");
        }

        // Keyboard: one step per key-down edge.
        Point2D.Direction tapped = Point2D.Direction.None;
        if (Pressed(kb, Keys.Up) || Pressed(kb, Keys.W)) tapped = Point2D.Direction.Up;
        else if (Pressed(kb, Keys.Down) || Pressed(kb, Keys.S)) tapped = Point2D.Direction.Down;
        else if (Pressed(kb, Keys.Left) || Pressed(kb, Keys.A)) tapped = Point2D.Direction.Left;
        else if (Pressed(kb, Keys.Right) || Pressed(kb, Keys.D)) tapped = Point2D.Direction.Right;

        // Physical keyboard letters (desktop) issue commands.
        for (Keys k = Keys.A; k <= Keys.Z; k++)
            if (Pressed(kb, k)) OnKey((char)('A' + (k - Keys.A)));

        // Held direction (keyboard arrows or on-screen D-pad) for repeat movement.
        Point2D.Direction held = Point2D.Direction.None;
        if (kb.IsKeyDown(Keys.Up)) held = Point2D.Direction.Up;
        else if (kb.IsKeyDown(Keys.Down)) held = Point2D.Direction.Down;
        else if (kb.IsKeyDown(Keys.Left)) held = Point2D.Direction.Left;
        else if (kb.IsKeyDown(Keys.Right)) held = Point2D.Direction.Right;

        foreach (var t in TouchPanel.GetState())
        {
            var p = t.Position;
            if (t.State == TouchLocationState.Pressed)
            {
                if (_kbToggle.Contains(p)) { _kbShown = !_kbShown; RecomputeView(); continue; }
                if (_kbShown)
                {
                    bool hitKey = false;
                    foreach (var key in _keys) if (key.r.Contains(p)) { OnKey(key.c); hitKey = true; break; }
                    if (hitKey) continue; // otherwise fall through so the D-pad still works
                }
            }
            if (t.State == TouchLocationState.Pressed || t.State == TouchLocationState.Moved)
            {
                if (_btnUp.Contains(p)) held = Point2D.Direction.Up;
                else if (_btnDown.Contains(p)) held = Point2D.Direction.Down;
                else if (_btnLeft.Contains(p)) held = Point2D.Direction.Left;
                else if (_btnRight.Contains(p)) held = Point2D.Direction.Right;
            }
        }

        if (tapped != Point2D.Direction.None) { DirectionInput(tapped); _moveTimer = 0.35; }
        else if (held != Point2D.Direction.None)
        {
            _moveTimer -= gameTime.ElapsedGameTime.TotalSeconds;
            if (_moveTimer <= 0) { DirectionInput(held); _moveTimer = 0.18; }
        }
        else _moveTimer = 0;

        _prevKb = kb;
        base.Update(gameTime);
    }

    // A direction either completes a pending command, or walks the Avatar.
    private void DirectionInput(Point2D.Direction dir)
    {
        if (_pendingCmd != '\0') { ExecuteDir(_pendingCmd, dir); _pendingCmd = '\0'; }
        else Move(dir);
    }

    private static string DirName(Point2D.Direction dir) => dir switch
    {
        Point2D.Direction.Up => "North",
        Point2D.Direction.Down => "South",
        Point2D.Direction.Left => "West",
        Point2D.Direction.Right => "East",
        _ => "?"
    };

    private static MapUnitMovement.MovementCommandDirection ToMoveDir(Point2D.Direction dir) => dir switch
    {
        Point2D.Direction.Up => MapUnitMovement.MovementCommandDirection.North,
        Point2D.Direction.Down => MapUnitMovement.MovementCommandDirection.South,
        Point2D.Direction.Left => MapUnitMovement.MovementCommandDirection.West,
        Point2D.Direction.Right => MapUnitMovement.MovementCommandDirection.East,
        _ => MapUnitMovement.MovementCommandDirection.None
    };

    private void Move(Point2D.Direction dir)
    {
        Point2D before = _map.CurrentPosition.XY;
        bool wasLarge = _onLargeMap;
        if (wasLarge) _returnLargePos = new Point2D(before.X, before.Y); // where to return on exit
        var tr = new TurnResults();
        try { _world.TryToMoveNonCombatMap(dir, false, false, tr); }
        catch { /* ignore movement edge cases */ }
        HandleResults(tr);                       // may exit a building; refreshes _map
        if (_onLargeMap) TryMoongate();          // may teleport
        Point2D after = _map.CurrentPosition.XY;
        bool blocked = wasLarge == _onLargeMap && before.X == after.X && before.Y == after.Y;
        Log(">" + DirName(dir) + (blocked ? " - Blocked!" : ""));
        if (blocked) _bonk?.Play();
        else _step?.Play();
    }

    // Greedy word-wrap a line to at most cpl characters wide.
    private static void WrapInto(string line, int cpl, System.Collections.Generic.List<string> outList)
    {
        if (line.Length <= cpl) { outList.Add(line); return; }
        var words = line.Split(' ');
        string cur = "";
        foreach (var word in words)
        {
            string w = word;
            while (w.Length > cpl) { outList.Add(w.Substring(0, cpl)); w = w.Substring(cpl); }
            if (cur.Length == 0) cur = w;
            else if (cur.Length + 1 + w.Length <= cpl) cur += " " + w;
            else { outList.Add(cur); cur = w; }
        }
        if (cur.Length > 0) outList.Add(cur);
    }

    private void Log(string s)
    {
        foreach (var line in s.Replace("\r", "").Split('\n'))
            if (!string.IsNullOrWhiteSpace(line)) _log.Add(line.Trim());
        while (_log.Count > 40) _log.RemoveAt(0);
    }

    // Keep our map reference in sync with the engine (it swaps CurrentMap on load).
    private void RefreshMap()
    {
        _map = _world.State.TheVirtualMap.CurrentMap;
        _onLargeMap = _map is LargeMap;
    }

    // The engine is headless: actions push "turn results" that the front-end must
    // act on — printing messages, but also loading maps on enter/exit/teleport.
    private void HandleResults(TurnResults tr)
    {
        while (tr.HasTurnResult)
        {
            var r = tr.PopTurnResult();
            if (r is TeleportNewLocation tp) DoTeleport(tp);
            else if (r is NpcTalkInteraction talk) StartConversation(talk.Npc);
            else if (r is IOutputString os) Log(os.OutputString);
            else if (r.TheTurnResultType == TurnResult.TurnResultType.OfferToExitScreen) ExitBuilding();
        }
        RefreshMap();
    }

    // --- Main menu / settings ---
    private void RecomputeMenu()
    {
        _menuItems.Clear();
        int w = GraphicsDevice.Viewport.Width, h = GraphicsDevice.Viewport.Height;
        int iw = System.Math.Min(w * 3 / 5, h * 3 / 2), ih = System.Math.Max(44, h / 9), gap = System.Math.Max(8, h / 50);
        var labels = new System.Collections.Generic.List<string> { "New Game" };
        if (HasSave) labels.Add("Continue");
        labels.Add(_musicOn ? "Music: On" : "Music: Off");
        int cx = w / 2, y = h * 50 / 100;
        foreach (var lbl in labels)
        {
            _menuItems.Add((new Rectangle(cx - iw / 2, y, iw, ih), lbl));
            y += ih + gap;
        }
    }

    private void MenuSelect(string label)
    {
        if (label == "New Game") { NewGame(); _titleShown = false; }
        else if (label == "Continue") { if (LoadSavedGame()) _titleShown = false; else Log("No save to load."); }
        else if (label.StartsWith("Music")) { _musicOn = !_musicOn; ApplyMusic(); RecomputeMenu(); }
    }

    private void ApplyMusic()
    {
        try { if (_musicOn) _music?.Resume(); else _music?.Pause(); } catch { }
    }

    private void NewGame()
    {
        try
        {
            _world = new World(true, _dataDir, _dataDir, true, true);
            _world.State.TheVirtualMap.LoadLargeMap(LargeMapLocationReferences.LargeMapType.Overworld);
            RefreshMap();
            _log.Clear();
            Log("Welcome to Britannia!");
            Log("Tap KEYS for commands.");
        }
        catch { }
    }

    // --- Save / load ---
    private void DoSave()
    {
        try
        {
            Directory.CreateDirectory(SaveDir);
            if (_world.State.SaveGame(out string err, SaveDir)) Log("Game saved.");
            else Log("Save failed: " + err);
        }
        catch { Log("Save failed."); }
    }

    private bool LoadSavedGame()
    {
        try
        {
            _world = new World(false, SaveDir, _dataDir, true, false);
            RefreshMap();
            return true;
        }
        catch (Exception e) { Console.WriteLine("U5LOAD failed: " + e); return false; }
    }

    // --- NPC conversation ---
    private void StartConversation(NonPlayerCharacter npc)
    {
        try
        {
            _convo = _world.CreateConversationAndBegin(npc.NpcState, OnConvoItem);
            _inConversation = true;
            _convoInput = "";
            if (!_kbShown) { _kbShown = true; RecomputeView(); } // raise the keyboard to type keywords
            Log("You meet " + npc.FriendlyName + ".");
            _convoTask = _convo.BeginConversation();              // runs async; emits via OnConvoItem
        }
        catch { Log("They do not respond."); _inConversation = false; }
    }

    // Called (on the conversation's task thread) each time the NPC emits a script
    // item — turn it into text and queue it for the game thread to log.
    private void OnConvoItem(Conversation c)
    {
        try
        {
            var item = c.DequeueFromOutputBuffer();
            if (item == null) return;
            string text = item.Command switch
            {
                Ultima5Redux.References.Dialogue.TalkScript.TalkCommand.PlainString => item.StringData,
                Ultima5Redux.References.Dialogue.TalkScript.TalkCommand.AvatarsName => "Avatar",
                Ultima5Redux.References.Dialogue.TalkScript.TalkCommand.NewLine => "\n",
                _ => ""     // control commands (Pause/KeyWait/etc.) render nothing
            };
            if (!string.IsNullOrEmpty(text)) _convoOut.Enqueue(text);
        }
        catch { /* ignore a malformed item */ }
    }

    // A typed key while a conversation is active builds/sends a keyword.
    private void ConvoKey(char c)
    {
        if (c == (char)27) { EndConversation(); return; }        // ESC = leave
        if (c == '\r')                                            // ENTER = send the keyword
        {
            string kw = _convoInput.Trim();
            Log("> " + (kw.Length == 0 ? "(pause)" : kw));
            try { _convo?.AddUserResponse(kw); } catch { }
            _convoInput = "";
            return;
        }
        if (c == '\b') { if (_convoInput.Length > 0) _convoInput = _convoInput.Substring(0, _convoInput.Length - 1); return; }
        if (c >= ' ' && c < (char)127 && _convoInput.Length < 16) _convoInput += char.ToLowerInvariant(c);
    }

    private void EndConversation()
    {
        try { _convo?.AddUserResponse("bye"); } catch { }
        _inConversation = false;
        _convoInput = "";
        Log("Bye.");
    }

    private void DoTeleport(TeleportNewLocation tp)
    {
        try
        {
            if (tp.TheTeleportType == TeleportNewLocation.TeleportType.EnterSmallMap)
            {
                var single = GameReferences.Instance.SmallMapRef.GetSingleMapByLocation(tp.TheLocation, tp.TeleportFloor);
                _world.State.TheVirtualMap.LoadSmallMap(single, tp.TeleportPosition);
                Log("Enter " + GameReferences.Instance.SmallMapRef.GetLocationName(tp.TheLocation));
            }
        }
        catch { Log("Can't go there."); }
    }

    // Walking off the edge of a town/castle drops you back on the overworld tile
    // you entered from.
    private void ExitBuilding()
    {
        try
        {
            _world.State.TheVirtualMap.LoadLargeMap(
                LargeMapLocationReferences.LargeMapType.Overworld, _returnLargePos);
            Log("Leaving.");
        }
        catch { }
    }

    // Stepping onto an active moongate at night teleports across Britannia (or to
    // the Underworld). Gated by the engine (night + buried moonstone), so this is a
    // no-op most of the time — and never throws.
    private void TryMoongate()
    {
        try
        {
            if (!_world.IsAvatarOnActiveMoongate()) return;
            var p3 = _world.GetMoongateTeleportLocation();
            var type = p3.Z == 0 ? LargeMapLocationReferences.LargeMapType.Overworld
                                 : LargeMapLocationReferences.LargeMapType.Underworld;
            _world.State.TheVirtualMap.LoadLargeMap(type, new Point2D(p3.X, p3.Y));
            RefreshMap();
            Log("* Moongate! *");
        }
        catch { }
    }

    // Immediate (non-directional) commands + starting directional ones.
    private void OnKey(char c)
    {
        if (_inConversation) { ConvoKey(c); return; }    // typing keywords, not commands
        if (c == (char)27) // ESC: cancel a pending command first, otherwise close the keyboard
        {
            if (_pendingCmd != '\0') { _pendingCmd = '\0'; Log("Cancelled."); return; }
            _kbShown = false; RecomputeView(); return;
        }
        c = char.ToUpperInvariant(c);
        var tr = new TurnResults();
        switch (c)
        {
            case ' ': // Pass — wait a turn (the authentic U5 SPACE command)
                _pendingCmd = '\0';
                _world.TryToPassTime(tr); HandleResults(tr);
                Log(">Pass");
                break;
            case 'Z': // ztats
                var recs = _world.State.CharacterRecords.GetActiveCharacterRecords();
                Log("-- Party --");
                foreach (var r in recs) Log($"{r.Name}  HP {r.Stats.CurrentHp}/{r.Stats.MaximumHp}  Lv{r.Stats.Level}");
                break;
            case 'E': // enter town/castle at current tile
                if (_onLargeMap)
                    _returnLargePos = new Point2D(_map.CurrentPosition.X, _map.CurrentPosition.Y);
                _world.TryToEnterBuilding(_map.CurrentPosition.XY, out _, tr);
                HandleResults(tr);
                break;
            case 'K': // Klimb ladders / grates (up or down from the current tile)
                _pendingCmd = '\0';
                _world.TryToKlimb(out _, tr); HandleResults(tr);
                break;
            case 'L': _pendingCmd = 'L'; Log("Look-"); break;
            case 'O': _pendingCmd = 'O'; Log("Open-"); break;
            case 'S': _pendingCmd = 'S'; Log("Search-"); break;
            case 'G': _pendingCmd = 'G'; Log("Get-"); break;
            case 'T': _pendingCmd = 'T'; Log("Talk-"); break;
            case 'Q': _pendingCmd = '\0'; DoSave(); break;   // Quit & Save (saves; keep playing)
            default: Log($"{c}: not yet."); break;
        }
        // Keep the keyboard open — the D-pad stays usable, and directional commands
        // (L/O/S/G) can now be aimed with the D-pad without the keyboard vanishing.
        // Dismiss the keyboard explicitly with HIDE or ESC.
    }

    private void ExecuteDir(char cmd, Point2D.Direction dir)
    {
        int dx = dir == Point2D.Direction.Left ? -1 : dir == Point2D.Direction.Right ? 1 : 0;
        int dy = dir == Point2D.Direction.Up ? -1 : dir == Point2D.Direction.Down ? 1 : 0;
        var xy = new Point2D(_map.CurrentPosition.X + dx, _map.CurrentPosition.Y + dy);
        var tr = new TurnResults();
        try
        {
            switch (cmd)
            {
                case 'L': _world.TryToLook(xy, out _, tr); break;
                case 'O': _world.TryToOpenAThing(xy, out _, tr); break;
                case 'S': _world.TryToSearch(xy, out _, tr); break;
                case 'G': _world.TryToGetAThing(xy, out _, out _, tr, dir); break;
                case 'T': _world.TryToTalk(ToMoveDir(dir), tr); break;
            }
            HandleResults(tr);
        }
        catch { Log("Nothing there."); }
    }

    private bool _shotSaved;

    protected override void Draw(GameTime gameTime)
    {
        RenderCanvas();   // draw the authentic U5 screen into the offscreen canvas

        string shotPath = Environment.GetEnvironmentVariable("U5_SCREENSHOT");
        if (shotPath != null && !_shotSaved && _map != null)
        {
            int w = _graphics.PreferredBackBufferWidth, h = _graphics.PreferredBackBufferHeight;
            using var rt = new RenderTarget2D(GraphicsDevice, w, h);
            GraphicsDevice.SetRenderTarget(rt);
            Compose();
            GraphicsDevice.SetRenderTarget(null);
            using (var fs = File.Create(shotPath)) rt.SaveAsPng(fs, w, h);
            _shotSaved = true;
            Console.WriteLine("U5PROTO: screenshot saved to " + shotPath);
            Console.Out.Flush();
#if !IOS
            Exit();
#endif
            return;
        }
        Compose();
        base.Draw(gameTime);
    }

    // Render the map + console panel to the fixed-size offscreen canvas.
    private void RenderCanvas()
    {
        GraphicsDevice.SetRenderTarget(_canvas);
        GraphicsDevice.Clear(Color.Black);
        if (_map != null)
        {
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            // A bad game state (mid-transition, unexpected tile) must never crash the
            // app — draw what we can and swallow the rest for this frame.
            try
            {
                int halfX = _viewX / 2, halfY = _viewY / 2;
                Point2D pos = _map.CurrentPosition.XY;
                int nx = _map.NumOfXTiles, ny = _map.NumOfYTiles;

                for (int sy = 0; sy < _viewY; sy++)
                    for (int sx = 0; sx < _viewX; sx++)
                    {
                        int rx = pos.X - halfX + sx, ry = pos.Y - halfY + sy;
                        // Large maps wrap around the globe; small maps (towns/dungeons)
                        // don't — anything off their edge is void (stays black).
                        int mx, my;
                        if (_onLargeMap) { mx = ((rx % nx) + nx) % nx; my = ((ry % ny) + ny) % ny; }
                        else { if (rx < 0 || rx >= nx || ry < 0 || ry >= ny) continue; mx = rx; my = ry; }

                        var tr = _map.GetTileReference(new Point2D(mx, my));
                        var rect = new Rectangle(_mapX + sx * TILE * _scale, _mapY + sy * TILE * _scale,
                                                 TILE * _scale, TILE * _scale);
                        // Overlay tiles (forest/castle/village/etc.) draw their ground
                        // tile first so transparent (index-0) pixels show terrain.
                        int ground = tr.FlatTileSubstitutionIndex >= 0 ? tr.FlatTileSubstitutionIndex
                                     : (tr.IsUpright ? 5 /*Grass*/ : -1);
                        if (ground >= 0 && ground < _tiles.Length)
                            _spriteBatch.Draw(_tiles[ground], rect, Color.White);
                        if (tr.Index >= 0 && tr.Index < _tiles.Length)
                            _spriteBatch.Draw(_tiles[tr.Index], rect, Color.White);
                    }

                // NPCs, monsters, and other map units — sprites drawn over the terrain
                // (their transparent pixels let the ground show through).
                int camX = pos.X - halfX, camY = pos.Y - halfY;
                Avatar avatarUnit = _map.CurrentMapUnits.TheAvatar;
                foreach (MapUnit u in _map.CurrentMapUnits.AllActiveMapUnits)
                {
                    if (ReferenceEquals(u, avatarUnit)) continue;      // avatar drawn centred below
                    var mp = u.MapUnitPosition;
                    if (mp.Floor != _map.CurrentPosition.Floor) continue;
                    int ux, uy;
                    if (_onLargeMap) { ux = ((mp.X - camX) % nx + nx) % nx; uy = ((mp.Y - camY) % ny + ny) % ny; }
                    else { ux = mp.X - camX; uy = mp.Y - camY; }
                    if (ux < 0 || ux >= _viewX || uy < 0 || uy >= _viewY) continue;
                    int uidx = u.KeyTileReference?.Index ?? -1;
                    if (uidx < 0 || uidx >= _tiles.Length) continue;
                    _spriteBatch.Draw(_tiles[uidx],
                        new Rectangle(_mapX + ux * TILE * _scale, _mapY + uy * TILE * _scale,
                                      TILE * _scale, TILE * _scale), Color.White);
                }

                if (AVATAR_TILE < _tiles.Length)
                    _spriteBatch.Draw(_tiles[AVATAR_TILE],
                        new Rectangle(_mapX + halfX * TILE * _scale, _mapY + halfY * TILE * _scale,
                                      TILE * _scale, TILE * _scale),
                        Color.White);

                DrawMapBorder();
                DrawPanel();
            }
            catch { /* skip this frame's remaining draws rather than crash */ }
            _spriteBatch.End();
        }
        GraphicsDevice.SetRenderTarget(null);
    }

    // Blit the canvas into the free screen space and draw the bezel controls
    // (or the title screen before the game begins).
    private void Compose()
    {
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_titleShown) DrawTitle();
        else
        {
            _spriteBatch.Draw(_canvas, _gameDest, Color.White);
            DrawControls();
        }
        _spriteBatch.End();
    }

    private void DrawCentered(string s, int y, int scale, Color c, int w)
        => _font.Draw(_spriteBatch, s, w / 2 - s.Length * Font.Glyph * scale / 2, y, scale, c);

    private void DrawTitle()
    {
        int w = GraphicsDevice.Viewport.Width, h = GraphicsDevice.Viewport.Height;
        var gold = new Color(214, 176, 74);
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, w, h), new Color(16, 20, 54)); // night sky

        // starfield (deterministic)
        int sd = System.Math.Max(1, h / 380);
        for (int i = 0; i < 70; i++)
        {
            int sx = (i * 97 + 41) % w, sy = (i * 173 + 23) % h;
            _spriteBatch.Draw(_pixel, new Rectangle(sx, sy, sd * (1 + i % 2), sd * (1 + i % 2)),
                              i % 3 == 0 ? gold : Color.White);
        }

        // gold double frame
        int m = System.Math.Max(12, h / 22), t = System.Math.Max(3, h / 240);
        Frame(m, m, w - 2 * m, h - 2 * m, t, gold);
        Frame(m + 3 * t, m + 3 * t, w - 2 * (m + 3 * t), h - 2 * (m + 3 * t), t, new Color(120, 92, 30));

        // titles
        int big = System.Math.Max(4, h / 52), sub = System.Math.Max(2, h / 150);
        DrawCentered("ULTIMA V", h * 18 / 100, big, gold, w);
        DrawCentered("Warriors of Destiny", h * 18 / 100 + Font.Glyph * big + h / 40, sub, Color.White, w);

        // menu buttons
        if (_menuItems.Count == 0) RecomputeMenu();
        int mfs = System.Math.Max(2, h / 150);
        double a = 0.7 + 0.3 * System.Math.Sin(_titleBlink * 3.0);
        foreach (var (r, label) in _menuItems)
        {
            _spriteBatch.Draw(_pixel, r, new Color(28, 32, 66));
            Frame(r.X, r.Y, r.Width, r.Height, System.Math.Max(2, h / 320), gold);
            var c = new Color((int)(255 * a), (int)(228 * a), (int)(150 * a));
            DrawCentered(label, r.Y + r.Height / 2 - Font.Glyph * mfs / 2, mfs, c, w);
        }
    }

    // Double-line frame around the map window, echoing the original's ornate border.
    private void DrawMapBorder()
    {
        int t = System.Math.Max(2, _scale / 2);
        int g = 2 * t + System.Math.Max(2, _scale / 2);
        Frame(_mapX - g, _mapY - g, _mapPx + 2 * g, _mapPx + 2 * g, t, new Color(200, 160, 60)); // outer gold line
        Frame(_mapX - t, _mapY - t, _mapPx + 2 * t, _mapPx + 2 * t, t, new Color(200, 160, 60)); // inner gold line
    }

    private void Frame(int x, int y, int w, int hh, int t, Color c)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, w, t), c);
        _spriteBatch.Draw(_pixel, new Rectangle(x, y + hh - t, w, t), c);
        _spriteBatch.Draw(_pixel, new Rectangle(x, y, t, hh), c);
        _spriteBatch.Draw(_pixel, new Rectangle(x + w - t, y, t, hh), c);
    }

    // Draw an EGA-style double-line frame (like the original's boxed panels).
    private void DrawBox(Rectangle r, Color c)
    {
        int t = System.Math.Max(2, GraphicsDevice.Viewport.Height / 360);
        _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, t), c);
        _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Y, t, r.Height), c);
        _spriteBatch.Draw(_pixel, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    private void DrawPanel()
    {
        int h = GraphicsDevice.Viewport.Height;
        int fs = System.Math.Max(2, h / 240);                 // font scale
        int lineH = Font.Glyph * fs + 6;
        int gap = System.Math.Max(4, h / 120);                // between boxes
        int inpad = System.Math.Max(5, h / 90);               // inside-box padding
        int ow = _panelW;                                      // outer box width
        int glyph = Font.Glyph * fs;
        var frame = new Color(90, 105, 180);                  // EGA-ish blue frame
        var gold = new Color(255, 215, 90);
        var green = new Color(120, 255, 120);
        int y = _mapY;

        // Fetch the party once (drives the party-box height).
        System.Collections.Generic.List<Ultima5Redux.PlayerCharacters.PlayerCharacterRecord> party = null;
        try { party = _world.State.CharacterRecords.GetActiveCharacterRecords(); } catch { }
        int rows = System.Math.Max(1, party?.Count ?? 1);

        // --- Party box (top): name left, current HP right ---
        int partyH = rows * lineH + 2 * inpad;
        DrawBox(new Rectangle(_panelX, y, ow, partyH), frame);
        int ix = _panelX + inpad, ir = _panelX + ow - inpad, iy = y + inpad;
        if (party != null)
            foreach (var r in party)
            {
                string name = string.IsNullOrWhiteSpace(r.Name) ? "Avatar" : r.Name;
                _font.Draw(_spriteBatch, name, ix, iy, fs, Color.White);
                string hp = r.Stats.CurrentHp.ToString();
                _font.Draw(_spriteBatch, hp, ir - hp.Length * glyph, iy, fs,
                           r.Stats.CurrentHp <= r.Stats.MaximumHp / 4 ? new Color(255, 90, 90) : green);
                iy += lineH;
            }
        y += partyH + gap;

        // --- Stats box (middle): Food / Gold / Date ---
        int statsH = 2 * lineH + 2 * inpad;
        DrawBox(new Rectangle(_panelX, y, ow, statsH), frame);
        iy = y + inpad;
        try
        {
            string f = "F:" + _world.State.PlayerInventory.Food;
            string g = "G:" + _world.State.PlayerInventory.Gold;
            _font.Draw(_spriteBatch, f, ix, iy, fs, gold);
            _font.Draw(_spriteBatch, g, ir - g.Length * glyph, iy, fs, gold);
            iy += lineH;
            var tod = _world.State.TheTimeOfDay;
            _font.Draw(_spriteBatch, $"{tod.Month}-{tod.Day}-{tod.Year}", ix, iy, fs, Color.White);
        }
        catch { }
        y += statsH + gap;

        // --- Command scroll box (bottom): fills the rest of the console ---
        int logBottom = _canvasH - _mapY;      // symmetric margin at the bottom
        int logH = System.Math.Max(lineH * 2 + 2 * inpad, logBottom - y);
        DrawBox(new Rectangle(_panelX, y, ow, logH), frame);
        int maxLines = System.Math.Max(1, (logH - 2 * inpad) / lineH);
        int cpl = System.Math.Max(4, (ow - 2 * inpad) / glyph);      // chars per line
        var wrapped = new System.Collections.Generic.List<string>();
        foreach (var line in _log) WrapInto(line, cpl, wrapped);
        if (_inConversation) WrapInto("> " + _convoInput + "_", cpl, wrapped); // live keyword prompt
        int start = System.Math.Max(0, wrapped.Count - maxLines);
        iy = y + inpad;
        for (int i = start; i < wrapped.Count; i++)
        {
            _font.Draw(_spriteBatch, wrapped[i], ix, iy, fs, green);
            iy += lineH;
        }
    }

    // Bezel controls, drawn in SCREEN space OUTSIDE the game canvas: the D-pad +
    // KEYS button when idle, or the on-screen keyboard when it's up.
    private void DrawControls()
    {
        int fs = System.Math.Max(2, GraphicsDevice.Viewport.Height / 300);

        // D-pad cross (always visible so you can move even while typing).
        var face = new Color(255, 255, 255, 70);
        var edge = new Color(255, 255, 255, 150);
        foreach (var r in new[] { _btnUp, _btnDown, _btnLeft, _btnRight })
        {
            _spriteBatch.Draw(_pixel, r, face);
            int t = System.Math.Max(1, r.Width / 20);
            _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, t), edge);
            _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Bottom - t, r.Width, t), edge);
            _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Y, t, r.Height), edge);
            _spriteBatch.Draw(_pixel, new Rectangle(r.Right - t, r.Y, t, r.Height), edge);
        }

        // KEYS/HIDE toggle button (glows amber when the keyboard is up).
        string tl = _kbShown ? "HIDE" : "KEYS";
        _spriteBatch.Draw(_pixel, _kbToggle, _kbShown ? new Color(120, 90, 30, 235) : new Color(60, 60, 80, 220));
        _spriteBatch.Draw(_pixel, new Rectangle(_kbToggle.X, _kbToggle.Y, _kbToggle.Width, 2), new Color(255, 215, 90));
        _font.Draw(_spriteBatch, tl, _kbToggle.X + _kbToggle.Width / 2 - tl.Length * Font.Glyph * fs / 2,
                   _kbToggle.Y + _kbToggle.Height / 2 - Font.Glyph * fs / 2, fs, Color.White);

        // On-screen keyboard (bottom band of the left area).
        if (_kbShown)
            foreach (var (r, _, label) in _keys)
            {
                _spriteBatch.Draw(_pixel, r, new Color(30, 30, 40, 255));
                _spriteBatch.Draw(_pixel, new Rectangle(r.X, r.Y, r.Width, 2), new Color(255, 255, 255, 90));
                int tx = r.X + r.Width / 2 - label.Length * Font.Glyph * fs / 2;
                int ty = r.Y + r.Height / 2 - Font.Glyph * fs / 2;
                _font.Draw(_spriteBatch, label, tx, ty, fs, Color.White);
            }
    }
}
