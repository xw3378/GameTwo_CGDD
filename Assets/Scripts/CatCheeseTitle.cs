// @role Title screen: shows the game name/blurb, starts the game via button or Space/Enter.
// @calls NbSceneSelect

// CatCheeseTitle.cs — 游戏标题页（运行时自装配，不改场景文件）
//
// 内容取自 "CGDD Game World Conceptualization"：游戏名 Stray Rat、创作者 Maile & Xinyuan、
// 概念一句话（扮一只流浪老鼠在公寓走廊觅食、躲猫）。
// 文案全部英文 —— HUD 用的 Kindly Crunch 字体没有中文字形（写中文会渲染成空白，且不报错）。
//
// 开始方式（requirement 里的 "a button to start the game (or other methods)"）：
//   ① 点 START GAME 按钮（鼠标左键按在按钮矩形内）
//   ② 按 SPACE / ENTER
// 都不走 EventSystem —— 本项目只启用新 Input System，旧 StandaloneInputModule 会失效；
// 而运行时自建 EventSystem 还得给 InputSystemUIInputModule 配 actions，太脆。
// 这里直接用 RectTransformUtility.RectangleContainsScreenPoint + Mouse/Keyboard.current，零依赖。
//
// 标题页期间：Time.timeScale = 0（猫停住、判定不推进）+ 把玩家的 AcceptInput 关掉 + 每帧解锁光标，
// 所以「人还没开始，猫先走过来把玩家抓了」不会发生；也不依赖两个 Bootstrap 的先后顺序。
// 注意「玩家不能动」用的是 AcceptInput 开关，不是 timeScale —— 新玩家读 unscaledDeltaTime，
// 世界冻着它照样能走，两件事互不连坐。
//
// 只在本次 Play 第一次进入 SampleScene 时出现；按 R 重开一局不再弹（static _shownOnce）。

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal class CatCheeseTitle : MonoBehaviour
{
    // 配色沿用工程现成的 UI 方案：按钮 #BF733A、强调 #FBD765。
    // 白盒场景里亮字压不住，所以用暖黑全屏底把场景挡掉（顺带也挡住了右上角的 HUD）。
    static readonly Color Accent = new Color(0.984f, 0.843f, 0.396f, 1f);   // #FBD765
    static readonly Color Body = new Color(1f, 1f, 1f, 0.92f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.60f);
    static readonly Color BtnIdle = new Color(0.749f, 0.451f, 0.227f, 1f);  // #BF733A
    static readonly Color BtnHover = new Color(0.878f, 0.561f, 0.310f, 1f);
    static readonly Color Backdrop = new Color(0.078f, 0.051f, 0.031f, 0.94f);
    static readonly Color32 OutlineColor = new Color32(28, 16, 6, 255);

    const string GameName = "STRAY RAT";
    const string Tagline = "a residential block, after dark";
    // 文案里只出现字母 / 数字 / 空格 / 句号逗号斜杠 —— 这套字体（Kindly Crunch）没有完整 ASCII，
    // 破折号、波浪号、中点之类的装饰符号会渲染成空白且不报错。
    const string Blurb =
        "Three places, ten levels each.  A residential block, a backyard patrol, a kitchen at night.\n" +
        "Carry the food home before the cats or the torch finds you.  A bite costs 10 health.\n" +
        "Pick your district after you press start.  Nothing is locked.";
    const string Creators = "by Maile & Xinyuan";
    const string ButtonLabel = "START GAME";
    const string Hint = "click START GAME  (or anywhere)      or press any key";

    [Header("字号")]
    public float titleSize = 170f;
    public float taglineSize = 46f;
    public float blurbSize = 40f;
    public float creatorsSize = 44f;
    public float buttonLabelSize = 48f;
    public float hintSize = 32f;

    [Header("布局")]
    public float buttonWidth = 420f;
    public float buttonHeight = 110f;
    public int sortingOrder = 200;          // 压过 HUD(50) 和结算层(100)
    public float inputDelay = 0.45f;        // 防误触：刚进来先不响应输入

    public float autoStartAfter = 12f;      // 没人操作就自己开始（0 = 永不自动开始）

    static bool _shownOnce;
    static CatCheeseTitle _instance;

    /// 标题页是不是还挡在前面。
    /// NbGame 靠它区分「世界是被标题页冻住的」和「世界本来就该静止」——
    /// 分不清这两件事，就会把"没人点开始"误诊成"玩家走不动"。
    internal static bool Showing { get; private set; }

    RectTransform _buttonRect;
    Image _buttonImage;
    CanvasGroup _group;
    Sprite _white;
    float _bornAt;
    bool _starting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _shownOnce = false;
        _instance = null;
        Showing = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => Install(scene);
        Scene active = SceneManager.GetActiveScene();
        if (active.isLoaded) Install(active);
    }

    static void Install(Scene scene)
    {
        if (scene.name != "SampleScene") return;
        if (_shownOnce) return;             // 按 R 重开一局：直接开始，不再拦一道标题页
        if (_instance != null) return;

        _shownOnce = true;
        _instance = new GameObject("CatCheeseTitle (auto)").AddComponent<CatCheeseTitle>();
        _instance.Build();
    }

    void Build()
    {
        _bornAt = Time.unscaledTime;
        Showing = true;
        Time.timeScale = 0f;                // 猫停住、判定不推进（标题页期间世界静止）
        FreezePlayer();

        GameObject canvasGo = new GameObject("Title Canvas (auto)", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 1f;

        _white = MakeWhiteSprite();

        // —— 全屏底（也顺便把场景和右上角 HUD 挡住）——
        RectTransform bg = NewRect("Backdrop", canvasGo.transform);
        bg.anchorMin = Vector2.zero;
        bg.anchorMax = Vector2.one;
        bg.offsetMin = Vector2.zero;
        bg.offsetMax = Vector2.zero;
        Image dim = bg.gameObject.AddComponent<Image>();
        dim.sprite = _white;
        dim.type = Image.Type.Simple;
        dim.color = Backdrop;
        dim.raycastTarget = false;

        TMP_FontAsset font = ResolveFont();

        // —— 游戏名 ——
        TextMeshProUGUI title = MakeText("Title", canvasGo.transform, font, titleSize, Accent, TextAlignmentOptions.Center);
        title.text = GameName;
        title.characterSpacing = 6f;
        title.rectTransform.sizeDelta = new Vector2(1800f, titleSize * 1.4f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 268f);

        // —— 一句话定位 ——
        TextMeshProUGUI tagline = MakeText("Tagline", canvasGo.transform, font, taglineSize, Body, TextAlignmentOptions.Center);
        tagline.text = Tagline;
        tagline.rectTransform.sizeDelta = new Vector2(1800f, taglineSize * 2f);
        tagline.rectTransform.anchoredPosition = new Vector2(0f, 150f);

        // —— 分隔线 ——
        RectTransform rule = NewRect("Rule", canvasGo.transform);
        rule.sizeDelta = new Vector2(760f, 4f);
        rule.anchoredPosition = new Vector2(0f, 96f);
        Image ruleImg = rule.gameObject.AddComponent<Image>();
        ruleImg.sprite = _white;
        ruleImg.type = Image.Type.Simple;
        ruleImg.color = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
        ruleImg.raycastTarget = false;

        // —— 玩法简介（basic information）——
        TextMeshProUGUI blurb = MakeText("Blurb", canvasGo.transform, font, blurbSize, Body, TextAlignmentOptions.Center);
        blurb.text = Blurb;
        blurb.lineSpacing = 14f;
        blurb.rectTransform.sizeDelta = new Vector2(1500f, blurbSize * 5f);
        blurb.rectTransform.anchoredPosition = new Vector2(0f, -8f);

        // —— 创作者 ——
        TextMeshProUGUI creators = MakeText("Creators", canvasGo.transform, font, creatorsSize, Accent, TextAlignmentOptions.Center);
        creators.text = Creators;
        creators.rectTransform.sizeDelta = new Vector2(1500f, creatorsSize * 2f);
        creators.rectTransform.anchoredPosition = new Vector2(0f, -186f);

        // —— START 按钮 ——
        _buttonRect = NewRect("StartButton", canvasGo.transform);
        _buttonRect.sizeDelta = new Vector2(buttonWidth, buttonHeight);
        _buttonRect.anchoredPosition = new Vector2(0f, -318f);
        _buttonImage = _buttonRect.gameObject.AddComponent<Image>();
        _buttonImage.sprite = _white;
        _buttonImage.type = Image.Type.Simple;
        _buttonImage.color = BtnIdle;
        _buttonImage.raycastTarget = false;

        TextMeshProUGUI btnText = MakeText("StartLabel", _buttonRect, font, buttonLabelSize, Color.white, TextAlignmentOptions.Center);
        btnText.text = ButtonLabel;
        btnText.rectTransform.anchorMin = Vector2.zero;
        btnText.rectTransform.anchorMax = Vector2.one;
        btnText.rectTransform.offsetMin = Vector2.zero;
        btnText.rectTransform.offsetMax = Vector2.zero;

        // —— 底部提示（"or other methods" 里的按键方式）——
        TextMeshProUGUI hint = MakeText("Hint", canvasGo.transform, font, hintSize, Dim, TextAlignmentOptions.Center);
        hint.text = Hint;
        hint.rectTransform.sizeDelta = new Vector2(1600f, hintSize * 2f);
        hint.rectTransform.anchoredPosition = new Vector2(0f, -442f);

        Debug.LogFormat("[CatCheese] 标题页已显示：{0} / {1} / {2}（点 START GAME 或按 SPACE / ENTER 开始）",
            GameName, Tagline, Creators);
    }

    void Update()
    {
        if (_starting) return;

        // 钉住：不管 FirstPersonMove 的 Start 有没有先跑过，标题页期间都别让玩家动、别让光标被锁、
        // 别让世界推进（三个 Bootstrap 的先后顺序是不保证的，所以每帧重申一次）
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        FreezePlayer();

        Mouse mouse = Mouse.current;
        if (mouse != null && _buttonImage != null)
        {
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_buttonRect, mouse.position.ReadValue());
            _buttonImage.color = over ? BtnHover : BtnIdle;
        }

        if (Time.unscaledTime - _bornAt < inputDelay) return;   // 防误触

        // 判定比原来宽得多：**点屏幕任何地方**、**按任何键**都算开始。
        // 以前只认"点在按钮矩形里"或 SPACE/ENTER —— 差几个像素就"点了没反应"，
        // 而这时世界是 timeScale=0 冻着的，玩家看到的正是"画面在、怎么按都不动"。
        bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
        Keyboard kb = Keyboard.current;
        bool pressed = kb != null && kb.anyKey.wasPressedThisFrame;

        if (clicked || pressed) { StartGame(); return; }

        // 兜底：一直没人动就自己开始。
        // 没有这一条时，只要标题页没被关掉，世界就永远冻着 —— 而屏幕上看不出"是标题页没关"。
        if (autoStartAfter > 0f && Time.unscaledTime - _bornAt > autoStartAfter)
        {
            Debug.LogFormat("[CatCheese] 标题页停留 {0:F0} 秒没有任何输入，自动开始（免得世界一直冻在 timeScale=0）。",
                autoStartAfter);
            StartGame();
        }
    }

    void StartGame()
    {
        _starting = true;
        Showing = false;
        Debug.Log("[CatCheese] 标题页收起 —— 进入场景选择。");

        // **这里故意不交还操作权。** 接下来是场景选择页，玩家在这一屏不该能走；
        // 操作权在"选中场景"那一刻交（NbGame.LoadLevel 会设 AcceptInput = true）。
        // 旧版在这里直接打开，结果选择页浮在上面、底下的人还能跑 —— 会走到场景边缘去。
        if (NbPlayer.I != null) NbPlayer.I.AcceptInput = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Time.timeScale = 1f;
        NbProgress.ClearAll();

        NbSceneSelect.Show();
        StartCoroutine(FadeOut());
    }

    IEnumerator FadeOut()
    {
        float d = 0.25f, t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            if (_group != null) _group.alpha = Mathf.Clamp01(1f - t / d);
            yield return null;
        }
        _instance = null;
        Destroy(gameObject);
    }

    void FreezePlayer()
    {
        // 只关"能不能操作"这一个开关，不碰 timeScale、也不 disable 任何组件。
        // 新玩家（NbPlayer）的移动读的是 unscaledDeltaTime，所以"世界冻着"和"人不能动"
        // 是两件独立的事 —— 这样标题页永远不会顺带把玩家永久锁死。
        if (NbPlayer.I != null) NbPlayer.I.AcceptInput = false;
    }

    TextMeshProUGUI MakeText(string goName, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
    {
        RectTransform rt = NewRect(goName, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        ApplyOutline(t, OutlineColor, 0.18f);
        return t;
    }

    /// TMP 描边只能这样加：启用 OUTLINE_ON 关键字 + 写材质属性。
    /// 只设 text.outlineWidth 无效（字体资源里的材质默认没开这个关键字），加 Outline/Shadow 组件更没用 ——
    /// TMP 根本不走 uGUI 的 mesh effect 通道，而且一声不响。
    static void ApplyOutline(TextMeshProUGUI t, Color32 color, float width)
    {
        Material m = t.fontMaterial;
        if (m == null || !m.HasProperty("_OutlineWidth")) return;
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat("_OutlineWidth", width);
        m.SetColor("_OutlineColor", color);
    }

    static TMP_FontAsset ResolveFont()
    {
        HudFontRef r = Resources.Load<HudFontRef>("GameProps/HudFont");
        if (r != null && r.font != null) return r.font;

        Debug.LogWarning("[CatCheese] 取不到 Resources/GameProps/HudFont 里的字体引用，标题页退回 TMP 默认字体。");
        return TMP_Settings.defaultFontAsset;
    }

    static RectTransform NewRect(string goName, Transform parent)
    {
        GameObject go = new GameObject(goName, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        return rt;
    }

    static Sprite MakeWhiteSprite()
    {
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        Sprite s = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    void OnDestroy()
    {
        // 这一页没了就不再冻世界。少这一行，只要它不是走 StartGame 消失的
        // （比如重开场景、被别的东西删掉），Showing 会一直停在 true，NbGame 就再也不敢放开 timeScale。
        if (_instance == this) _instance = null;
        Showing = false;

        if (_white != null)
        {
            if (_white.texture != null) Destroy(_white.texture);
            Destroy(_white);
            _white = null;
        }
    }
}
