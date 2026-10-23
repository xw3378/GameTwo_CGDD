// @role Win/lose overlay for the legacy cat game: freezes time and shows Game Failed / You Win.
// @calls CheeseHud

// CatCheeseEndScreen.cs — 游戏内的成败覆盖层（运行时自装配，不改场景文件）
//
// 为什么不切到 UI_Screens 里的 End Screen 页：
//   ① 那两个页面写的是 "You got caught!" / "You made it home!"，不是想要的 "Game Failed"；
//   ② 更要紧的是 —— UI_Screens 不在 Build Settings 的 Scenes In Build 里（那里只有 SampleScene），
//      SceneManager.LoadScene("UI_Screens") 会直接抛
//      「Scene 'UI_Screens' couldn't be loaded because it has not been added to the build settings」，
//      画面上什么都不会发生。这就是上一版「被抓 → 失败页」没反应的原因。
//
// 所以结果直接画在当前游戏画面上（和右上角 HUD 同一套字体/配色，Kindly Crunch 只有 ASCII，不能写中文）：
//   被猫抓到     → 大字 "Game Failed"
//   集齐所有奶酪 → 大字 "You Win"
// 出现时：冻结时间（Time.timeScale = 0）、放开鼠标、停掉玩家移动；按 R 重开本关。
//
// ⚠️ TMP 的描边不走 uGUI 的 Outline/Shadow 组件（那些只对 legacy Text 生效，加了不报错也不生效），
//    只能启用 SDF 材质的 OUTLINE_ON 关键字 + 写 _OutlineWidth / _OutlineColor。见 ApplyOutline。

using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal class CatCheeseEndScreen : MonoBehaviour
{
    // 与 CheeseHud 同一套配色：失败红、成功用奶酪黄 #FBD765
    static readonly Color LoseColor = new Color(1f, 0.35f, 0.32f, 1f);
    static readonly Color WinColor = new Color(0.984f, 0.843f, 0.396f, 1f);
    static readonly Color SubColor = new Color(1f, 1f, 1f, 0.88f);
    static readonly Color32 OutlineColor = new Color32(22, 13, 5, 255);

    const string LoseTitle = "Game Failed";
    const string WinTitle = "You Win";
    const string LoseSub = "the cat got you   -   press R to try again";
    const string WinSub = "all cheese collected   -   press R to play again";

    [Header("文字")]
    public float titleSize = 150f;
    public float subSize = 42f;

    [Header("层级 / 动效")]
    public int sortingOrder = 100;        // 要压过 CheeseHud 的 50
    public float fadeInDuration = 0.45f;

    static CatCheeseEndScreen _instance;

    TextMeshProUGUI _title;
    CanvasGroup _group;
    Sprite _white;
    float _fade;

    // 若工程关了 Domain Reload，静态字段不会自动重置 —— 显式清一次，免得第二次 Play 弹不出来
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _instance = null; }

    /// 弹出结果覆盖层（一局只会弹一次）
    internal static void Show(bool win)
    {
        if (_instance != null) return;
        GameObject go = new GameObject("CatCheeseEndScreen (auto)");
        _instance = go.AddComponent<CatCheeseEndScreen>();
        _instance.Build(win);
    }

    void Build(bool win)
    {
        // —— 收尾：冻住场景、停掉玩家、放开鼠标 ——
        Time.timeScale = 0f;
        FreezePlayer();

        // —— 画布（比 HUD 更高一层，全屏压暗在最下面）——
        GameObject canvasGo = new GameObject("End Canvas (auto)", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;                 // 淡入
        _group.blocksRaycasts = false;
        _group.interactable = false;

        _white = MakeWhiteSprite();
        RectTransform dim = NewRect("Dim", canvasGo.transform);
        dim.anchorMin = Vector2.zero;
        dim.anchorMax = Vector2.one;
        dim.offsetMin = Vector2.zero;
        dim.offsetMax = Vector2.zero;
        Image dimImage = dim.gameObject.AddComponent<Image>();
        dimImage.sprite = _white;
        dimImage.type = Image.Type.Simple;
        dimImage.color = new Color(0f, 0f, 0f, 0.70f);   // 白盒场景偏白，压暗才看得清大字
        dimImage.raycastTarget = false;

        TMP_FontAsset font = ResolveFont();

        // —— 大字：Game Failed / You Win ——
        _title = MakeText("Title", canvasGo.transform, font, titleSize, win ? WinColor : LoseColor);
        _title.text = win ? WinTitle : LoseTitle;
        _title.rectTransform.sizeDelta = new Vector2(1800f, titleSize * 1.5f);
        _title.rectTransform.anchoredPosition = new Vector2(0f, 60f);

        // —— 小字：操作提示 ——
        TextMeshProUGUI sub = MakeText("Sub", canvasGo.transform, font, subSize, SubColor);
        sub.text = win ? WinSub : LoseSub;
        sub.rectTransform.sizeDelta = new Vector2(1800f, subSize * 2f);
        sub.rectTransform.anchoredPosition = new Vector2(0f, -80f);

        Debug.LogFormat("[CatCheese] {0} —— 结果画面已显示（按 R 重开本关）。",
            win ? "集齐所有奶酪，挑战成功" : "被猫抓到，挑战失败");
    }

    void FreezePlayer()
    {
        foreach (FirstPersonMove move in Object.FindObjectsByType<FirstPersonMove>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            move.enabled = false;          // 停掉移动（它的 OnDisable 会顺手把鼠标放开）

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        // 淡入要注意：此时 timeScale 已经是 0，只能用 unscaledDeltaTime
        if (_group != null && _fade < 1f)
        {
            _fade = Mathf.Clamp01(_fade + Time.unscaledDeltaTime / Mathf.Max(fadeInDuration, 0.01f));
            _group.alpha = _fade;
        }

        // 大字轻微呼吸，让它看起来"活着"
        if (_title != null)
        {
            float k = 1f + 0.03f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI);
            _title.rectTransform.localScale = new Vector3(k, k, 1f);
        }

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame) Restart();
    }

    void Restart()
    {
        Time.timeScale = 1f;               // 必须先还回来，否则新一局是静止的
        _instance = null;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    TextMeshProUGUI MakeText(string goName, Transform parent, TMP_FontAsset font, float size, Color color)
    {
        RectTransform rt = NewRect(goName, parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;
        ApplyOutline(t, OutlineColor, 0.20f);
        return t;
    }

    /// TMP 描边只能这样加：启用 OUTLINE_ON 关键字 + 写材质属性。
    /// 只设 text.outlineWidth 没用 —— 字体资源里的材质默认没开这个关键字。
    static void ApplyOutline(TextMeshProUGUI t, Color32 color, float width)
    {
        Material m = t.fontMaterial;       // 取实例（每份 Text 一份，不动共享材质）
        if (m == null || !m.HasProperty("_OutlineWidth")) return;
        m.EnableKeyword("OUTLINE_ON");
        m.SetFloat("_OutlineWidth", width);
        m.SetColor("_OutlineColor", color);
    }

    static TMP_FontAsset ResolveFont()
    {
        HudFontRef r = Resources.Load<HudFontRef>("GameProps/HudFont");
        if (r != null && r.font != null) return r.font;

        Debug.LogWarning("[CatCheese] 取不到 Resources/GameProps/HudFont 里的字体引用，结果画面退回 TMP 默认字体（仍会显示，只是字体和 HUD 不同）。");
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
        // 自建的贴图/Sprite 要自己销毁，否则每次切场景泄漏一张
        if (_white != null)
        {
            if (_white.texture != null) Destroy(_white.texture);
            Destroy(_white);
            _white = null;
        }
    }
}
