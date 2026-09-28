using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// =============================================================
// 一鍵設定：Unity 上方選單 Tools → 九宮格切割 → 一鍵設定到目前場景
//   1. 建立九宮格用的材質（Assets/GridCut/Materials）
//   2. 在場景建立 GridCutController 並連好 MeshCutter / AI 黑板
//   3. 在 CutTarget_Cube Prefab 和場景裡的方塊加上 GridCutTarget
//   4. 存檔
// 重複執行也沒關係，不會重複建立
// =============================================================
public static class GridCutSetup
{
    private const string RootFolder = "Assets/GridCut";
    private const string MaterialFolder = "Assets/GridCut/Materials";
    private const string CubePrefabPath = "Assets/Prefabs/CutTarget_Cube.prefab";

    [MenuItem("Tools/九宮格切割/一鍵設定到目前場景")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("九宮格切割", "請先按停止（離開 Play 模式）再執行。", "好");
            return;
        }

        // 1. 材質
        EnsureFolder("Assets", "GridCut");
        EnsureFolder(RootFolder, "Materials");

        Material lineMat = GetOrCreateMaterial("GridLine", GridCutController.DefaultGridLineColor);
        Material dotMat = GetOrCreateMaterial("GridDot", GridCutController.DefaultDotColor);
        Material dotHoverMat = GetOrCreateMaterial("GridDotHover", GridCutController.DefaultDotHoverColor);
        Material dotSelectedMat = GetOrCreateMaterial("GridDotSelected", GridCutController.DefaultDotSelectedColor);
        Material dashMat = GetOrCreateMaterial("GridPreviewDash", GridCutController.DefaultDashColor);
        Material outlineMat = GetOrCreateMaterial("GridPreviewOutline", GridCutController.DefaultOutlineColor);

        AssetDatabase.SaveAssets();

        // 2. GridCutController
        GridCutController controller =
            Object.FindFirstObjectByType<GridCutController>(FindObjectsInactive.Include);

        if (controller == null)
        {
            GameObject go = new GameObject("GridCutController");
            Undo.RegisterCreatedObjectUndo(go, "Create GridCutController");
            controller = go.AddComponent<GridCutController>();
        }

        SerializedObject so = new SerializedObject(controller);

        SetRef(so, "meshCutter", Object.FindFirstObjectByType<MeshCutter>(FindObjectsInactive.Include));
        SetRef(so, "aiTutorManager", Object.FindFirstObjectByType<AITutorManager>(FindObjectsInactive.Include));
        SetRef(so, "gridLineMaterial", lineMat);
        SetRef(so, "dotMaterial", dotMat);
        SetRef(so, "dotHoverMaterial", dotHoverMat);
        SetRef(so, "dotSelectedMaterial", dotSelectedMat);
        SetRef(so, "previewDashMaterial", dashMat);
        SetRef(so, "previewOutlineMaterial", outlineMat);

        so.ApplyModifiedProperties();

        // 3a. Prefab（Reset 重生的方塊也會有九宮格）
        bool prefabUpdated = false;

        if (AssetDatabase.LoadAssetAtPath<GameObject>(CubePrefabPath) != null)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CubePrefabPath);

            try
            {
                if (root.GetComponent<GridCutTarget>() == null)
                {
                    root.AddComponent<GridCutTarget>();
                    prefabUpdated = true;
                }

                PrefabUtility.SaveAsPrefabAsset(root, CubePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        else
        {
            Debug.LogWarning("找不到 " + CubePrefabPath + "，只會設定場景裡的方塊");
        }

        // 3b. 場景裡的方塊
        int sceneCubes = 0;

        foreach (GameObject cube in GameObject.FindGameObjectsWithTag("Cuttable"))
        {
            // 如果是 CutTarget_Cube Prefab 的分身，Prefab 已經有了，不要再加一次
            bool fromCubePrefab =
                PrefabUtility.IsPartOfPrefabInstance(cube) &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(cube) == CubePrefabPath;

            if (!fromCubePrefab && cube.GetComponent<GridCutTarget>() == null)
            {
                Undo.AddComponent<GridCutTarget>(cube);
            }

            sceneCubes++;
        }

        // 4. 存檔
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        EditorUtility.DisplayDialog(
            "九宮格切割",
            "設定完成！\n\n" +
            "・材質：Assets/GridCut/Materials\n" +
            "・GridCutController：已放到場景\n" +
            "・Prefab：" + (prefabUpdated ? "已加上 GridCutTarget" : "原本就有 / 未找到") + "\n" +
            "・場景方塊：" + sceneCubes + " 個\n\n" +
            "按 Play 就能看到九宮格和點點。",
            "好");
    }

    private static void SetRef(SerializedObject so, string property, Object value)
    {
        SerializedProperty p = so.FindProperty(property);

        if (p != null && value != null)
        {
            p.objectReferenceValue = value;
        }
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";

        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (m == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }

        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);

        EditorUtility.SetDirty(m);

        return m;
    }
}
