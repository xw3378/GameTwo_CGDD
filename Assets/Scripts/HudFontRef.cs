// HudFontRef.cs — 一个只用来「持有字体引用」的小资产类型
//
// 为什么需要它：运行时只能用 Resources.Load 取资源，而工程里 UI 用的那款字体
// Assets/Fonts/TMP_KindlyCrunch.asset 不在 Resources 目录下。
// 放一个「引用资产」到 Resources 里做中转（Assets/Resources/GameProps/HudFont.asset），
// 就不必搬动或复制那份 6.4 MB 的字体，屏幕上的字体也和 UI_Screens 的结算页完全一致。
//
// 这个类本身没有任何逻辑，只是让 Unity 能把「一个 TMP_FontAsset 引用」序列化成一个资产。

using TMPro;
using UnityEngine;

public class HudFontRef : ScriptableObject
{
    public TMP_FontAsset font;
}
