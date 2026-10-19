// BakeNeighbourhoodProps.cs — 把居民区的树、花坛、垃圾桶、路灯、草地、铺装"烘"进场景文件
//
// 为什么需要它：
//   这些东西一直是 NbBuilder 在**开机时**用 CreatePrimitive 现搭的（本工程的老规矩：场景文件一行不改）。
//   代价是「只按 Play 才存在」—— 不按 Play 打开 SampleScene，能看见的只有 12 栋楼，
//   树、路灯、花坛全是空的；摆玩家出生点、看构图的时候等于在对着空地看着摆。
//
// 做法：
//   编辑模式下调同一个 NbBuilder.Build()，把生成结果留在场景里，然后存盘。
//   · 网格是 Unity 内置的 Cube / Cylinder / Sphere —— 不是 new Mesh()，所以能存进场景；
//   · 材质是 Nb.Mat 里 new 出来的（按颜色共享，一共十来份），会作为「内嵌材质」跟着场景一起存；
//   · 运行时的 NbBuilder.Build() 开头会先找同名根节点，找到就沿用，**不会叠出第二套**。
//
// 两个菜单：
//   Tools/Street/Bake Neighbourhood Props    —— 搭进去并存盘（推荐在摆完楼栋之后跑一次）
//   Tools/Street/Remove Baked Props          —— 删掉，回到「只在运行时才有」
//
// 注意：只在编辑模式下跑。Play 模式下 Hierarchy 里的改动不会存盘，白忙。

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class BakeNeighbourhoodProps
{
    const string Menu = "Tools/Street/";

    [MenuItem(Menu + "Bake Neighbourhood Props (into scene)", false, 20)]
    static void Bake()
    {
        if (!Guard(out UnityEngine.SceneManagement.Scene scene)) return;

        Transform old = FindRoot(scene);
        int oldCount = old != null ? old.GetComponentsInChildren<Renderer>(true).Length : 0;

        string ask = old != null
            ? string.Format("场景里已经有一份「{0}」（{1} 个渲染器）。\n\n" +
                            "继续会先删掉旧的、按当前代码重搭一份，然后保存场景。\n" +
                            "（改了 NeighbourhoodBuilder 里的位置/颜色之后，就该重跑这一次。）",
                NbBuilder.RootName, oldCount)
            : "会把草地、铺装、围墙、路灯、树、垃圾桶、花坛搭进当前场景，然后保存。\n" +
              "之后不按 Play 也能在 Scene 视图里看见它们；运行时不会再重复生成。";

        if (!EditorUtility.DisplayDialog("烘焙居民区装饰物", ask, "烘焙并保存", "取消")) return;

        if (old != null)
        {
            Debug.LogFormat("[Bake] 删掉旧的「{0}」（{1} 个渲染器）", NbBuilder.RootName, oldCount);
            Object.DestroyImmediate(old.gameObject);
        }

        Transform root = NbBuilder.Build(null);
        if (root == null)
        {
            Debug.LogError("[Bake] NbBuilder.Build 没返回根节点 —— 可能场景里还有一份没删干净。");
            return;
        }

        root.gameObject.name = NbBuilder.RootName;      // Build 里已经这么命名，这里只是兜底
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        int objects = root.GetComponentsInChildren<Transform>(true).Length;
        int rends = root.GetComponentsInChildren<Renderer>(true).Length;
        Selection.activeGameObject = root.gameObject;
        EditorGUIUtility.PingObject(root.gameObject);

        Debug.LogFormat("[Bake] 已写入场景：{0} 个物体、{1} 个渲染器，场景已保存（{2}）。" +
                        "Play 时 NbBuilder 会直接沿用这一份，不再重复生成。",
            objects, rends, string.IsNullOrEmpty(scene.path) ? "未命名场景" : scene.path);
    }

    [MenuItem(Menu + "Remove Baked Props (delete from scene)", false, 21)]
    static void Remove()
    {
        if (!Guard(out UnityEngine.SceneManagement.Scene scene)) return;

        Transform root = FindRoot(scene);
        if (root == null)
        {
            EditorUtility.DisplayDialog("没有可删的", "当前场景里没有 " + NbBuilder.RootName + "。", "好");
            return;
        }

        int objects = root.GetComponentsInChildren<Transform>(true).Length;
        if (!EditorUtility.DisplayDialog("删掉烘好的装饰物",
                string.Format("会从场景里删掉「{0}」的 {1} 个物体并保存。\n\n删完之后，Play 时又会由代码重新搭出来，所以游戏不受影响。",
                    NbBuilder.RootName, objects), "删除并保存", "取消"))
            return;

        Object.DestroyImmediate(root.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.LogFormat("[Bake] 已从场景删掉「{0}」（{1} 个物体），回到运行时自装配。", NbBuilder.RootName, objects);
    }

    // ───────────────────────── 公共小工具 ─────────────────────────

    static bool Guard(out UnityEngine.SceneManagement.Scene scene)
    {
        scene = EditorSceneManager.GetActiveScene();

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("先退出 Play",
                "这个操作要在编辑模式下做。\n\nPlay 模式里改 Hierarchy 是不会存盘的，退出 Play 之后一切还原。", "好");
            return false;
        }
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("场景还没存过",
                "当前场景没有磁盘路径（新建未保存）。先 Cmd+S 存一次再烘焙。", "好");
            return false;
        }
        return true;
    }

    static Transform FindRoot(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            if (roots[i] != null && roots[i].name == NbBuilder.RootName) return roots[i].transform;
        return null;
    }
}
