// @role Scene-select page after the title: pick Street/Yard/Fridge, each with its own best-level progress.
// @calls NbGame NbScene NbProgress NbUi

// NbSceneSelect.cs — 标题页之后的场景选择页
//
// 为什么单独一页、而不是做进标题页：
//   标题页只负责"这是什么东西"，选择页负责"我今天想玩哪一个"。挤在一页会有两个主按钮，
//   玩家会先点错再回来 —— 而这三个场景的难度起点完全一样（都是第 1 关），
//   选错的代价是重新走一遍标题页。
//
// 交互沿用标题页那套：RectTransformUtility 手动判定 + Mouse.current，不依赖 EventSystem。
//   鼠标悬停变亮、点击进入；键盘 1 / 2 / 3 也能选（测试时比找鼠标快得多）。
//
// 三个场景**自由选择**：没有解锁条件，各自记自己的最高关卡（NbProgress）。
// 这一条是用户明确选的 —— 换成顺序解锁的话，每次验收新场景都得先打 10 关。

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

internal class NbSceneSelect : MonoBehaviour
{
    internal static NbSceneSelect I;
    internal static bool Showing { get; private set; }

    static readonly NbSceneKind[] Order =
    {
        NbSceneKind.Street, NbSceneKind.Yard, NbSceneKind.Fridge,
    };

    // 每个场景一个主色。**不是装饰**：三张卡片的颜色会一路延续到那一关的 HUD 提示色上，
    // 玩家选完之后就知道"我在蓝的那一关里"，不用再看标题。
    static readonly Color[] Accent =
    {
        new Color(1.00f, 0.76f, 0.32f),   // 街区：路灯的暖黄
        new Color(0.46f, 0.68f, 1.00f),   // 后院：月光与手电的冷蓝
        new Color(0.62f, 0.84f, 1.00f),   // 冰箱：冷藏灯的白
    };

    static readonly string[] Title =
    {
        "RESIDENTIAL\nBLOCK",
        "BACKYARD\nPATROL",
        "THE\nFRIDGE",
    };

    static readonly string[] Sub =
    {
        "Twelve houses.  Five alleys.\nA cat on every corner.",
        "One open lawn.\nNowhere to hide in the middle.",
        "Six shelves.  One door.\nNowhere to stand in the light.",
    };

    static readonly Color PanelCol = new Color(0.07f, 0.07f, 0.08f, 0.94f);
    static readonly Color DimCol   = new Color(1f, 1f, 1f, 0.55f);
    static readonly Color TextCol  = new Color(1f, 0.97f, 0.91f, 1f);

    readonly RectTransform[] _cards = new RectTransform[3];
    readonly Image[] _cardBg = new Image[3];
    readonly TextMeshProUGUI[] _cardHint = new TextMeshProUGUI[3];

    CanvasGroup _group;
    int _hover = -1;
    bool _picked;

    // ───────────────────────── 创建 ─────────────────────────

    internal static NbSceneSelect Show()
    {
        if (I != null) return I;

        GameObject go = new GameObject("Scene Select (auto)");
        NbSceneSelect s = go.AddComponent<NbSceneSelect>();
        s.Build();
        return s;
    }

    void Awake()
    {
        I = this;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        Showing = false;
    }

    void Build()
    {
        Showing = true;

        GameObject canvasGo = new GameObject("SceneSelect Canvas (auto)",
            typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;                 // 压过 HUD(40) 和小地图(41)

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = canvasGo.GetComponent<CanvasGroup>();

        Transform c = canvasGo.transform;

        // 遮罩：选择页是**独立的一屏**，不是浮在游戏上的小窗。
        // 背景要压到几乎全黑，否则背后的街区会争夺注意力。
        RectTransform shade = NbUi.Rect("Shade", c);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.offsetMin = Vector2.zero;
        shade.offsetMax = Vector2.zero;
        Image sh = shade.gameObject.AddComponent<Image>();
        sh.color = new Color(0.02f, 0.02f, 0.03f, 0.90f);
        sh.raycastTarget = false;

        TextMeshProUGUI head = NbUi.Text("Head", c, "CHOOSE  A  DISTRICT", 46f, TextCol,
            TextAlignmentOptions.Center);
        Center(head.rectTransform, new Vector2(0f, 330f), new Vector2(1200f, 60f));

        TextMeshProUGUI note = NbUi.Text("Note", c,
            "Three places, ten levels each.  Pick any one.  They all start at level 1.",
            20f, DimCol, TextAlignmentOptions.Center);
        Center(note.rectTransform, new Vector2(0f, 278f), new Vector2(1400f, 34f));

        for (int i = 0; i < 3; i++) BuildCard(c, i);

        TextMeshProUGUI foot = NbUi.Text("Foot", c,
            "CLICK  A  CARD   /   PRESS  1  2  3", 18f,
            new Color(1f, 1f, 1f, 0.40f), TextAlignmentOptions.Center);
        Center(foot.rectTransform, new Vector2(0f, -352f), new Vector2(900f, 30f));

        Debug.Log("[Nb] 场景选择页就位：街区 / 后院 / 冰箱，自由选择。");
    }

    void BuildCard(Transform c, int i)
    {
        RectTransform card = NbUi.Rect("Card " + i, c);
        Center(card, new Vector2(-540f + i * 540f, -20f), new Vector2(480f, 470f));
        _cards[i] = card;

        Image bg = card.gameObject.AddComponent<Image>();
        bg.sprite = NbUi.Round;
        bg.type = Image.Type.Sliced;
        bg.color = PanelCol;
        bg.raycastTarget = false;
        _cardBg[i] = bg;

        // 顶部色条：这一页唯一的"颜色身份"，也是 hover 时唯一会亮的东西
        RectTransform bar = NbUi.Rect("Accent", card);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -22f);
        bar.sizeDelta = new Vector2(120f, 6f);
        Image bi = bar.gameObject.AddComponent<Image>();
        bi.sprite = NbUi.Round;
        bi.type = Image.Type.Sliced;
        bi.color = Accent[i];
        bi.raycastTarget = false;

        TextMeshProUGUI no = NbUi.Text("No", card, "0" + (i + 1), 26f,
            new Color(Accent[i].r, Accent[i].g, Accent[i].b, 0.55f), TextAlignmentOptions.Center);
        Center(no.rectTransform, new Vector2(0f, 178f), new Vector2(200f, 34f));

        TextMeshProUGUI name = NbUi.Text("Name", card, Title[i], 40f, TextCol,
            TextAlignmentOptions.Center);
        Center(name.rectTransform, new Vector2(0f, 96f), new Vector2(440f, 120f));

        TextMeshProUGUI sub = NbUi.Text("Sub", card, Sub[i], 21f,
            new Color(1f, 0.97f, 0.91f, 0.66f), TextAlignmentOptions.Center);
        Center(sub.rectTransform, new Vector2(0f, 6f), new Vector2(420f, 80f));

        RectTransform rule = NbUi.Rect("Rule", card);
        rule.anchorMin = rule.anchorMax = rule.pivot = new Vector2(0.5f, 0.5f);
        rule.anchoredPosition = new Vector2(0f, -54f);
        rule.sizeDelta = new Vector2(360f, 1f);
        Image ri = rule.gameObject.AddComponent<Image>();
        ri.color = new Color(1f, 1f, 1f, 0.12f);
        ri.raycastTarget = false;

        int best = NbProgress.Best(Order[i]);
        string prog = best >= NbLevels.Count ? "ALL  TEN  CLEARED" : "BEST  LEVEL  " + best;
        TextMeshProUGUI pr = NbUi.Text("Prog", card, prog, 20f,
            new Color(Accent[i].r, Accent[i].g, Accent[i].b, 0.92f), TextAlignmentOptions.Center);
        Center(pr.rectTransform, new Vector2(0f, -92f), new Vector2(420f, 30f));

        TextMeshProUGUI hint = NbUi.Text("Hint", card, "CLICK  TO  ENTER", 19f,
            new Color(1f, 1f, 1f, 0.35f), TextAlignmentOptions.Center);
        Center(hint.rectTransform, new Vector2(0f, -168f), new Vector2(420f, 30f));
        _cardHint[i] = hint;
    }

    // ───────────────────────── 交互 ─────────────────────────

    void Update()
    {
        if (_picked) return;

        var mouse = Mouse.current;
        var kb = Keyboard.current;

        Vector2 mp = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

        int hover = -1;
        for (int i = 0; i < _cards.Length; i++)
        {
            if (_cards[i] == null) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(_cards[i], mp))
            {
                hover = i;
                break;
            }
        }

        if (hover != _hover)
        {
            _hover = hover;
            Repaint();
        }

        if (mouse != null && mouse.leftButton.wasPressedThisFrame && _hover >= 0)
        {
            Pick(_hover);
            return;
        }

        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) { Pick(0); return; }
            if (kb.digit2Key.wasPressedThisFrame) { Pick(1); return; }
            if (kb.digit3Key.wasPressedThisFrame) { Pick(2); return; }
        }
    }

    /// 悬停只改两样东西：底板亮度和底部那句提示。
    /// 不做缩放/位移动画 —— 三张卡片是并排的，一张放大就会推挤另外两张的视觉重量。
    void Repaint()
    {
        for (int i = 0; i < _cards.Length; i++)
        {
            bool on = i == _hover;
            if (_cardBg[i] != null)
            {
                _cardBg[i].color = on
                    ? new Color(Accent[i].r * 0.22f, Accent[i].g * 0.22f, Accent[i].b * 0.22f, 0.97f)
                    : PanelCol;
            }
            if (_cardHint[i] != null)
            {
                _cardHint[i].text = on ? "ENTER  THIS  DISTRICT" : "CLICK  TO  ENTER";
                _cardHint[i].color = on ? Accent[i] : new Color(1f, 1f, 1f, 0.35f);
            }
        }
    }

    void Pick(int i)
    {
        if (_picked) return;
        _picked = true;

        // **这里不关 Showing。** 淡出还有 0.22 秒，这期间 EnsurePlayable 会每帧
        // 把 AcceptInput 钉回 true —— 要是现在就放开，玩家会带着"已经走了一段"的状态进场。
        // 交给 FadeAndGo 的最后一刻再放。
        NbSceneKind k = Order[i];
        Debug.LogFormat("[Nb] 选中场景 {0}（{1}）", i + 1, k);

        PlayerPrefs.SetInt("nb.scene.last", i);
        PlayerPrefs.Save();

        StartCoroutine(FadeAndGo(k));
    }

    IEnumerator FadeAndGo(NbSceneKind k)
    {
        float d = 0.22f, t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            if (_group != null) _group.alpha = Mathf.Clamp01(1f - t / d);
            yield return null;
        }

        // 先让世界重建，再销毁自己：反过来的话，这一帧没有任何东西接管输入，
        // 玩家会看到黑屏停在原地（而且不会报错）。
        Showing = false;                       // 交接的一刻才放开操作权
        if (NbGame.I != null) NbGame.I.EnterScene(k);
        else Debug.LogWarning("[Nb] 场景选择页找不到 NbGame —— 世界不会重建。");

        Destroy(gameObject);
    }

    static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
