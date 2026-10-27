using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把锤子从"圆柱+方块"占位换成 Kenney 的卡通锤子模型。
///
/// 关键约束：命中判定用的是 hammerHead 那个空节点的位置 + 一个球形半径，
/// 所以换模型时必须把 hammerHead 挪到新模型锤头的中心，否则手感会变
/// （打得到的地方和看到的锤头对不上）。
///
/// 菜单：PockHammer / Build Hammer Model
/// </summary>
public static class HammerModelBuilder
{
    const string HammerModel = "Assets/Art/Kenney/SurvivalKit/Models/tool-hammer-upgraded.fbx";
    const string VisualName = "HammerVisual";

    [MenuItem("PockHammer/Build Hammer Model")]
    public static void Build()
    {
        var hammer = GameObject.Find("Hammer");
        if (hammer == null) { Debug.LogError("[HammerModelBuilder] 场景里找不到 Hammer"); return; }

        var controller = hammer.GetComponent<PockHammer.Gameplay.HammerHandController>();
        if (controller == null) { Debug.LogError("[HammerModelBuilder] Hammer 上没有 HammerHandController"); return; }

        // 1) 清掉旧的视觉体和上一次生成的模型
        foreach (var name in new[] { "HammerHead", "HammerHandle", VisualName })
        {
            var old = hammer.transform.Find(name);
            if (old == null) continue;

            if (name == "HammerHead")
            {
                // HammerHead 是命中判定锚点，只剥掉网格和碰撞体，节点本身留着
                var mf = old.GetComponent<MeshFilter>(); if (mf != null) Object.DestroyImmediate(mf);
                var mr = old.GetComponent<MeshRenderer>(); if (mr != null) Object.DestroyImmediate(mr);
                var col = old.GetComponent<Collider>(); if (col != null) Object.DestroyImmediate(col);
            }
            else
            {
                Object.DestroyImmediate(old.gameObject);
            }
        }

        var headAnchor = hammer.transform.Find("HammerHead");
        if (headAnchor == null)
        {
            headAnchor = new GameObject("HammerHead").transform;
            headAnchor.SetParent(hammer.transform, false);
        }

        // 2) 放新模型，按"世界高度 0.40 米"定缩放（和原来手搓的锤子一样大）
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(HammerModel);
        if (asset == null) { Debug.LogError("[HammerModelBuilder] 模型不存在: " + HammerModel); return; }

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(asset, hammer.transform);
        visual.name = VisualName;

        float assetHeight = MeasureHeight(asset);
        if (assetHeight <= 0.001f) assetHeight = 0.18f;

        float rootScale = hammer.transform.lossyScale.y;
        if (rootScale <= 0.001f) rootScale = 1f;
        const float targetWorldHeight = 0.34f;
        float scale = targetWorldHeight / rootScale / assetHeight;
        visual.transform.localScale = Vector3.one * scale;

        // 模型是"柄在下、头在上"，把整把锤子的中点对到握把原点，和原来的摆法一致
        float localHeight = assetHeight * scale;
        visual.transform.localPosition = new Vector3(0f, -localHeight * 0.5f, 0f);
        visual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        // 3) 把命中锚点挪到新锤头中心
        float modelTopY = visual.transform.localPosition.y + localHeight;
        float headThickness = MeasurePartHeight(asset, "head") ; // 兜底用整体比例
        if (headThickness <= 0.001f) headThickness = assetHeight * 0.28f;
        float headCenterY = modelTopY - headThickness * scale * 0.5f;
        headAnchor.localPosition = new Vector3(0f, headCenterY, 0f);
        headAnchor.localScale = Vector3.one;

        // 4) 写回引用（hammerHead 字段），并保证命中半径和锤头视觉尺寸匹配
        var so = new SerializedObject(controller);
        var headProp = so.FindProperty("hammerHead");
        if (headProp != null) headProp.objectReferenceValue = headAnchor;
        var radiusProp = so.FindProperty("hitRadius");
        if (radiusProp != null) radiusProp.floatValue = 0.14f;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[HammerModelBuilder] 锤子模型已替换，缩放 {scale:F2}，锤头锚点 y={headCenterY:F3}");
    }

    static float MeasureHeight(GameObject asset)
    {
        var rs = asset.GetComponentsInChildren<MeshRenderer>(true);
        if (rs.Length == 0) return 0f;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.size.y;
    }

    /// <summary>模型里如果有单独命名的锤头部件，用它算锤头厚度；没有就返回 0 走整体比例兜底。</summary>
    static float MeasurePartHeight(GameObject asset, string keyword)
    {
        foreach (var r in asset.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.name.ToLowerInvariant().Contains(keyword)) return r.bounds.size.y;
        }
        return 0f;
    }
}