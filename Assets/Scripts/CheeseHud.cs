// CheeseHud.cs — 右上角的奶酪计数（运行时自装配，不改场景文件）
//
// 显示：右上角一行小字「cheese」+ 一行大字「1 / 3」。吃到一块时数字会弹一下并闪成奶酪黄。
//
// 字体：优先用工程 UI 里那款 Kindly Crunch（Assets/Fonts/TMP_KindlyCrunch.asset），
//       这样 HUD 和 UI_Screens 里的结算页字体完全一致。运行时只能 Resources.Load，
//       所以用 Resources/GameProps/HudFont.asset 做引用中转（见 HudFontRef.cs）。
//       取不到时退回 TMP 默认字体，HUD 照常显示，只是字体不同。
//
// 可读性：白盒场景墙面偏白，纯白文字会看不见。所以默认
//         ① 加一块半透明深色小底板（backingAlpha，调成 0 就是纯文字）
//         ② 给文字开 SDF 描边（outlineText）
//
// ⚠️ TMP 的文字不走 uGUI 的 Outline/Shadow 组件通道（那些只对 legacy Text 生效，
//    加了也不会有任何效果、也不报错）。要给 TMP 加描边只能改 SDF 材质：
//    启用 OUTLINE_ON 关键字 + 设 _OutlineWidth / _OutlineColor。见 ApplyOutline。

using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal class CheeseHud : MonoBehaviour
{
    // 色调取自 UI_Screens 里现成的方案：按钮 #BF733A、强调 #FBD765
    static readonly Color LabelColor   = new Color(0.984f, 0.843f, 0.396f, 1f); // #FBD765
    static readonly Color CountColor   = new Color(1f, 1f, 1f, 1f);
    static readonly Color PulseColor   = new Color(1f, 0.906f, 0.478f, 1f);     // 吃到时闪的奶酪黄
    static readonly Color BackingColor = new Color(0.16f, 0.11f, 0.07f, 1f);    // 深棕黑，配合 backingAlpha
    static readonly Color32 OutlineColor = new Color32(28, 16, 6, 255);

    [Header("布局")]
    public float cornerMargin = 36f;
    public int sortingOrder = 50;          // SampleScene 里没有别的画布，正数即可

    [Header("文字")]
    public string label = "cheese";
    public float labelSize = 30f;
    public float countSize = 64f;
    public string countFormat = "{0} / {1}";   // {0}=已吃到，{1}=总数

    [Header("可读性（压在偏白的白盒画面上）")]
    [Range(0f, 1f)] public float backingAlpha = 0.42f;  // 0 = 不要底板，纯文字
    public bool outlineText = true;                     // SDF 描边

    [Header("吃到时的反馈")]
    public float popScale = 0.30f;
    public float popDuration = 0.28f;

    RectTransform _countRect;
    TextMeshProUGUI _labelText;
    TextMeshProUGUI _countText;
    Sprite _white;
    int _total;
    int _collected;
    float _pop;   // 1 → 0，吃到时用

    /// 造一个右上角奶酪计数；调用方用 SetCollected 更新
    internal static CheeseHud Create(int total)
    {
        GameObject go = new GameObject("CheeseHud (auto)");
        CheeseHud hud = go.AddComponent<CheeseHud>();
        hud.Build(total);
        return hud;
    }

    void Build(int total)
    {
        _total = Mathf.Max(total, 0);
        TMP_FontAsset font = ResolveFont();

        GameObject canvasGo = new GameObject("CheeseHud Canvas (auto)", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = NewRect("HudRoot", canvasGo.transform);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);   // 贴右上角
        root.anchoredPosition = new Vector2(-cornerMargin, -cornerMargin);
        root.sizeDelta = new Vector2(280f, labelSize + countSize + 40f);

        // —— 可选底板（默认开一点点：白盒场景偏白，纯白字会看不见）——
        if (backingAlpha > 0.001f)
        {
            _white = MakeWhiteSprite();
            RectTransform bg = NewRect("Backing", root);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = new Vector2(-18f, -14f);   // 往外扩一圈，让文字不贴边
            bg.offsetMax = new Vector2(18f, 14f);
            Image backing = bg.gameObject.AddComponent<Image>();
            backing.sprite = _white;
            backing.type = Image.Type.Simple;
            backing.color = new Color(BackingColor.r, BackingColor.g, BackingColor.b, backingAlpha);
            backing.raycastTarget = false;
        }

        // —— 小字标签 ——
        _labelText = MakeText("Label", root, font, labelSize, LabelColor);
        _labelText.text = label;
        RectTransform lr = _labelText.rectTransform;
        lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(1f, 1f);
        lr.sizeDelta = new Vector2(root.sizeDelta.x, labelSize + 10f);
        lr.anchoredPosition = Vector2.zero;

        // —— 大号计数（右对齐：数字变长时往左长，不会跑出屏幕）——
        _countText = MakeText("Count", root, font, countSize, CountColor);
        _countRect = _countText.rectTransform;
        _countRect.anchorMin = _countRect.anchorMax = _countRect.pivot = new Vector2(1f, 1f);
        _countRect.sizeDelta = new Vector2(root.sizeDelta.x, countSize + 18f);
        _countRect.anchoredPosition = new Vector2(0f, -(labelSize + 10f));

        Refresh();
        Debug.LogFormat("[CheeseHud] 右上角奶酪计数已就位（共 {0} 块）。", _total);
    }

    /// 更新已吃到的数量（吃掉一块就调一次）
    internal void SetCollected(int collected)
    {
        _collected = Mathf.Clamp(collected, 0, _total);
        Refresh();
        _pop = 1f;
    }

    void Refresh()
    {
        if (_countText != null) _countText.text = string.Format(countFormat, _collected, _total);
    }

    void Update()
    {
        if (_pop <= 0f) return;

        // 用 unscaledDeltaTime：万一有人把 timeScale 设成 0（比如加了暂停），HUD 也不会僵住
        _pop = Mathf.Max(0f, _pop - Time.unscaledDeltaTime / Mathf.Max(popDuration, 0.01f));

        float k = Mathf.Sin(_pop * Mathf.PI);   // _pop 1→0 时 k 走 0→1→0，天然缓入缓出
        if (_countRect != null) _countRect.localScale = Vector3.one * (1f + popScale * k);
        if (_countText != null) _countText.color = Color.Lerp(CountColor, PulseColor, k);
    }

    TextMeshProUGUI MakeText(string goName, Transform parent, TMP_FontAsset font, float size, Color color)
    {
        RectTransform rt = NewRect(goName, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.TopRight;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;                 // 纯显示，别挡任何点击
        if (outlineText) ApplyOutline(t, OutlineColor, 0.18f);
        return t;
    }

    /// TMP 的描边只能这样加：启用 OUTLINE_ON 关键字 + 写材质属性。
    /// 只设 text.outlineWidth 是没用的 —— SetOutlineThickness 只写浮点、不启用关键字，
    /// 而字体资源里的材质默认没开这个关键字。
    static void ApplyOutline(TextMeshProUGUI t, Color32 color, float width)
    {
        Material m = t.fontMaterial;             // 取实例（每份 Text 一份，不动共享材质）
        if (m == null || !m.HasProperty("_OutlineWidth")) return;
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat("_OutlineWidth", width);
        m.SetColor("_OutlineColor", color);
    }

    static TMP_FontAsset ResolveFont()
    {
        HudFontRef r = Resources.Load<HudFontRef>("GameProps/HudFont");
        if (r != null && r.font != null) return r.font;

        Debug.LogWarning("[CheeseHud] 取不到 Resources/GameProps/HudFont 里的字体引用，退回 TMP 默认字体（HUD 仍会显示，只是字体与结算页不同）。");
        TMP_FontAsset def = TMP_Settings.defaultFontAsset;
        if (def == null)
            Debug.LogError("[CheeseHud] TMP 默认字体也是空的 —— 请确认 Assets/TextMesh Pro 已导入（Window > TextMeshPro > Import TMP Essential Resources）。");
        return def;
    }

    static RectTransform NewRect(string goName, Transform parent)
    {
        GameObject go = new GameObject(goName, typeof(RectTransform));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
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
        // 自建的贴图/Sprite 要自己销毁，否则每次切场景泄漏一张
        if (_white != null)
        {
            if (_white.texture != null) Destroy(_white.texture);
            Destroy(_white);
            _white = null;
        }
    }
}
