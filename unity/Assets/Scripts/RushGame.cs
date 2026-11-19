using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// Rail Rush Workout: body-controlled endless runner. Everything is created from code,
/// so the scene only needs to exist (the bootstrap adds this component automatically).
public class RushGame : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<RushGame>() == null) new GameObject("RushGame").AddComponent<RushGame>();
    }

    class Session { public string name, sub, glyph; public Color col; public float mins, speed, density; }
    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    readonly Session[] sessions =
    {
        new Session { name = "Quick Dash", sub = "3 min · easy", glyph = "3", col = Hex("#30d158"), mins = 3, speed = 11, density = 0.8f },
        new Session { name = "Subway Sprint", sub = "5 min · medium", glyph = "5", col = Hex("#0a84ff"), mins = 5, speed = 13, density = 1f },
        new Session { name = "Cardio Burner", sub = "10 min · hard", glyph = "10", col = Hex("#ff375f"), mins = 10, speed = 15, density = 1.25f },
        new Session { name = "Endless Run", sub = "30 min · free run", glyph = "∞", col = Hex("#bf5af2"), mins = 30, speed = 12, density = 1f },
    };

    enum Mode { Menu, Setup, Countdown, Run, Results }
    Mode mode = Mode.Menu;
    Session cur;
    RushWorld world; Synth synth; Camera cam;
    readonly MotionControls motion = new MotionControls();
    bool useBody = true;      // menu toggle
    bool runUsesBody;         // this run (swipe fallback can switch it off)

    // run state
    float t, dist, x, y, vy, lean, stumble, stopT, countdown, nextRowZ, effortShown;
    int lane, coinsGot, dodges, jumps, ducks, hits;
    bool duck, touchDuckHeld; float touchDuckT;

    // UI
    RectTransform safe, menu, hud, results, cast, setup;
    Image progFill, cueRing; Text coinsTx, timeTx, cueTx, toastTx, statusTx, setupTx, setupSub, effortTx;
    RectTransform pip; readonly List<Image> bones = new List<Image>(); Image pipBg; Image effortFill;
    Text rCal, rCoins, rDist, rDodge, rMoves; Toggle bodyToggle; Image toggleBg; RectTransform toggleKnob;
    float toastT;

    static readonly int[,] Skeleton = { { 1, 2 }, { 1, 3 }, { 3, 5 }, { 2, 4 }, { 4, 6 }, { 1, 7 }, { 2, 8 }, { 7, 8 }, { 7, 9 }, { 9, 11 }, { 8, 10 }, { 10, 12 }, { 0, 1 }, { 0, 2 } };

    void Start()
    {
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.orientation = ScreenOrientation.LandscapeLeft;

        world = new GameObject("World").AddComponent<RushWorld>();
        world.Build();
        cam = Camera.main;
        if (cam == null) { cam = new GameObject("Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.clearFlags = CameraClearFlags.Skybox; cam.fieldOfView = 60; cam.nearClipPlane = 0.2f; cam.farClipPlane = 220;
        cam.transform.position = new Vector3(0, 4.6f, -7.5f); cam.transform.rotation = Quaternion.Euler(18, 0, 0);
        if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        synth = new GameObject("Synth").AddComponent<Synth>();
        BuildUI();
        ShowMenu();
    }

    // =====================================================================
    // UI
    // =====================================================================
    void BuildUI()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
        if (FindAnyObjectByType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        safe = UIKit.Rect("Safe", canvasGo.transform); ApplySafeArea();

        // ---------------- HUD ----------------
        hud = UIKit.Stretch(UIKit.Rect("HUD", safe));
        var close = UIKit.Button(hud, new Color(0, 0, 0, 0.45f), () => EndRun(false));
        close.image.sprite = UIKit.Circle;
        UIKit.Place(close.image.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(76, 76));
        UIKit.Stretch(UIKit.Label(close.transform, "✕", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);

        var bar = UIKit.Box(hud, new Color(0, 0, 0, 0.4f));
        UIKit.Place(bar.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-40, -42), new Vector2(980, 36));
        progFill = UIKit.Box(bar.transform, Hex("#c7f464"));
        var pf = progFill.rectTransform; pf.anchorMin = Vector2.zero; pf.anchorMax = new Vector2(0, 1); pf.pivot = new Vector2(0, 0.5f); pf.offsetMin = new Vector2(4, 4); pf.offsetMax = new Vector2(0, -4);

        var coinPill = UIKit.Box(hud, new Color(0, 0, 0, 0.45f));
        UIKit.Place(coinPill.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-200, -24), new Vector2(170, 76));
        var coinIc = UIKit.Box(coinPill.transform, Hex("#ffc300"), UIKit.Circle);
        UIKit.Place(coinIc.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(40, 40));
        coinsTx = UIKit.Label(coinPill.transform, "0", 36, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
        UIKit.Stretch(coinsTx.rectTransform, 60, 0, 20, 0);
        var timePill = UIKit.Box(hud, new Color(0, 0, 0, 0.45f));
        UIKit.Place(timePill.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(160, 76));
        timeTx = UIKit.Label(timePill.transform, "0:00", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Stretch(timeTx.rectTransform);

        cueRing = UIKit.Box(hud, new Color(0.05f, 0.25f, 0.12f, 0.9f), UIKit.Circle);
        UIKit.Place(cueRing.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -110), new Vector2(170, 170));
        var outline = cueRing.gameObject.AddComponent<Outline>(); outline.effectColor = Hex("#4ade80"); outline.effectDistance = new Vector2(5, 5);
        cueTx = UIKit.Label(cueRing.transform, "DUCK", 40, Hex("#4ade80"), TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Stretch(cueTx.rectTransform);

        toastTx = UIKit.Label(hud, "", 96, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Place(toastTx.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1200, 200));
        toastTx.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4, -4);
        statusTx = UIKit.Label(hud, "", 54, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Place(statusTx.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(1400, 160));
        statusTx.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -3);

        // body preview (skeleton) + effort meter
        pipBg = UIKit.Box(hud, new Color(0, 0, 0, 0.55f));
        pip = pipBg.rectTransform;
        UIKit.Place(pip, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(220, 290));
        for (int i = 0; i < Skeleton.GetLength(0); i++) bones.Add(UIKit.Line(pip, Hex("#4ade80"), 9));
        var eb = UIKit.Box(hud, new Color(0, 0, 0, 0.45f));
        UIKit.Place(eb.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 28), new Vector2(360, 70));
        effortFill = UIKit.Box(eb.transform, Hex("#ff9f0a"));
        var ef = effortFill.rectTransform; ef.anchorMin = Vector2.zero; ef.anchorMax = new Vector2(0, 1); ef.pivot = new Vector2(0, 0.5f); ef.offsetMin = new Vector2(4, 4); ef.offsetMax = new Vector2(0, -4);
        effortTx = UIKit.Label(eb.transform, "RUN!", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Stretch(effortTx.rectTransform);

        // ---------------- Setup (camera placement + calibration) ----------------
        setup = UIKit.Stretch(UIKit.Rect("Setup", safe));
        var sbg = UIKit.Box(setup, new Color(0.04f, 0.04f, 0.08f, 0.82f)); UIKit.Stretch(sbg.rectTransform, -200, -200, -200, -200);
        setupTx = UIKit.Label(setup, "", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Place(setupTx.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-170, 90), new Vector2(1200, 120));
        setupSub = UIKit.Label(setup, "", 34, new Color(1, 1, 1, 0.75f), TextAnchor.UpperCenter);
        UIKit.Place(setupSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1), new Vector2(-170, 20), new Vector2(1100, 260));
        var skip = UIKit.Button(setup, new Color(1, 1, 1, 0.14f), () => { PoseSource.Stop(); runUsesBody = false; BeginCountdown(); });
        UIKit.Place(skip.image.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-170, 60), new Vector2(520, 96));
        UIKit.Stretch(UIKit.Label(skip.transform, "Play with swipes instead", 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);

        // ---------------- Menu ----------------
        menu = UIKit.Stretch(UIKit.Rect("Menu", safe));
        var mbg = UIKit.Box(menu, new Color(0.04f, 0.04f, 0.08f, 0.72f)); UIKit.Stretch(mbg.rectTransform, -200, -200, -200, -200);
        var icon = UIKit.Box(menu, Hex("#5e5ce6"));
        UIKit.Place(icon.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -60), new Vector2(140, 140));
        UIKit.Stretch(UIKit.Label(icon.transform, "R", 86, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
        var title = UIKit.Label(menu, "Rail Rush Workout", 68, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
        UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -220), new Vector2(700, 100));
        var sub = UIKit.Label(menu, "Run in place. Lean to switch lanes. Jump. Squat to duck.", 32, new Color(1, 1, 1, 0.7f), TextAnchor.UpperLeft);
        UIKit.Place(sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -320), new Vector2(680, 120));

        var castRow = UIKit.Row(menu, Hex("#636366"), "TV", "Cast to TV", "Smart View · Chromecast · AirPlay", ShowCast);
        UIKit.Place(castRow.image.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 190), new Vector2(760, 110));
        var tg = UIKit.Box(menu, new Color(1, 1, 1, 0.10f));
        UIKit.Place(tg.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 60), new Vector2(760, 110));
        UIKit.Stretch(UIKit.Label(tg.transform, "Body control (camera)", 34, Color.white).rectTransform, 30, 0, 160, 0);
        toggleBg = UIKit.Box(tg.transform, Hex("#34c759"), UIKit.Circle);
        UIKit.Place(toggleBg.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(110, 64));
        var knob = UIKit.Box(toggleBg.transform, Color.white, UIKit.Circle); toggleKnob = knob.rectTransform;
        UIKit.Place(toggleKnob, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-5, 0), new Vector2(54, 54));
        var tb = tg.gameObject.AddComponent<Button>(); tb.onClick.AddListener(() => { useBody = !useBody; RefreshToggle(); });

        for (int i = 0; i < sessions.Length; i++)
        {
            var s = sessions[i];
            var row = UIKit.Row(menu, s.col, s.glyph, s.name, s.sub, () => StartSession(s));
            UIKit.Place(row.image.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-80, -60 - i * 136), new Vector2(740, 118));
        }

        // ---------------- Results ----------------
        results = UIKit.Stretch(UIKit.Rect("Results", safe));
        var rbg = UIKit.Box(results, new Color(0.04f, 0.04f, 0.08f, 0.85f)); UIKit.Stretch(rbg.rectTransform, -200, -200, -200, -200);
        var done = UIKit.Label(results, "Workout complete", 36, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter);
        UIKit.Place(done.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(800, 60));
        rCal = UIKit.Label(results, "0", 150, Hex("#ffd60a"), TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Place(rCal.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(800, 180));
        var kc = UIKit.Label(results, "estimated kcal", 32, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter);
        UIKit.Place(kc.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -310), new Vector2(800, 50));
        Text Stat(int i, string label)
        {
            var b = UIKit.Box(results, new Color(1, 1, 1, 0.1f));
            UIKit.Place(b.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-465 + i * 310, -40), new Vector2(280, 150));
            var v = UIKit.Label(b.transform, "0", 56, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold); UIKit.Stretch(v.rectTransform, 0, 10, 0, 50);
            UIKit.Stretch(UIKit.Label(b.transform, label, 26, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter).rectTransform, 0, 100, 0, 8);
            return v;
        }
        rCoins = Stat(0, "Coins"); rDist = Stat(1, "Distance"); rDodge = Stat(2, "Lane switches"); rMoves = Stat(3, "Jumps + ducks");
        var again = UIKit.Button(results, Hex("#5e5ce6"), () => StartSession(cur));
        UIKit.Place(again.image.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-220, 70), new Vector2(400, 110));
        UIKit.Stretch(UIKit.Label(again.transform, "Run again", 38, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
        var home = UIKit.Button(results, new Color(1, 1, 1, 0.14f), ShowMenu);
        UIKit.Place(home.image.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(220, 70), new Vector2(400, 110));
        UIKit.Stretch(UIKit.Label(home.transform, "Home", 38, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);

        // ---------------- Cast sheet ----------------
        cast = UIKit.Stretch(UIKit.Rect("Cast", safe));
        var cbg = UIKit.Box(cast, new Color(0.04f, 0.04f, 0.08f, 0.96f)); UIKit.Stretch(cbg.rectTransform, -200, -200, -200, -200);
        var ct = UIKit.Label(cast, "Cast to TV", 64, Color.white, TextAnchor.UpperLeft, FontStyle.Bold);
        UIKit.Place(ct.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -50), new Vector2(900, 90));
        string[,] tips =
        {
            { "1", "Samsung · LG · Hisense · Sony · TCL", "Android: swipe down → Smart View / Screen Cast → your TV" },
            { "2", "Chromecast · Google TV", "Android: Google Home app → your TV → Cast my screen" },
            { "3", "iPhone → AirPlay TV / Apple TV", "Control Center → Screen Mirroring → your TV" },
            { "4", "Any other TV", "Chromecast / Miracast dongle, or USB-C → HDMI cable" },
        };
        Color[] tc = { Hex("#0a84ff"), Hex("#30d158"), Hex("#8e8e93"), Hex("#ff9f0a") };
        for (int i = 0; i < 4; i++)
        {
            var r = UIKit.Row(cast, tc[i], tips[i, 0], tips[i, 1], tips[i, 2], () => { });
            UIKit.Place(r.image.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -170 - i * 130), new Vector2(1100, 112));
        }
        var open = UIKit.Button(cast, Hex("#5e5ce6"), OpenCastSettings);
        UIKit.Place(open.image.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-80, 200), new Vector2(520, 110));
        UIKit.Stretch(UIKit.Label(open.transform, "Open cast settings", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
        var cdone = UIKit.Button(cast, new Color(1, 1, 1, 0.14f), () => cast.gameObject.SetActive(false));
        UIKit.Place(cdone.image.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-80, 70), new Vector2(520, 110));
        UIKit.Stretch(UIKit.Label(cdone.transform, "Done", 36, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).rectTransform);
        cast.gameObject.SetActive(false);
    }

    Rect lastSafe;
    void ApplySafeArea()
    {
        var s = Screen.safeArea; lastSafe = s;
        if (Screen.width <= 0 || Screen.height <= 0) return;
        safe.anchorMin = new Vector2(s.xMin / Screen.width, s.yMin / Screen.height);
        safe.anchorMax = new Vector2(s.xMax / Screen.width, s.yMax / Screen.height);
        safe.offsetMin = safe.offsetMax = Vector2.zero;
    }

    void RefreshToggle()
    {
        toggleBg.color = useBody ? Hex("#34c759") : Hex("#3a3a44");
        UIKit.Place(toggleKnob, new Vector2(useBody ? 1 : 0, 0.5f), new Vector2(useBody ? 1 : 0, 0.5f), new Vector2(useBody ? -5 : 5, 0), new Vector2(54, 54));
    }

    void ShowCast() { cast.gameObject.SetActive(true); cast.SetAsLastSibling(); }

    void OpenCastSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            var act = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");
            foreach (var a in new[] { "android.settings.CAST_SETTINGS", "android.settings.WIFI_DISPLAY_SETTINGS" })
            {
                try { act.Call("startActivity", new AndroidJavaObject("android.content.Intent", a)); return; } catch { }
            }
        }
        catch { }
#endif
        Toast("Use Control Center → Screen Mirroring", 2f);
    }

    void Show(RectTransform panel)
    {
        foreach (var p in new[] { menu, hud, results, setup }) p.gameObject.SetActive(p == panel || (p == hud && panel == setup));
    }

    void ShowMenu()
    {
        mode = Mode.Menu; synth.Playing = false; PoseSource.Stop();
        world.Clear(); Show(menu); RefreshToggle();
    }

    // =====================================================================
    // Flow
    // =====================================================================
    void StartSession(Session s)
    {
        cur = s; world.Clear();
        t = dist = x = y = vy = lean = stumble = stopT = 0; lane = 0; coinsGot = dodges = jumps = ducks = hits = 0; duck = false;
        nextRowZ = 45f; toastT = 0; toastTx.text = "";
        motion.Reset();
        synth.Playing = true;
        runUsesBody = useBody && PoseSource.Supported;
        if (runUsesBody)
        {
            camStarted = false; setupT = 0;
            mode = Mode.Setup; Show(setup);
        }
        else BeginCountdown();
    }

    void BeginCountdown() { mode = Mode.Countdown; countdown = 3.4f; Show(hud); }

    void EndRun(bool finished)
    {
        if (!finished) { ShowMenu(); return; }
        mode = Mode.Results; synth.Playing = false; PoseSource.Stop();
        int kcal = Mathf.RoundToInt(t / 60f * (runUsesBody ? 10f : 4f) + (jumps + ducks) * 0.4f);
        rCal.text = kcal.ToString(); rCoins.text = coinsGot.ToString(); rDist.text = Mathf.RoundToInt(dist) + " m";
        rDodge.text = dodges.ToString(); rMoves.text = (jumps + ducks).ToString();
        Show(results);
    }

    void Toast(string s, float dur = 0.8f) { toastTx.text = s; toastT = dur; }

    // =====================================================================
    // Update
    // =====================================================================
    void Update()
    {
        if (Screen.safeArea != lastSafe) ApplySafeArea();
        float dt = Mathf.Min(Time.deltaTime, 0.05f), now = Time.realtimeSinceStartup;
        if (runUsesBody) { PoseSource.Poll(); motion.Update(now); DrawSkeleton(); }
        pip.gameObject.SetActive(runUsesBody && (mode == Mode.Run || mode == Mode.Countdown || mode == Mode.Setup));
        effortFill.transform.parent.gameObject.SetActive(runUsesBody && mode == Mode.Run);

        if (toastT > 0) { toastT -= dt; if (toastT <= 0) toastTx.text = ""; }

        switch (mode)
        {
            case Mode.Menu: case Mode.Results: Idle(dt); break;
            case Mode.Setup: UpdateSetup(dt); break;
            case Mode.Countdown:
                Idle(dt);
                countdown -= dt;
                statusTx.text = countdown > 0.4f ? Mathf.CeilToInt(countdown - 0.4f).ToString() : "RUN!";
                cueRing.gameObject.SetActive(false);
                if (countdown <= 0) { mode = Mode.Run; statusTx.text = ""; }
                break;
            case Mode.Run: UpdateRun(dt); break;
        }
    }

    void Idle(float dt)
    {
        world.ScrollStatic(dt * 3f);
        world.PoseRunner(0, 0, 0, 3f, false, false, 0, dt);
        FollowCamera(0, dt);
    }

    bool camStarted; float setupT;
    void UpdateSetup(float dt)
    {
        Idle(dt); setupT += dt;
        cueRing.gameObject.SetActive(false); statusTx.text = "";
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            if (!askedPerm) { askedPerm = true; UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera); }
            setupTx.text = "Allow camera access";
            setupSub.text = "The camera tracks your body. Nothing is recorded or uploaded.\nIf you denied it: Settings → Apps → Rail Rush → Permissions → Camera.";
            return;
        }
#endif
        if (!camStarted) { camStarted = true; PoseSource.Start(); }
        int st = PoseSource.State;
        string diag = $"\n\nCamera: {(st > 0 ? "on" : st < 0 ? "blocked" : "starting…")} · frames {PoseSource.FrameCount}";
        if (st < 0) { setupTx.text = "Camera not available"; setupSub.text = "Allow camera access in Settings → Rail Rush, or play with swipes." + diag; return; }
        if (st > 0 && PoseSource.FrameCount == 0 && setupT > 6f) { setupTx.text = "Camera is on but no picture"; setupSub.text = "Close other apps using the camera and try again." + diag; return; }
        switch (motion.State)
        {
            case MotionControls.Phase.NoBody:
                setupTx.text = "Step back into view";
                setupSub.text = "Prop your phone at waist height and stand about 2 m away\nso your head, hips and knees are all visible." + diag;
                break;
            case MotionControls.Phase.Calibrating:
                setupTx.text = "Stand still… " + Mathf.RoundToInt(Mathf.Clamp01(motion.CalibrationProgress) * 100) + "%";
                setupSub.text = "Arms by your side. This sets your centre lane and standing height." + diag;
                break;
            case MotionControls.Phase.Ready:
                BeginCountdown(); break;
        }
    }

    void UpdateRun(float dt)
    {
        float total = cur.mins * 60f;
        t += dt;
        if (t >= total) { EndRun(true); return; }

        // ---------- inputs ----------
        int wantLane = lane; bool jumpNow = false;
        if (runUsesBody)
        {
            if (motion.LostFor > 1.2f) { statusTx.text = "Step back into view"; }
            else statusTx.text = "";
            wantLane = motion.Lane;
            jumpNow = motion.JumpPressed;
            duck = motion.Ducking;
        }
        else
        {
            ReadTouch(ref wantLane, ref jumpNow);
            if (touchDuckT > 0) { touchDuckT -= dt; duck = true; } else duck = false;
        }

        // ---------- lane change (blocked if a train is alongside) ----------
        wantLane = Mathf.Clamp(wantLane, -1, 1);
        if (wantLane != lane)
        {
            int step = wantLane > lane ? 1 : -1, next = lane + step;
            if (TrainAlongside(next)) { if (!runUsesBody) world.PoseRunner(x, y, step * 0.6f, 0, false, false, 0.4f, 0); }
            else { lane = next; dodges++; synth.Whoosh(); }
        }
        float targetX = lane * RushWorld.LaneW;
        float prevX = x; x = Mathf.MoveTowards(x, targetX, dt * 16f);
        lean = Mathf.Lerp(lean, Mathf.Clamp((x - prevX) / Mathf.Max(dt, 0.001f) / 12f, -1, 1), dt * 10f);

        // ---------- jump / gravity ----------
        if (jumpNow && y <= 0.001f && !duck) { vy = 10.5f; jumps++; synth.Jump(); }
        if (duck && y > 0) vy = Mathf.Min(vy, -18f);
        if (duck && !wasDuck) ducks++;
        wasDuck = duck;
        vy -= 28f * dt; y = Mathf.Max(0, y + vy * dt); if (y <= 0) vy = 0;

        // ---------- speed: you must actually run in place ----------
        float effort = runUsesBody ? motion.Effort : 1f;
        effortShown = Mathf.Lerp(effortShown, effort, dt * 6f);
        effortFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(effortShown), 1);
        effortTx.text = effortShown < 0.25f ? "RUN IN PLACE!" : effortShown < 0.6f ? "FASTER!" : "GREAT PACE";
        float ramp = 1f + 0.3f * (t / total);
        float speed = cur.speed * ramp * (runUsesBody ? Mathf.Lerp(0.3f, 1.05f, effort) : 1f);
        if (stopT > 0) { stopT -= dt; speed = 0; }
        stumble = Mathf.MoveTowards(stumble, 0, dt * 2f);
        float dz = speed * dt; dist += dz;
        world.ScrollStatic(dz);

        // ---------- obstacles ----------
        nextRowZ -= dz;
        if (nextRowZ < RushWorld.SpawnZ) { SpawnRow(nextRowZ); nextRowZ += Mathf.Lerp(30f, 20f, (cur.density - 0.8f) / 0.5f) + Random.Range(0f, 8f); }

        string cue = null; float cueDist = 1e9f;
        for (int i = world.obstacles.Count - 1; i >= 0; i--)
        {
            var o = world.obstacles[i];
            o.z -= dz;
            if (o.flyT > 0)
            {
                o.flyT += dt;
                o.tr.position = new Vector3(o.lane * RushWorld.LaneW + o.flyT * 6f, o.flyT * 8f - o.flyT * o.flyT * 10f, o.z + o.flyT * 10f);
                o.tr.rotation = Quaternion.Euler(o.flyT * 400f, o.flyT * 200f, 0);
            }
            else o.tr.position = new Vector3(o.lane * RushWorld.LaneW, 0, o.z);

            if (o.Back < -12f) { Destroy(o.tr.gameObject); world.obstacles.RemoveAt(i); continue; }
            if (o.hit) continue;

            bool sameLane = Mathf.Abs(o.lane * RushWorld.LaneW - x) < 1.2f;
            // upcoming cue in the lane the player is (heading) in
            if (o.lane == lane && o.Front > 0.5f && speed > 0.1f)
            {
                float tt = o.Front / Mathf.Max(speed, 6f);
                if (tt < 1.7f && o.Front < cueDist) { cueDist = o.Front; cue = CueFor(o); }
            }
            if (!sameLane || o.Front > 0.45f || o.Back < -0.45f) continue;

            switch (o.kind)
            {
                case RushWorld.Obstacle.Kind.Low:
                    if (y < 0.75f) Crash(o, false); break;
                case RushWorld.Obstacle.Kind.High:
                    if (!duck) Crash(o, false); break;
                case RushWorld.Obstacle.Kind.Train:
                    Crash(o, true); break;
            }
        }
        cueRing.gameObject.SetActive(cue != null);
        if (cue != null) cueTx.text = cue;

        // ---------- coins ----------
        for (int i = world.coins.Count - 1; i >= 0; i--)
        {
            var c = world.coins[i];
            c.position += new Vector3(0, 0, -dz);
            c.rotation = Quaternion.Euler(90, t * 200f, 0);
            if (c.position.z < -6f) { world.RecycleCoin(i); continue; }
            if (Mathf.Abs(c.position.x - x) < 0.9f && Mathf.Abs(c.position.z) < 0.7f && Mathf.Abs(c.position.y - (y + 1f)) < 1.3f)
            { coinsGot++; synth.Coin(); world.RecycleCoin(i); }
        }

        world.PoseRunner(x, y, lean, speed, duck && y <= 0.01f, y > 0.01f, stumble, dt);
        FollowCamera(x, dt);

        // ---------- HUD ----------
        progFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(t / total), 1);
        coinsTx.text = coinsGot.ToString();
        float rem = total - t; timeTx.text = $"{(int)(rem / 60)}:{(int)(rem % 60):00}";
    }
    bool wasDuck, askedPerm;

    string CueFor(RushWorld.Obstacle o)
    {
        switch (o.kind)
        {
            case RushWorld.Obstacle.Kind.Low: return "JUMP";
            case RushWorld.Obstacle.Kind.High: return "DUCK";
            default:
                int free = FreeLaneNear(o.z);
                return free < lane ? "◀ MOVE" : "MOVE ▶";
        }
    }

    int FreeLaneNear(float z)
    {
        foreach (int d in new[] { -1, 1, -2, 2 })
        {
            int l = lane + d; if (l < -1 || l > 1) continue;
            bool blocked = false;
            foreach (var o in world.obstacles)
                if (o.lane == l && o.kind == RushWorld.Obstacle.Kind.Train && o.Front < z + 4 && o.Back > z - 4) blocked = true;
            if (!blocked) return l;
        }
        return lane;
    }

    bool TrainAlongside(int l)
    {
        foreach (var o in world.obstacles)
            if (!o.hit && o.lane == l && o.kind == RushWorld.Obstacle.Kind.Train && o.Front < 1.2f && o.Back > -1.2f) return true;
        return false;
    }

    void Crash(RushWorld.Obstacle o, bool train)
    {
        hits++; synth.Hit(); stumble = 1f;
        Handheld.Vibrate();
        if (train)
        {
            // stop at the train's nose, then sidestep to the nearest free lane — never run through it
            stopT = 0.7f; o.hit = false;
            float push = o.Front - 0.5f; // move the world so the runner stands in front of the train
            foreach (var ob in world.obstacles) ob.z -= push;
            int free = FreeLaneNear(o.z);
            if (free != lane) { lane = free; }
            Toast("Ouch! Move!");
        }
        else
        {
            o.hit = true; o.flyT = 0.001f; stopT = 0.35f;
            Toast("Ouch!");
        }
    }

    void SpawnRow(float z)
    {
        // At least one lane is always passable, and trains never block all three.
        int pattern = Random.Range(0, 10);
        var lanes = new List<int> { -1, 0, 1 };
        for (int i = 0; i < 3; i++) { int j = Random.Range(i, 3); (lanes[i], lanes[j]) = (lanes[j], lanes[i]); }
        int safeLane = lanes[2];
        if (pattern < 3)
        {
            float len = Random.Range(14f, 26f);
            world.SpawnTrain(lanes[0], z + len / 2, len);
            if (cur.density > 1f && Random.value < 0.4f) world.SpawnTrain(lanes[1], z + len / 2 + 6, Random.Range(14f, 22f));
        }
        else if (pattern < 6)
        {
            bool high = Random.value < 0.5f;
            world.SpawnBarrier(lanes[0], z, high);
            if (Random.value < 0.5f * cur.density) world.SpawnBarrier(lanes[1], z, !high);
        }
        else if (pattern < 8)
        {
            // a "wall" of barriers across two lanes, the third has coins
            bool high = Random.value < 0.5f;
            world.SpawnBarrier(lanes[0], z, high); world.SpawnBarrier(lanes[1], z, high);
        }
        else
        {
            world.SpawnBarrier(lanes[0], z, Random.value < 0.5f);
        }
        for (int i = 0; i < 7; i++) world.SpawnCoin(safeLane, z - 6 + i * 2f);
    }

    void FollowCamera(float px, float dt)
    {
        var target = new Vector3(px * 0.65f, 4.4f + y * 0.35f, -7.6f);
        cam.transform.position = Vector3.Lerp(cam.transform.position, target, 1 - Mathf.Exp(-dt * 6f));
        var look = new Vector3(px * 0.5f, 1.6f, 6f);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - cam.transform.position), 1 - Mathf.Exp(-dt * 6f));
    }

    // touch swipes (fallback / no camera)
    Vector2 touchStart; bool touching;
    void ReadTouch(ref int wantLane, ref bool jumpNow)
    {
        Vector2 d = Vector2.zero; bool ended = false;
        if (Input.touchCount > 0)
        {
            var tc = Input.GetTouch(0);
            if (tc.phase == TouchPhase.Began) { touchStart = tc.position; touching = true; }
            if (touching && (tc.phase == TouchPhase.Moved || tc.phase == TouchPhase.Ended)) { d = tc.position - touchStart; if (d.magnitude > Screen.dpi * 0.25f + 20) { ended = true; touching = false; } }
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) { wantLane = lane - 1; }
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) { wantLane = lane + 1; }
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space)) jumpNow = true;
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) touchDuckT = 0.8f;
        if (!ended) return;
        if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) wantLane = lane + (d.x > 0 ? 1 : -1);
        else if (d.y > 0) jumpNow = true; else touchDuckT = 0.8f;
    }

    void DrawSkeleton()
    {
        if (!pip.gameObject.activeSelf) return;
        var size = pip.rect.size;
        Vector2 P(int i) { var p = PoseSource.Pos[i]; return new Vector2((1 - p.x) * size.x, (1 - p.y) * size.y); } // mirrored like a selfie
        bool ok = motion.LostFor < 0.6f;
        for (int i = 0; i < bones.Count; i++)
        {
            int a = Skeleton[i, 0], b = Skeleton[i, 1];
            bool vis = ok && PoseSource.Conf[a] > 0.4f && PoseSource.Conf[b] > 0.4f;
            bones[i].enabled = vis;
            if (vis) UIKit.SetLine(bones[i], P(a), P(b));
        }
        pipBg.color = motion.State == MotionControls.Phase.Ready ? new Color(0, 0, 0, 0.55f) : new Color(0.4f, 0.05f, 0.05f, 0.6f);
    }
}
