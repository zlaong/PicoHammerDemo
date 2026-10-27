using PockHammer.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一键把主场景搭成"卡通农场"关卡：草地、栅栏、玉米地、南瓜、树、栅栏外的杂物，
/// 并把占位方块换成南瓜模型。
///
/// 重新执行会先删掉旧的 Farm 根节点再重建，所以可以反复调。
/// 菜单：PockHammer / Build Farm Scene
/// </summary>
public static class FarmSceneBuilder
{
    const string Nature = "Assets/Art/Kenney/NatureKit/Models/";
    const string Food = "Assets/Art/Kenney/FoodKit/Models/";
    const string Surv = "Assets/Art/Kenney/SurvivalKit/Models/";

    [MenuItem("PockHammer/Build Farm Scene")]
    public static void Build()
    {
        // 0) 清掉上一次搭的农场，保证可重复执行
        var old = GameObject.Find("Farm");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("Farm");
        root.AddComponent<FarmEnvironmentPlacement>();

        // 1) 地面 + 泥地
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground_Grass";
        ground.transform.SetParent(root.transform, false);
        ground.transform.localPosition = Vector3.zero;
        ground.transform.localScale = Vector3.one * 12f; // Plane 原始 10x10 → 120x120 米，远处用雾收边
        ground.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("Farm_Ground", new Color(0.42f, 0.62f, 0.28f));
        MarkStatic(ground);

        var dirt = GameObject.CreatePrimitive(PrimitiveType.Plane);
        dirt.name = "Ground_Dirt";
        dirt.transform.SetParent(root.transform, false);
        dirt.transform.localPosition = new Vector3(0f, 0.02f, 1.0f);
        dirt.transform.localScale = Vector3.one * 0.32f;
        dirt.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("Farm_Dirt", new Color(0.46f, 0.32f, 0.20f));
        MarkStatic(dirt);

        // 2) 栅栏围一圈：8x8 米的院子，中心在 (0, 2)
        BuildFenceRing(root.transform);

        // 3) 玉米地（院子左边）+ 南瓜地（院子右边）
        BuildCropRows(root.transform);

        // 4) 外围树木
        Vector3[] trees =
        {
            new Vector3(-6.5f, 0f, 8f), new Vector3(6.5f, 0f, 8.5f), new Vector3(-7.5f, 0f, 3f),
            new Vector3(7.5f, 0f, 3.5f), new Vector3(-7f, 0f, -2.5f), new Vector3(6.5f, 0f, -3f),
            new Vector3(-2f, 0f, 10f), new Vector3(3f, 0f, 10.5f),
        };
        for (int i = 0; i < trees.Length; i++)
        {
            string model = (i % 3 == 0) ? "tree_pineTallA" : (i % 3 == 1 ? "tree_oak" : "tree_default");
            Spawn(Nature + model + ".fbx", root.transform, trees[i], 2.2f + (i % 3) * 0.25f, i * 47f);
        }

        Spawn(Nature + "tree_small.fbx", root.transform, new Vector3(-5.5f, 0f, 5.5f), 1.8f, 30f);
        Spawn(Nature + "tree_simple.fbx", root.transform, new Vector3(5.2f, 0f, -4.5f), 1.9f, 210f);

        // 5) 灌木 / 草 / 花 / 石头（院子内外点缀）
        Vector3[] bushes =
        {
            new Vector3(-4.8f, 0f, 6.2f), new Vector3(4.6f, 0f, 6.4f), new Vector3(-5.2f, 0f, -1.2f),
            new Vector3(5.4f, 0f, -1.5f), new Vector3(-3.6f, 0f, 5.2f), new Vector3(3.8f, 0f, 5.4f),
        };
        foreach (var p in bushes) Spawn(Nature + "plant_bush.fbx", root.transform, p, 1.9f, Random.Range(0f, 360f));
        Spawn(Nature + "plant_bushLarge.fbx", root.transform, new Vector3(0f, 0f, 8f), 2.2f, 15f);

        Vector3[] grasses =
        {
            new Vector3(-1.8f, 0f, 4.6f), new Vector3(1.9f, 0f, 4.8f), new Vector3(-2.6f, 0f, 0.4f),
            new Vector3(2.6f, 0f, 0.6f), new Vector3(-1.2f, 0f, -1.4f), new Vector3(1.4f, 0f, -1.5f),
            new Vector3(3.2f, 0f, 3.2f), new Vector3(-3.4f, 0f, 3.4f),
        };
        foreach (var p in grasses) Spawn(Nature + "grass.fbx", root.transform, p, 2.4f, Random.Range(0f, 360f));
        Spawn(Nature + "grass_large.fbx", root.transform, new Vector3(-0.6f, 0f, 5.2f), 2.2f, 40f);
        Spawn(Nature + "grass_large.fbx", root.transform, new Vector3(0.9f, 0f, 5.4f), 2.0f, 160f);

        Vector3[] flowers =
        {
            new Vector3(-2.2f, 0f, 3.6f), new Vector3(2.3f, 0f, 3.7f), new Vector3(-3f, 0f, 5.6f),
            new Vector3(3.1f, 0f, 5.5f), new Vector3(0.6f, 0f, -1.8f), new Vector3(-0.9f, 0f, -1.9f),
        };
        for (int i = 0; i < flowers.Length; i++)
        {
            string m = (i % 2 == 0) ? "flower_redA" : "flower_yellowA";
            Spawn(Nature + m + ".fbx", root.transform, flowers[i], 2.2f, Random.Range(0f, 360f));
        }

        Vector3[] rocks =
        {
            new Vector3(-4.2f, 0f, 4.2f), new Vector3(4.4f, 0f, 4.4f), new Vector3(-2.9f, 0f, 7.2f),
            new Vector3(3.2f, 0f, 7.4f), new Vector3(-6f, 0f, 0.5f),
        };
        foreach (var p in rocks) Spawn(Nature + "rock_smallA.fbx", root.transform, p, 2.0f, Random.Range(0f, 360f));
        Spawn(Nature + "rock_largeA.fbx", root.transform, new Vector3(5.8f, 0f, 1.2f), 1.8f, 25f);
        Spawn(Nature + "stone_tallA.fbx", root.transform, new Vector3(-6.2f, 0f, 6.4f), 1.6f, 0f);

        // 6) 农场杂物：木箱、木桶、铁砧、木桩、柴堆、招牌、篝火
        Spawn(Surv + "box-large.fbx", root.transform, new Vector3(-2.9f, 0f, 0.2f), 0.9f, 18f);
        Spawn(Surv + "box.fbx", root.transform, new Vector3(-2.6f, 0f, 1.1f), 0.9f, -25f);
        Spawn(Food + "barrel.fbx", root.transform, new Vector3(3.3f, 0f, 0.1f), 1.0f, 0f);
        Spawn(Surv + "barrel-open.fbx", root.transform, new Vector3(3.6f, 0f, 1.2f), 0.9f, 70f);
        Spawn(Surv + "workbench-anvil.fbx", root.transform, new Vector3(-4.2f, 0f, -0.6f), 1.0f, 90f);
        Spawn(Nature + "stump_round.fbx", root.transform, new Vector3(4.3f, 0f, -0.8f), 1.6f, 0f);
        Spawn(Nature + "log_stack.fbx", root.transform, new Vector3(-4.4f, 0f, 2.6f), 1.6f, 12f);
        Spawn(Nature + "pot_large.fbx", root.transform, new Vector3(2.8f, 0f, 4.6f), 1.6f, 0f);
        Spawn(Nature + "sign.fbx", root.transform, new Vector3(1.8f, 0f, 5.6f), 1.6f, 200f);
        Spawn(Nature + "campfire_logs.fbx", root.transform, new Vector3(-1.4f, 0f, 6.4f), 1.5f, 0f);
        Spawn(Nature + "mushroom_red.fbx", root.transform, new Vector3(-3.8f, 0f, 5.9f), 2.5f, 0f);

        // 7) 南瓜靶子：脚下垫两层木箱，把南瓜抬到手能够到的高度
        BuildTargetStand(root.transform);

        // 8) 灯光：卡通风格要亮，主光偏暖
        var light = GameObject.Find("Directional Light");
        if (light != null)
        {
            var l = light.GetComponent<Light>();
            if (l != null)
            {
                l.color = new Color(1f, 0.96f, 0.88f);
                l.intensity = 1.35f;
            }
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.74f, 0.88f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.60f, 0.55f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.34f, 0.26f);

        // 远处用雾收边，避免看到草地平面的直边
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.012f;
        RenderSettings.fogColor = new Color(0.72f, 0.83f, 0.92f);

        // 9) 把占位方块换成南瓜模型（碰撞体保留，命中判定逻辑不变）
        SwapTargetToPumpkin();

        // 编辑器里也让南瓜站在木桶上，所见即所得
        var target = GameObject.Find("TargetCube");
        if (target != null)
        {
            target.transform.SetPositionAndRotation(new Vector3(0f, 1.4f, 0.9f), Quaternion.identity);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[FarmSceneBuilder] 农场场景搭建完成");
    }

    static void BuildFenceRing(Transform parent)
    {
        const float half = 4f;
        const float cz = 2f;
        for (int i = 0; i < 4; i++)
        {
            float t = -3f + i * 2f;
            Spawn(Nature + "fence_simple.fbx", parent, new Vector3(t, 0f, cz + half), 2.0f, 0f);      // 前
            Spawn(Nature + "fence_simple.fbx", parent, new Vector3(t, 0f, cz - half), 2.0f, 180f);    // 后
            Spawn(Nature + "fence_simple.fbx", parent, new Vector3(-half, 0f, cz + t), 2.0f, 90f);    // 左
            Spawn(Nature + "fence_simple.fbx", parent, new Vector3(half, 0f, cz + t), 2.0f, -90f);    // 右
        }
        Spawn(Nature + "fence_gate.fbx", parent, new Vector3(0f, 0f, cz - half), 2.4f, 180f);
    }

    static void BuildCropRows(Transform parent)
    {
        for (int i = 0; i < 3; i++)
        {
            float z = 1.4f + i * 1.5f;
            Spawn(Nature + "crops_dirtRow.fbx", parent, new Vector3(-2.7f, 0f, z), 2.0f, 0f);
            Spawn(Nature + "crops_cornStageD.fbx", parent, new Vector3(-3.1f, 0f, z), 1.8f, 0f);
            Spawn(Nature + "crops_cornStageD.fbx", parent, new Vector3(-2.4f, 0f, z + 0.1f), 1.6f, 0f);
        }
        Spawn(Nature + "crops_wheatStageB.fbx", parent, new Vector3(-3.5f, 0f, 4.9f), 1.8f, 0f);
        Spawn(Nature + "crops_dirtRowEnd.fbx", parent, new Vector3(-2.7f, 0f, 0.6f), 2.0f, 0f);

        Vector3[] pumpkins =
        {
            new Vector3(2.5f, 0f, 1.2f), new Vector3(3.3f, 0f, 1.0f),
            new Vector3(2.4f, 0f, 2.1f), new Vector3(3.4f, 0f, 2.2f),
            new Vector3(2.9f, 0f, 3.1f),
        };
        for (int i = 0; i < pumpkins.Length; i++)
            Spawn(Nature + "crop_pumpkin.fbx", parent, pumpkins[i], 1.8f, Random.Range(0f, 360f));
        Spawn(Nature + "crop_carrot.fbx", parent, new Vector3(2.0f, 0f, 2.6f), 1.8f, 0f);
        Spawn(Nature + "crop_turnip.fbx", parent, new Vector3(3.7f, 0f, 2.9f), 1.8f, 0f);
    }

    /// <summary>
    /// 南瓜摆在一根细木桩的小平台上，桩面 1.27 米高（胸腹之间）。
    /// 为什么不用木箱堆：木箱太宽，正好挡在玩家眼前；细柱子不挡视线。
    /// 高度和运行时的摆位参数对齐（眼睛 -0.2 ≈ 1.4）。
    /// </summary>
    static void BuildTargetStand(Transform parent)
    {
        const float standX = 0f, standZ = 0.9f;
        const float standHeight = 1.27f;

        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "TargetStand_Post";
        post.transform.SetParent(parent, false);
        post.transform.localPosition = new Vector3(standX, standHeight * 0.5f, standZ);
        post.transform.localScale = new Vector3(0.18f, standHeight * 0.5f, 0.18f);
        post.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("Farm_Wood", new Color(0.55f, 0.36f, 0.20f));
        Object.DestroyImmediate(post.GetComponent<Collider>());
        MarkStatic(post);

        var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
        top.name = "TargetStand_Top";
        top.transform.SetParent(parent, false);
        top.transform.localPosition = new Vector3(standX, standHeight + 0.04f, standZ);
        top.transform.localScale = new Vector3(0.36f, 0.08f, 0.36f);
        top.GetComponent<MeshRenderer>().sharedMaterial = GetMaterial("Farm_Wood", new Color(0.55f, 0.36f, 0.20f));
        Object.DestroyImmediate(top.GetComponent<Collider>());
        MarkStatic(top);

        // 运行时摆位：眼睛高度(约1.6) - 0.2 ≈ 1.4，南瓜底面 ≈ 1.27 正好落在平台上
        var placement = Object.FindFirstObjectByType<PockHammer.Gameplay.PlayerRelativePlacement>();
        if (placement != null)
        {
            var so = new SerializedObject(placement);
            var heightProp = so.FindProperty("targetHeightOffset");
            if (heightProp != null) heightProp.floatValue = -0.2f;
            var distProp = so.FindProperty("targetDistance");
            if (distProp != null) distProp.floatValue = 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void SwapTargetToPumpkin()
    {
        var target = GameObject.Find("TargetCube");
        if (target == null) return;

        // 去掉占位方块的网格，保留 BoxCollider（命中判定靠它）
        var mf = target.GetComponent<MeshFilter>();
        if (mf != null) Object.DestroyImmediate(mf);
        var mr = target.GetComponent<MeshRenderer>();
        if (mr != null) Object.DestroyImmediate(mr);

        foreach (Transform child in target.transform)
        {
            if (child.name.StartsWith("PumpkinVisual")) Object.DestroyImmediate(child.gameObject);
        }

        var pumpkin = Spawn(Food + "pumpkin.fbx", target.transform, Vector3.zero, 1.15f, 0f);
        if (pumpkin != null)
        {
            pumpkin.name = "PumpkinVisual";
            pumpkin.transform.localPosition = new Vector3(0f, -0.115f, 0f); // 让南瓜居中在碰撞盒里
            pumpkin.isStatic = false;
        }
    }

    static GameObject Spawn(string path, Transform parent, Vector3 localPos, float scale, float yaw)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null)
        {
            Debug.LogWarning("[FarmSceneBuilder] 模型不存在: " + path);
            return null;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        go.name = System.IO.Path.GetFileNameWithoutExtension(path) + "_" + parent.childCount;
        MarkStatic(go);
        return go;
    }

    static float MeasureHeight(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) return 1f;
        var renderers = asset.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0) return 1f;
        return renderers[0].bounds.size.y;
    }

    static void MarkStatic(GameObject go)
    {
        GameObjectUtility.SetStaticEditorFlags(go,
            StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
    }

    static Material GetMaterial(string name, Color color)
    {
        string dir = "Assets/Materials";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets", "Materials");
        string path = dir + "/" + name + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}