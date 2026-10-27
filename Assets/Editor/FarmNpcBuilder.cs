using PockHammer.Targets;
using PockHammer.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一键往农场里放 NPC：两头牛、一匹马在栅栏外的草地上踱步，一只羊一只猪原地吃草，
/// 两个农夫在院子里小范围走动。素材是 Quaternius 的 CC0 低模（带骨骼动画）。
///
/// 重复执行会先删掉旧的 NPCs 根节点再重建。
/// 菜单：PockHammer / Add Farm NPCs
/// </summary>
public static class FarmNpcBuilder
{
    const string AnimalDir = "Assets/Art/Quaternius/FarmAnimals/";
    const string HumanDir = "Assets/Art/Quaternius/Humans/";
    const string MaterialDir = "Assets/Art/Quaternius/Materials/";

    /// <summary>一个 NPC 的摆放配置。</summary>
    private struct NpcSpec
    {
        public string ModelPath;
        public string Name;
        public Vector3 Position;
        public float Height;        // 想要的真实身高（米）
        public float WanderRadius;
        public bool Wanders;
        public Color Color;
        public float Durability;    // >0 表示这只动物可以被锤（0 = 农夫，纯装饰）

        public NpcSpec(string modelPath, string name, Vector3 position, float height,
            float wanderRadius, bool wanders, Color color, float durability = 0f)
        {
            ModelPath = modelPath;
            Name = name;
            Position = position;
            Height = height;
            WanderRadius = wanderRadius;
            Wanders = wanders;
            Color = color;
            Durability = durability;
        }
    }

    [MenuItem("PockHammer/Add Farm NPCs")]
    public static void Build()
    {
        var old = GameObject.Find("NPCs");
        if (old != null)
        {
            Object.DestroyImmediate(old);
        }

        var root = new GameObject("NPCs");
        root.AddComponent<FarmAnchorFollower>();

        // 动物全部摆在栅栏外更远的空草地上：它们被打到会长到 5 倍大，
        // 贴着栅栏放的话长起来一定会插进栅栏和树里（见 4.23）。用的是"甩锤子"打（飞行 20 米）。
        var specs = new[]
        {
            // 会走动的
            new NpcSpec(AnimalDir + "Cow.fbx", "Cow_A", new Vector3(-4.8f, 0f, 12.8f), 1.5f, 1.6f, true, new Color(0.93f, 0.92f, 0.88f), 160f),
            new NpcSpec(AnimalDir + "Cow.fbx", "Cow_B", new Vector3(4.8f, 0f, 13.4f), 1.4f, 1.6f, true, new Color(0.85f, 0.82f, 0.76f), 160f),
            new NpcSpec(AnimalDir + "Horse.fbx", "Horse_A", new Vector3(0f, 0f, 15.6f), 1.9f, 1.4f, true, new Color(0.52f, 0.35f, 0.22f), 160f),

            // 只会吃草（这两个模型没有走路动画）
            new NpcSpec(AnimalDir + "Sheep.fbx", "Sheep_A", new Vector3(-2.6f, 0f, 11.2f), 0.95f, 0f, false, new Color(0.95f, 0.94f, 0.9f), 160f),
            new NpcSpec(AnimalDir + "Pig.fbx", "Pig_A", new Vector3(2.6f, 0f, 11.0f), 0.8f, 0f, false, new Color(0.95f, 0.66f, 0.62f), 160f),

            // 院子里：农夫小范围走动，离南瓜和玩家都留出距离（纯装饰，不能砸）
            new NpcSpec(HumanDir + "Animated Human.fbx", "Farmer_A", new Vector3(-2.5f, 0f, 4.4f), 1.75f, 0.9f, true, new Color(0.32f, 0.45f, 0.68f)),
            new NpcSpec(HumanDir + "Animated Human.fbx", "Farmer_B", new Vector3(2.6f, 0f, 4.9f), 1.7f, 0.9f, true, new Color(0.7f, 0.38f, 0.3f)),
        };

        foreach (NpcSpec spec in specs)
        {
            Spawn(root.transform, spec);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[FarmNpcBuilder] 放好了 {specs.Length} 个 NPC");
    }

    private static void Spawn(Transform root, NpcSpec spec)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
        if (model == null)
        {
            Debug.LogError($"[FarmNpcBuilder] 找不到模型 {spec.ModelPath}");
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = spec.Name;
        instance.transform.SetParent(root, false);
        instance.transform.SetPositionAndRotation(spec.Position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

        // 这个模型的骨骼层级是 100 倍缩放、网格本身只有几厘米，
        // Unity 画蒙皮时只认骨骼矩阵、不认根节点缩放，所以模型默认只有 2 厘米高。
        // 这里按"骨骼真实蒙皮出来的高度"反推出要乘多少倍，才能长到想要的身高。
        float skinnedHeight = MeasureSkinnedHeight(instance);
        instance.transform.localScale = Vector3.one * (skinnedHeight > 0.0001f ? spec.Height / skinnedHeight : 1f);

        // 这个 FBX 导入后网格自带的包围盒是空的（0.00），Unity 会认为模型"不在画面里"直接不画，
        // 所以要按蒙皮后的真实形状重新算一遍包围盒，并强制每帧更新。
        FixSkinnedBounds(instance, spec.Height);

        var npc = instance.GetComponent<FarmNpc>();
        if (npc == null)
        {
            npc = instance.AddComponent<FarmNpc>();
        }

        var so = new SerializedObject(npc);
        so.FindProperty("wanders").boolValue = spec.Wanders;
        so.FindProperty("wanderRadius").floatValue = spec.WanderRadius;
        so.FindProperty("walkSpeed").floatValue = spec.Wanders && spec.Height < 1.2f ? 0.45f : 0.65f;
        so.ApplyModifiedPropertiesWithoutUndo();

        ApplyColor(instance, spec.Color, spec.Name);

        if (spec.Durability > 0f)
        {
            MakeSmashable(instance, spec);
        }
    }

    /// <summary>
    /// 把动物变成"可砸物体"：加碰撞体 + 耐久 + 变大/爆开的视觉反馈。
    /// 动物被砸会长到 5 倍大再炸开，所以耐久要比南瓜高一些（默认 120，4 下左右）。
    /// </summary>
    private static void MakeSmashable(GameObject instance, NpcSpec spec)
    {
        Vector3 scale = instance.transform.lossyScale;
        float sx = Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
        float sy = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);

        var capsule = instance.AddComponent<CapsuleCollider>();
        capsule.direction = 1; // Y 轴
        capsule.center = new Vector3(0f, 0.5f * spec.Height / sy, 0f);
        capsule.height = spec.Height / sy;
        capsule.radius = 0.34f * spec.Height / sx;

        var target = instance.AddComponent<SmashableTarget>();
        var targetSo = new SerializedObject(target);
        targetSo.FindProperty("maxDurability").floatValue = spec.Durability;
        targetSo.ApplyModifiedPropertiesWithoutUndo();

        var visual = instance.AddComponent<TargetVisualFeedback>();
        var visualSo = new SerializedObject(visual);
        visualSo.FindProperty("maxGrowScale").floatValue = 5f;          // 最大 5 倍
        visualSo.FindProperty("growCompleteAtPercent").floatValue = 0.3f;  // 剩 30% 就长满 5 倍，最后一下才炸
        visualSo.FindProperty("maxChunkSize").floatValue = 1.1f;        // 5 倍大时碎块不封顶会非常夸张
        visualSo.FindProperty("chunkColor").colorValue = new Color(0.62f, 0.2f, 0.18f);
        visualSo.FindProperty("seedColor").colorValue = new Color(0.95f, 0.92f, 0.86f);
        visualSo.FindProperty("chunkCount").intValue = 18;
        visualSo.FindProperty("chunkSpeed").floatValue = 2.4f;
        visualSo.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// 按骨骼蒙皮算一遍模型真正画出来有多高（Unity 渲染蒙皮时只看骨骼，不看网格顶点）。
    /// </summary>
    private static float MeasureSkinnedHeight(GameObject instance)
    {
        var skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skinned == null || skinned.sharedMesh == null)
        {
            return 0f;
        }

        Mesh mesh = skinned.sharedMesh;
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bindposes = mesh.bindposes;
        Transform[] bones = skinned.bones;
        if (weights == null || weights.Length != vertices.Length || bindposes.Length == 0 || bones == null)
        {
            return 0f;
        }

        var matrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
        }

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            BoneWeight weight = weights[i];
            Vector3 point =
                matrices[weight.boneIndex0].MultiplyPoint3x4(vertices[i]) * weight.weight0 +
                matrices[weight.boneIndex1].MultiplyPoint3x4(vertices[i]) * weight.weight1 +
                matrices[weight.boneIndex2].MultiplyPoint3x4(vertices[i]) * weight.weight2 +
                matrices[weight.boneIndex3].MultiplyPoint3x4(vertices[i]) * weight.weight3;

            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        return maxY - minY;
    }

    private static void FixSkinnedBounds(GameObject instance, float worldHeight)
    {
        foreach (SkinnedMeshRenderer skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            // 模型原点在脚底，所以包围盒中心要抬到身高一半的位置。
            // 这里的数值是"渲染器本地空间"，要除以渲染器自身的世界缩放。
            Vector3 scale = skinned.transform.lossyScale;
            float sx = Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
            float sy = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
            float sz = Mathf.Max(Mathf.Abs(scale.z), 0.0001f);

            var center = new Vector3(0f, worldHeight * 0.5f / sy, 0f);
            var size = new Vector3(worldHeight * 1.5f / sx, worldHeight / sy, worldHeight * 1.5f / sz);

            // 注意：updateWhenOffscreen 打开时 Unity 会每帧用"实际蒙皮结果"覆盖 localBounds，
            // 而在编辑器/首帧里蒙皮还没算出来，包围盒会变成 0 → 被剔除。所以这里必须关掉它，
            // 用手动算好的包围盒。
            skinned.updateWhenOffscreen = false;
            skinned.localBounds = new Bounds(center, size);
        }
    }

    private static float MeasureHeight(GameObject instance)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (first)
            {
                bounds = renderer.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return first ? 1f : bounds.size.y;
    }

    /// <summary>模型自带材质是纯白的，这里给每个 NPC 一个自己的颜色。</summary>
    private static void ApplyColor(GameObject instance, Color color, string name)
    {
        Material material = GetOrCreateMaterial(name + "_Mat", color);
        if (material == null)
        {
            return;
        }

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var materials = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art/Quaternius/Materials"))
        {
            AssetDatabase.CreateFolder("Assets/Art/Quaternius", "Materials");
        }

        string path = MaterialDir + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(material);
        return material;
    }
}
