// @role In-game HUD: cheese count, toasts, health pips, exposure/rot/door meters, win/fail overlay.
// @calls NbGame NbScene NbLevels NbUi NbFridge

// NeighbourhoodHud.cs — 左上角奶酪计数 + 提示条 + 成功面板 + 被抓红闪
//
// 和工程里现成的 CheeseHud 同一套路：运行时建 Canvas，贴图全部程序化生成，
// 字体走 Resources/GameProps/HudFont（指向工程 UI 用的那款 Kindly Crunch）。

using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// 造 UI 用的小工具箱（地图面板也用这里的）
internal static class NbUi
{
    static Sprite _white, _round, _tri;
    static TMP_FontAsset _font;
    static bool _fontTried;

    internal static Sprite White
    {
        get
        {
            if (_white == null)
            {
                Texture2D t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                Color[] px = new Color[16];
                for (int i = 0; i < px.Length; i++) px[i] = Color.white;
                t.SetPixels(px);
                t.Apply();
                t.hideFlags = HideFlags.HideAndDontSave;
                _white = Sprite.Create(t, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
                _white.hideFlags = HideFlags.HideAndDontSave;
            }
            return _white;
        }
    }

    internal static Sprite Round
    {
        get
        {
            if (_round == null) _round = MakeRounded(48, 12f);
            return _round;
        }
    }

    /// 朝上的三角形，用来做小地图上的"自己"
    internal static Sprite Triangle
    {
        get
        {
            if (_tri == null) _tri = MakeTriangle(64);
            return _tri;
        }
    }

    internal static TMP_FontAsset Font
    {
        get
        {
            if (!_fontTried)
            {
                _fontTried = true;
                HudFontRef r = Resources.Load<HudFontRef>("GameProps/HudFont");
                if (r != null && r.font != null) _font = r.font;
                else
                {
                    _font = TMP_Settings.defaultFontAsset;
                    Debug.LogWarning("[Nb] 取不到 Resources/GameProps/HudFont，UI 退回 TMP 默认字体。");
                }
            }
            return _font;
        }
    }

    internal static RectTransform Rect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    internal static Image Img(string name, Transform parent, Color color, Sprite sprite = null)
    {
        RectTransform rt = Rect(name, parent);
        Image im = rt.gameObject.AddComponent<Image>();
        im.sprite = sprite != null ? sprite : White;
        im.type = Image.Type.Simple;
        im.color = color;
        im.raycastTarget = false;
        return im;
    }

    internal static TextMeshProUGUI Text(string name, Transform parent, string s, float size, Color color,
        TextAlignmentOptions align)
    {
        RectTransform rt = Rect(name, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset f = Font;
        if (f != null) t.font = f;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        t.text = s;
        ApplyOutline(t, new Color32(18, 14, 10, 255), 0.16f);
        return t;
    }

    /// TMP 的描边只能改 SDF 材质：启用 OUTLINE_ON 关键字再写宽度，加组件没用
    internal static void ApplyOutline(TextMeshProUGUI t, Color32 color, float width)
    {
        Material m = t.fontMaterial;
        if (m == null || !m.HasProperty("_OutlineWidth")) return;
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat("_OutlineWidth", width);
        m.SetColor("_OutlineColor", color);
    }

    static Sprite MakeRounded(int size, float radius)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[size * size];
        float r = Mathf.Min(radius, size * 0.5f - 1f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float cx = Mathf.Clamp(fx, r, size - r);
                float cy = Mathf.Clamp(fy, r, size - r);
                float d = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
                float a = Mathf.Clamp01(r - d + 0.5f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        t.SetPixels(px);
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        Sprite s = Sprite.Create(t, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    static Sprite MakeTriangle(int size)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;                 // 0 = 底部, 1 = 顶部
            float halfW = 0.46f * (1f - v);              // 越往上越窄 → 朝上的三角形
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f;
                float a = Mathf.Clamp01((halfW - Mathf.Abs(u)) * size * 1.1f + 0.5f);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        t.SetPixels(px);
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        Sprite s = Sprite.Create(t, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}

internal class NbHud : MonoBehaviour
{
    internal static NbHud I;

    static readonly Color Cream = new Color(1.00f, 0.97f, 0.90f, 1f);
    static readonly Color Gold = new Color(1.00f, 0.86f, 0.36f, 1f);
    static readonly Color Dim = new Color(0.86f, 0.82f, 0.74f, 1f);
    static readonly Color Panel = new Color(0.10f, 0.09f, 0.08f, 0.62f);

    TextMeshProUGUI _level;
    TextMeshProUGUI _count;
    TextMeshProUGUI _catLine;
    TextMeshProUGUI _pickLabel;     // 目标物标签：四个场景都叫 CHEESE
    TextMeshProUGUI _toast;
    TextMeshProUGUI _probe;
    TextMeshProUGUI _hpText;
    TextMeshProUGUI _alert;         // "SPOTTED - SHIFT TO RUN"（只在被保安追时出现）

    /// 生命条被画成 **10 个小格子**，一格 = 一口 = 10 点血。
    /// 不用一条连续的长条，是因为"还能挨几口"是这个游戏唯一需要瞬间读出的数字；
    /// 连续条的 70% 和 60% 在余光里分不出来，少一格却一眼就看见。
    readonly Image[] _hpSegs = new Image[NbLevels.StartHp / NbLevels.BiteDamage];

    Image _flash;

    GameObject _endRoot;
    TextMeshProUGUI _endTitle;
    TextMeshProUGUI _endSub;
    TextMeshProUGUI _endKey;

    // ── 场景专属的第二根条 ──
    // 后院是曝光（EXPOSURE），厨房是新鲜度（FRESH）。
    // **只做一个控件、换标签和颜色**，因为这两件事永远不会同时出现；
    // 各做一套只会让 Build() 里多一倍坐标要维护，而且两套永远有一套是死的。
    GameObject _meterRoot;
    TextMeshProUGUI _meterLabel;
    Image _meterFill;
    const float MeterW = 520f;

    // ── 底部排版（改之前先看这张表）──
    // 旧版这一块是**叠着**的：吐司条占 y 66…118，而曝光条的标签正好在 66…94，两行字直接压在一起；
    // 左下角那行操作提示宽 1040 px，一直伸到屏幕中间，从曝光条下面穿过去；F1 读数条同理。
    // 现在每个东西都有自己的水平区间和高度：
    //   吐司    y 134…186（宽 1100，居中）
    //   警告    y 192…236（宽 1000，居中）
    //   状态条  y  58…120，其中标签 97…125（宽 560，居中 ⇒ 屏幕 x 680…1240）
    //   左下两行 **宽度一律 ≤620 px**（x 36…656），永远碰不到中间那根条。
    const float ToastY = 160f;
    const float AlertY = 214f;
    const float LeftW = 620f;

    internal static NbHud Create()
    {
        GameObject go = new GameObject("Neighbourhood HUD (auto)");
        NbHud hud = go.AddComponent<NbHud>();
        hud.Build();
        return hud;
    }

    void Awake()
    {
        I = this;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }

    void Build()
    {
        GameObject canvasGo = new GameObject("Nb Canvas (auto)", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform c = canvasGo.transform;

        // ── 被抓时全屏红闪（最底层）──
        RectTransform flashRt = NbUi.Rect("Flash", c);
        flashRt.anchorMin = Vector2.zero;
        flashRt.anchorMax = Vector2.one;
        flashRt.offsetMin = Vector2.zero;
        flashRt.offsetMax = Vector2.zero;
        _flash = flashRt.gameObject.AddComponent<Image>();
        _flash.sprite = NbUi.White;
        _flash.color = new Color(0.75f, 0.08f, 0.06f, 0f);
        _flash.raycastTarget = false;

        // ── 左上角：关卡 / 奶酪 / 猫数 ──
        RectTransform card = NbUi.Rect("CheeseCard", c);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0f, 1f);
        card.anchoredPosition = new Vector2(36f, -36f);
        card.sizeDelta = new Vector2(320f, 196f);

        RectTransform cardBg = NbUi.Rect("Bg", card);
        cardBg.anchorMin = Vector2.zero;
        cardBg.anchorMax = Vector2.one;
        cardBg.offsetMin = Vector2.zero;
        cardBg.offsetMax = Vector2.zero;
        Image bg = cardBg.gameObject.AddComponent<Image>();
        bg.sprite = NbUi.Round;
        bg.type = Image.Type.Sliced;
        bg.color = Panel;
        bg.raycastTarget = false;

        _level = NbUi.Text("Level", card, "LEVEL  1 / 10", 24f, Gold, TextAlignmentOptions.TopLeft);
        Place(_level.rectTransform, new Vector2(22f, -12f), new Vector2(280f, 30f));

        TextMeshProUGUI label = NbUi.Text("Label", card, "CHEESE", 22f, Dim, TextAlignmentOptions.TopLeft);
        Place(label.rectTransform, new Vector2(22f, -46f), new Vector2(260f, 26f));
        _pickLabel = label;

        _count = NbUi.Text("Count", card, "0 / 2", 58f, Cream, TextAlignmentOptions.TopLeft);
        Place(_count.rectTransform, new Vector2(20f, -72f), new Vector2(230f, 66f));

        _catLine = NbUi.Text("Cats", card, "CATS  1", 22f, Gold, TextAlignmentOptions.TopLeft);
        Place(_catLine.rectTransform, new Vector2(22f, -146f), new Vector2(260f, 28f));

        // ── 左上角第二块：生命条 ──
        RectTransform hpCard = NbUi.Rect("HpCard", c);
        hpCard.anchorMin = hpCard.anchorMax = hpCard.pivot = new Vector2(0f, 1f);
        hpCard.anchoredPosition = new Vector2(36f, -244f);
        hpCard.sizeDelta = new Vector2(320f, 96f);

        RectTransform hpBg = NbUi.Rect("Bg", hpCard);
        hpBg.anchorMin = Vector2.zero;
        hpBg.anchorMax = Vector2.one;
        hpBg.offsetMin = Vector2.zero;
        hpBg.offsetMax = Vector2.zero;
        Image hpBgImg = hpBg.gameObject.AddComponent<Image>();
        hpBgImg.sprite = NbUi.Round;
        hpBgImg.type = Image.Type.Sliced;
        hpBgImg.color = Panel;
        hpBgImg.raycastTarget = false;

        TextMeshProUGUI hpLabel = NbUi.Text("Label", hpCard, "HEALTH", 22f, Dim, TextAlignmentOptions.TopLeft);
        Place(hpLabel.rectTransform, new Vector2(22f, -12f), new Vector2(160f, 26f));

        _hpText = NbUi.Text("Value", hpCard, "100 / 100", 22f, Cream, TextAlignmentOptions.TopRight);
        Place(_hpText.rectTransform, new Vector2(152f, -12f), new Vector2(146f, 26f));

        // 10 格：宽 (276 - 9*3) / 5 = 50，节距 27.9
        const float segW = 50f, segGap = 3f;
        for (int i = 0; i < _hpSegs.Length; i++)
        {
            // 用纯白方块而不是圆角图：这是一排"电池格"，圆角切开后每格只剩两条竖边，
            // 看起来像进度条被拉长了 —— 方格才数得清"还剩几口"。
            Image seg = NbUi.Img("Seg" + i, hpCard, new Color(0.55f, 0.78f, 0.42f, 1f));
            seg.rectTransform.anchorMin = seg.rectTransform.anchorMax = seg.rectTransform.pivot = new Vector2(0f, 1f);
            seg.rectTransform.anchoredPosition = new Vector2(22f + i * (segW + segGap), -48f);
            seg.rectTransform.sizeDelta = new Vector2(segW, 20f);
            _hpSegs[i] = seg;
        }

        // ── 底部提示条 ──
        // 挪到曝光条**上方**（160 而不是 92）。92 的时候它和曝光条的标签（97…125）完全重合，
        // 三个场景都会出现 "LEVEL 1" 压在 "EXPOSURE" 上。
        _toast = NbUi.Text("Toast", c, "", 38f, Cream, TextAlignmentOptions.Center);
        RectTransform tr = _toast.rectTransform;
        tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0.5f, 0f);
        tr.anchoredPosition = new Vector2(0f, ToastY);
        tr.sizeDelta = new Vector2(1100f, 52f);
        _toast.color = new Color(Cream.r, Cream.g, Cream.b, 0f);

        // ── 被发现的警告 ──
        // 只回答"现在该做什么"。玩家已经知道自己被发现了（手电变红、人正冲过来），
        // 所以这里不写 "YOU WERE SEEN"，直接写距离；按键由同一秒弹出的吐司负责。
        _alert = NbUi.Text("Alert", c, "SPOTTED  -  HIDE  5 m  AWAY", 34f,
            new Color(1f, 0.42f, 0.30f, 0f), TextAlignmentOptions.Center);
        RectTransform ar = _alert.rectTransform;
        ar.anchorMin = ar.anchorMax = ar.pivot = new Vector2(0.5f, 0f);
        ar.anchoredPosition = new Vector2(0f, AlertY);
        ar.sizeDelta = new Vector2(1000f, 44f);

        // ── 操作提示（左下角）──
        // 宽度压到 620：旧版 1040 会横穿屏幕中央，被曝光条压住一半。
        TextMeshProUGUI hint = NbUi.Text("Hint", c, "WASD - MOVE     SHIFT - RUN     SPACE - JUMP     Q/E - TURN     ENTER - MAIN MENU", 18f,
            new Color(1f, 1f, 1f, 0.42f), TextAlignmentOptions.BottomLeft);
        RectTransform hr = hint.rectTransform;
        hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(0f, 0f);
        hr.anchoredPosition = new Vector2(36f, 30f);
        hr.sizeDelta = new Vector2(LeftW, 26f);

        // ── 读数条（左下角，提示行上方一行）──
        // 存在的理由很具体：画面一片均匀时，人眼分不清"玩家没动"和"看不出在动"。
        // 这一行直接把坐标和实测速度摊开 —— 数字在跳就说明人真的在走。
        // 字号 16、宽 620：它和上面那行一样，不能伸进屏幕中央（那里是状态条）。
        _probe = NbUi.Text("Probe", c, "", 16f, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.BottomLeft);
        // 默认**开着**：这一行就是"为什么动不了"的现场读数（timeScale / MOVE / CC / 速度）。
        // 默认关掉的话，出问题时还是只能靠日志猜 —— 而这正是前几轮反复卡住的地方。
        _probe.enabled = true;
        _probe.text = "READOUT ...";
        RectTransform pr = _probe.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0f, 0f);
        pr.anchoredPosition = new Vector2(36f, 60f);
        pr.sizeDelta = new Vector2(LeftW, 24f);

        BuildMeter(c);
        BuildEnd(c);
        Debug.Log("[Nb] HUD 就位（关卡行 / 奶酪计数 / 猫数 / 10 格生命条）。");
    }

    static void Place(RectTransform rt, Vector2 anchored, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
    }

    /// 底部中间那条状态条。放在这里而不是左上角，是因为它读的不是"我有多少资源"，
    /// 而是"我还能撑多久"—— 这是在跑动中要看的，视线应该留在画面中央附近。
    void BuildMeter(Transform c)
    {
        RectTransform root = NbUi.Rect("Meter", c);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 58f);
        root.sizeDelta = new Vector2(560f, 62f);

        RectTransform bg = NbUi.Rect("Bg", root);
        bg.anchorMin = bg.anchorMax = bg.pivot = new Vector2(0.5f, 0.5f);
        bg.anchoredPosition = new Vector2(0f, -10f);
        bg.sizeDelta = new Vector2(560f, 30f);
        Image bgi = bg.gameObject.AddComponent<Image>();
        bgi.sprite = NbUi.Round;
        bgi.type = Image.Type.Sliced;
        bgi.color = new Color(0.06f, 0.06f, 0.07f, 0.72f);
        bgi.raycastTarget = false;

        RectTransform track = NbUi.Rect("Track", bg);
        track.anchorMin = track.anchorMax = track.pivot = new Vector2(0f, 0.5f);
        track.anchoredPosition = new Vector2(16f, 0f);
        track.sizeDelta = new Vector2(MeterW, 12f);
        Image ti = track.gameObject.AddComponent<Image>();
        ti.sprite = NbUi.Round;
        ti.type = Image.Type.Sliced;
        ti.color = new Color(1f, 1f, 1f, 0.10f);
        ti.raycastTarget = false;

        RectTransform fill = NbUi.Rect("Fill", track);
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, 0.5f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = new Vector2(0f, 12f);
        _meterFill = fill.gameObject.AddComponent<Image>();
        _meterFill.sprite = NbUi.Round;
        _meterFill.type = Image.Type.Sliced;
        _meterFill.color = Gold;
        _meterFill.raycastTarget = false;

        _meterLabel = NbUi.Text("Label", root, "EXPOSURE", 22f, Dim, TextAlignmentOptions.Center);
        PlaceCenter(_meterLabel.rectTransform, new Vector2(0f, 22f), new Vector2(560f, 28f));

        _meterRoot = root.gameObject;
        _meterRoot.SetActive(false);
    }

    /// label 传空 = 收起整条（街区没有这根条）。
    /// t 是 0…1 的比例，col 决定"这根条现在是什么心情"：
    /// 曝光用金→红，新鲜度用绿→红，玩家不需要读数字。
    internal void SetMeter(string label, float t, Color col)
    {
        if (_meterRoot == null) return;

        bool on = !string.IsNullOrEmpty(label);
        if (_meterRoot.activeSelf != on) _meterRoot.SetActive(on);
        if (!on) return;

        _meterLabel.text = label;
        _meterLabel.color = col;
        _meterFill.color = col;

        RectTransform rt = _meterFill.rectTransform;
        Vector2 sz = rt.sizeDelta;
        sz.x = Mathf.Clamp01(t) * MeterW;
        rt.sizeDelta = sz;
    }

    void BuildEnd(Transform c)
    {
        RectTransform root = NbUi.Rect("End", c);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(780f, 300f);

        Image bg = root.gameObject.AddComponent<Image>();
        bg.sprite = NbUi.Round;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.08f, 0.07f, 0.06f, 0.90f);
        bg.raycastTarget = false;

        // 文案全部由 NbGame 在过关 / 失败那一刻填 —— 这里是 10 关，同一块面板要报
        // "第几关通过 / 全部通关 / 第几关死了" 三种完全不同的结果，写死的字一定会说错话。
        _endTitle = NbUi.Text("Title", root, "LEVEL  1  CLEAR", 54f, Gold, TextAlignmentOptions.Center);
        PlaceCenter(_endTitle.rectTransform, new Vector2(0f, 74f), new Vector2(720f, 80f));

        _endSub = NbUi.Text("Sub", root, "", 26f, Cream, TextAlignmentOptions.Center);
        PlaceCenter(_endSub.rectTransform, new Vector2(0f, 6f), new Vector2(720f, 40f));

        _endKey = NbUi.Text("Key", root, "PRESS  R  TO  CONTINUE  /  PRESS M TO RETURN TO MAIN MENU", 24f, Dim, TextAlignmentOptions.Center);
        PlaceCenter(_endKey.rectTransform, new Vector2(0f, -74f), new Vector2(720f, 36f));

        _endRoot = root.gameObject;
        _endRoot.SetActive(false);
    }

    static void PlaceCenter(RectTransform rt, Vector2 anchored, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
    }

    /// 过关 / 失败共用一块面板。`good = true` 用金色标题（过关），false 用红色（血被咬光）。
    internal void ShowEnd(string title, string sub, string key, bool good)
    {
        if (_endRoot == null) return;

        Color head = good ? Gold : new Color(0.95f, 0.36f, 0.30f, 1f);
        if (_endTitle != null) { _endTitle.text = title; _endTitle.color = head; }
        if (_endSub != null) _endSub.text = sub;
        if (_endKey != null) _endKey.text = key;

        _endRoot.SetActive(true);
    }

    internal void HideEnd()
    {
        if (_endRoot != null) _endRoot.SetActive(false);
        if (_toast != null) _toast.color = new Color(Cream.r, Cream.g, Cream.b, 0f);
    }

    void Update()
    {
        NbGame g = NbGame.I;
        if (g == null) return;

        if (_level != null) _level.text = "LEVEL  " + g.Level + " / " + NbLevels.Count;

        if (_count != null)
        {
            int need = Mathf.Max(1, g.CheeseNeeded);
            int shown = Mathf.Clamp(g.Collected, 0, need);
            _count.text = shown + " / " + need;
            _count.color = shown >= need ? Gold : Cream;
        }

        if (_catLine != null)
        {
            if (NbScene.UsesDoor)
            {
                // 冰箱里没有猫，但玩家会**在六层之间迷路** —— 这才是这一行该回答的问题。
                // 层号与定位器上的方块数字共用 NbFridge.ShelfOf，两处不会差一级。
                // 总层数是 Layers + 1（底层地板 + 六块板的顶面 = 7 个可站的层）。
                float y = g.Player != null ? g.Player.position.y : 0f;
                int shelf = NbFridge.ShelfOf(y);
                _catLine.text = "SHELF  " + shelf + " / " + (NbFridge.Layers + 1);
                _catLine.color = new Color(0.62f, 0.84f, 1f, 1f);
            }
            else if (NbScene.UsesGuards)
            {
                // 后院没有猫了（用户要求删掉），这一行改报**保安人数**。
                // 写死 "CATS 0" 会让玩家以为猫还没刷出来，而不是"这关本来就没有猫"。
                _catLine.text = "GUARDS  " + g.Guards.Count;
                _catLine.color = g.Chased ? new Color(1f, 0.42f, 0.30f, 1f) : Gold;
            }
            else
            {
                // 猫多起来的时候把这一行染红：第 8 关往后"全是猫"这件事应该在余光里就能看见。
                _catLine.text = "CATS  " + g.CatCount;
                _catLine.color = g.CatCount >= 8 ? new Color(0.95f, 0.42f, 0.34f, 1f) : Gold;
            }
        }

        // 四个场景的目标物**都是奶酪**，标签不再跟着场景变。
        if (_pickLabel != null) _pickLabel.text = "CHEESE";

        if (_hpText != null) _hpText.text = g.Hp + " / " + g.MaxHp;

        // 一格 = 一口 = BiteDamage 点。满血全亮，掉血从右往左灭 —— 读起来就是"被咬掉一块"。
        int bite = Mathf.Max(1, NbLevels.BiteDamage);
        int live = Mathf.Clamp(g.Hp / bite, 0, _hpSegs.Length);
        Color segOn = live >= (_hpSegs.Length * 2 / 3) ? new Color(0.55f, 0.78f, 0.42f, 1f)
                    : live >= Mathf.Max(1, (_hpSegs.Length / 3)) ? new Color(0.98f, 0.74f, 0.28f, 1f)
                                : new Color(0.92f, 0.28f, 0.22f, 1f);
        for (int i = 0; i < _hpSegs.Length; i++)
        {
            if (_hpSegs[i] == null) continue;
            _hpSegs[i].color = i < live ? segOn : new Color(0.20f, 0.17f, 0.15f, 0.85f);
        }

        if (_toast != null)
        {
            bool on = g.ToastT > 0f && !string.IsNullOrEmpty(g.Toast);
            _toast.text = g.Toast;
            float a = on ? Mathf.Clamp01(g.ToastT * 2f) : 0f;
            _toast.color = new Color(Cream.r, Cream.g, Cream.b, a);
            _toast.rectTransform.anchoredPosition = new Vector2(0f, ToastY + (1f - a) * -8f);
        }

        if (_flash != null)
        {
            float a = Mathf.Clamp01(g.Flash) * 0.42f;
            _flash.color = new Color(0.75f, 0.08f, 0.06f, a);
        }

        // 被追的时候这条会**呼吸**：静止的红字在余光里会被当成 HUD 的一部分，闪的不会。
        // 结算面板出现后立刻收掉 —— 那时候"快跑"已经没有意义了。
        if (_alert != null)
        {
            bool on = g.Chased && !g.Finished;
            float a = on ? 0.70f + 0.30f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) : 0f;
            _alert.color = new Color(1f, 0.42f, 0.30f, a);
        }

        // ── 读数条：坐标 / 实测速度 / 最近的猫 ──
        // 这一行是给"到底走没走"这个问题用的：画面均匀时人眼分不清，
        // 但 SPEED 只要不是 0 就说明输入 → CharacterController 这条链是通的。
        if (_probe != null)
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) _probe.enabled = !_probe.enabled;

            if (_probe.enabled && g.Player != null)
            {
                Vector3 p = g.Player.position;

                // EYE / PIT 这两项是给"画面一动不动"用的：
                // EYE 是眼睛离脚底的高度（0.16 才对）；PIT 接近 ±80 说明相机被锁死低头/抬头。
                // 两个都会让人以为"走不动"，因为画面里没有可参照的东西。
                // 标签压到 3~4 个字母不是偷懒：这一行必须 ≤620 px（见 LeftW 的注释），
                // 而且不能出现 `°` 这类符号 —— HUD 字体缺字形时是**静默空白**。
                Camera cam = g.Rig != null ? g.Rig.Cam : null;
                float eye = cam != null ? cam.transform.position.y - p.y : 0f;
                float pitch = g.Rig != null ? g.Rig.Pitch : 0f;

                _probe.text = string.Format(
                    "POS {0:F1}/{1:F1}  SPD {2:F1}  EYE {3:F2}  PIT {4:F0}  tS {5:F2}  MOVE {6}  CC {7}",
                    p.x, p.z, g.Speed, eye, pitch, Time.timeScale,
                    (g.Rig != null && g.Rig.AcceptInput) ? "ON" : "OFF",
                    (g.Rig != null && g.Rig.Cc != null && g.Rig.Cc.enabled) ? "ON" : "OFF");
            }
        }
    }
}
