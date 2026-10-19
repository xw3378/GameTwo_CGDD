// NbBriefing.cs — 进场前的玩法说明（目前只有冰箱用）
//
// 为什么这一关必须有它：
//   街区/后院的规则可以边走边学（看见猫就躲、看见手电就绕）。冰箱的核心规则**看不见**——
//   "门会在 3 秒后打开"这件事没有任何先验，玩家第一次一定会在空地上被照一次，
//   然后以为是随机掉血。那不是难度，那是没有教会玩家就开始考试。
//
// 交互刻意做得最笨：**任意键 / 点击任意处**关闭。没有按钮、没有"下一步"。
//   因为这一页的全部内容是一段规则，玩家只会读一遍，多余的 UI 只会推迟他进入游戏。
//
// 它会冻结玩家（AcceptInput = false），并且**同时冻结门**（NbGame.DoorUpdate 里
// 第一句就 return）—— 否则玩家读规则读到一半，倒计时走完，读完先挨一下。
//
// 字号/换行都是按 1920×1080 的参考分辨率调的，正文每行手动断句 ——
// TMP 的自动换行在没有中文字体的工程里会把长句切成很难看的形状。

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

internal class NbBriefing : MonoBehaviour
{
    internal static NbBriefing I;
    internal static bool Showing { get; private set; }

    const string Kicker = "BEFORE  YOU  GO  IN";
    const string Title  = "THE  FRIDGE";

    const string Body =
        "You are a mouse in a fridge.  Six shelves go up,\n" +
        "and the cheese is on them.  Everything else in here is just food.\n" +
        "\n" +
        "There is no cat in here.  But someone keeps opening the door.\n" +
        "\n" +
        "Three seconds before it opens, two things happen:\n" +
        "the gap under the door starts to glow, and the bar at the bottom\n" +
        "of the screen starts counting down.  There is no sound.\n" +
        "That is your warning - you get three seconds, not more.\n" +
        "\n" +
        "When the door opens, light floods the whole fridge.\n" +
        "If you are standing in the open, you lose 10 health.\n" +
        "Hide behind something solid: a milk crate, a melon,\n" +
        "a bread box, a block of butter.  Four on every shelf.\n" +
        "\n" +
        "Standing in a corner does not hide you.\n" +
        "Neither does standing still.  Something has to be in the way.\n" +
        "\n" +
        "To go up, use the staircase in the middle of the fridge.\n" +
        "Four steps per shelf - just walk up, no jumping needed.\n" +
        "Each shelf opens off the side of the stair.\n" +
        "\n" +
        "Bring back every piece of cheese to clear the level.";

    const string Foot = "PRESS  ANY  KEY   /   CLICK  TO  START";

    CanvasGroup _group;
    float _born;
    bool _closing;

    // ───────────────────────── 创建 ─────────────────────────

    internal static NbBriefing Show()
    {
        if (I != null) return I;

        GameObject go = new GameObject("Briefing (auto)");
        NbBriefing b = go.AddComponent<NbBriefing>();
        b.Build();
        return b;
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
        _born = Time.unscaledTime;

        // 冻结玩家。这一步是这一页存在的意义之一 —— 说明是"开始之前"给的，
        // 不是叠在游戏上的浮层。
        if (NbPlayer.I != null) NbPlayer.I.AcceptInput = false;

        GameObject canvasGo = new GameObject("Briefing Canvas (auto)",
            typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 55;                 // 压过 HUD(40)/小地图(41)，低于场景选择页(60)

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = canvasGo.GetComponent<CanvasGroup>();

        Transform c = canvasGo.transform;

        RectTransform shade = NbUi.Rect("Shade", c);
        shade.anchorMin = Vector2.zero;
        shade.anchorMax = Vector2.one;
        shade.offsetMin = Vector2.zero;
        shade.offsetMax = Vector2.zero;
        Image sh = shade.gameObject.AddComponent<Image>();
        sh.color = new Color(0.03f, 0.04f, 0.06f, 0.94f);
        sh.raycastTarget = false;

        RectTransform card = NbUi.Rect("Card", c);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(1040f, 880f);

        Image bg = card.gameObject.AddComponent<Image>();
        bg.sprite = NbUi.Round;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.06f, 0.07f, 0.09f, 0.96f);
        bg.raycastTarget = false;

        // 顶部一条冷光（冰箱的灯色），让这一页和那一关是同一个人设计的
        RectTransform bar = NbUi.Rect("Accent", card);
        bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -26f);
        bar.sizeDelta = new Vector2(140f, 5f);
        Image bi = bar.gameObject.AddComponent<Image>();
        bi.sprite = NbUi.Round;
        bi.type = Image.Type.Sliced;
        bi.color = new Color(0.62f, 0.82f, 1f);
        bi.raycastTarget = false;

        TextMeshProUGUI kick = NbUi.Text("Kicker", card, Kicker, 20f,
            new Color(0.62f, 0.82f, 1f, 0.85f), TextAlignmentOptions.Center);
        Place(kick.rectTransform, new Vector2(0f, 366f), new Vector2(900f, 28f));

        TextMeshProUGUI title = NbUi.Text("Title", card, Title, 52f,
            new Color(1f, 0.98f, 0.94f), TextAlignmentOptions.Center);
        Place(title.rectTransform, new Vector2(0f, 314f), new Vector2(900f, 62f));

        TextMeshProUGUI body = NbUi.Text("Body", card, Body, 22f,
            new Color(1f, 1f, 1f, 0.82f), TextAlignmentOptions.TopLeft);
        body.lineSpacing = 8f;
        Place(body.rectTransform, new Vector2(0f, 262f), new Vector2(940f, 520f));
        body.rectTransform.pivot = new Vector2(0.5f, 1f);
        body.rectTransform.anchoredPosition = new Vector2(0f, 262f);

        TextMeshProUGUI foot = NbUi.Text("Foot", card, Foot, 20f,
            new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Center);
        Place(foot.rectTransform, new Vector2(0f, -396f), new Vector2(900f, 30f));

        Debug.Log("[Nb] 玩法说明已显示（冰箱）—— 任意键或点击开始，门在关闭说明之前不会走。");
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // ───────────────────────── 关闭 ─────────────────────────

    void Update()
    {
        if (_closing) return;
        if (Time.unscaledTime - _born < 0.6f) return;      // 躲开进场那一帧的误触

        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;

        bool go = (kb != null && kb.anyKey.wasPressedThisFrame)
               || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        if (go) StartCoroutine(FadeOut());
    }

    IEnumerator FadeOut()
    {
        _closing = true;

        float d = 0.25f, t = 0f;
        while (t < d)
        {
            t += Time.unscaledDeltaTime;
            if (_group != null) _group.alpha = Mathf.Clamp01(1f - t / d);
            yield return null;
        }

        // 先放开 Showing 再销毁：EnsurePlayable 每帧都在看这个标志，
        // 反过来的话会有几帧"说明没了但人还不能动"。
        Showing = false;
        if (NbPlayer.I != null)
        {
            NbPlayer.I.AcceptInput = true;
            NbPlayer.I.LockCursor();
        }
        Debug.Log("[Nb] 玩法说明关闭，门开始倒计时。");

        Destroy(gameObject);
    }
}
