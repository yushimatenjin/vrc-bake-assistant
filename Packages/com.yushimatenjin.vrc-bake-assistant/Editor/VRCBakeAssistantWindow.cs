#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRCBakeAssistant
{
    /// <summary>
    /// VRChat world creators向けのライトベイク補助MVP。
    /// Unity 2022.3.22f1 / Built-in Render Pipeline想定。
    /// VPM/UPM packageとして配布する想定です。Tools/VRC Bake Assistant から開けます。
    /// </summary>
    public sealed class VRCBakeAssistantWindow : EditorWindow
    {
        private const string RootFolder = "Assets/VRCBakeAssistant";
        private const string LightingFolder = RootFolder + "/LightingSettings";
        private const string ReflectionFolder = RootFolder + "/BakedReflectionProbes";
        private const string MaterialFolder = RootFolder + "/Materials";

        private BakePreset preset = BakePreset.PC_Balanced;
        private bool preferGpu = true;
        private bool includeInactive = true;
        private bool selectionOnly = false;
        private float probeSpacing = 3.0f;
        private float probePadding = 1.0f;
        private int maxProbeCount = 768;
        private int reflectionProbeResolution = 128;
        private bool reflectionProbeBoxProjection = true;
        private Vector2 scroll;
        private ScanReport lastReport;

        private enum BakePreset
        {
            Practice_Fast,
            Quest_Light,
            PC_Balanced,
            Final_Quality
        }

        private sealed class ScanReport
        {
            public int renderers;
            public int giRenderers;
            public int reflectionStaticRenderers;
            public int renderersWithoutUv2;
            public int lights;
            public int bakedLights;
            public int mixedLights;
            public int realtimeLights;
            public int lightProbeGroups;
            public int lightProbeCount;
            public int reflectionProbes;
            public bool hasLightingSettings;
            public bool autoGenerate;
            public readonly List<string> warnings = new List<string>();
        }

        [MenuItem("Tools/VRC Bake Assistant/Open")]
        public static void Open()
        {
            var window = GetWindow<VRCBakeAssistantWindow>("VRC Bake Assistant");
            window.minSize = new Vector2(460, 620);
            window.Show();
        }

        private void OnEnable()
        {
            Lightmapping.bakeStarted -= OnBakeStarted;
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Lightmapping.bakeStarted += OnBakeStarted;
            Lightmapping.bakeCompleted += OnBakeCompleted;
            EditorApplication.update -= RepaintWhileBaking;
            EditorApplication.update += RepaintWhileBaking;
        }

        private void OnDisable()
        {
            Lightmapping.bakeStarted -= OnBakeStarted;
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            EditorApplication.update -= RepaintWhileBaking;
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("VRChat World Bake Assistant", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "ライトマップ、Light Probe、Reflection Probeを“手順で進める”ためのMVPです。最初は練習シーンで流れを確認し、その後自分のワールドに適用してください。",
                MessageType.Info);

            DrawBakeStatus();
            DrawCommonOptions();
            DrawStep0PracticeScene();
            DrawStep1Scan();
            DrawStep2StaticAndUv();
            DrawStep3LightingPreset();
            DrawStep4ProbeGeneration();
            DrawStep5Bake();
            DrawStep6Cleanup();

            EditorGUILayout.EndScrollView();
        }

        private void DrawBakeStatus()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                if (Lightmapping.isRunning)
                {
                    EditorGUILayout.LabelField("Bake Running", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"Progress: {Lightmapping.buildProgress:P1}");
                    if (GUILayout.Button("Cancel Bake"))
                    {
                        Lightmapping.Cancel();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("Bake Status: Idle");
                }
            }
        }

        private void DrawCommonOptions()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("共通オプション", EditorStyles.boldLabel);
                preset = (BakePreset)EditorGUILayout.EnumPopup("プリセット", preset);
                preferGpu = EditorGUILayout.ToggleLeft("Progressive GPUを優先する", preferGpu);
                includeInactive = EditorGUILayout.ToggleLeft("非アクティブなオブジェクトも対象にする", includeInactive);
                selectionOnly = EditorGUILayout.ToggleLeft("選択オブジェクトだけを対象にする", selectionOnly);
            }
        }

        private void DrawStep0PracticeScene()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 0: 練習シーン", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("床・壁・ライト・反射球を持つ簡単な練習シーンを作ります。");
                if (GUILayout.Button("練習用シーンを新規作成"))
                {
                    CreatePracticeScene();
                    lastReport = ScanScene();
                }
            }
        }

        private void DrawStep1Scan()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 1: シーン診断", EditorStyles.boldLabel);
                if (GUILayout.Button("現在のシーンを診断"))
                {
                    lastReport = ScanScene();
                }

                if (lastReport != null)
                {
                    EditorGUILayout.LabelField($"Renderer: {lastReport.renderers} / Contribute GI: {lastReport.giRenderers}");
                    EditorGUILayout.LabelField($"Light: {lastReport.lights}  Baked:{lastReport.bakedLights} Mixed:{lastReport.mixedLights} Realtime:{lastReport.realtimeLights}");
                    EditorGUILayout.LabelField($"Light Probe Group: {lastReport.lightProbeGroups} / Probe数: {lastReport.lightProbeCount}");
                    EditorGUILayout.LabelField($"Reflection Probe: {lastReport.reflectionProbes}");

                    foreach (var warning in lastReport.warnings)
                    {
                        EditorGUILayout.HelpBox(warning, MessageType.Warning);
                    }
                }
            }
        }

        private void DrawStep2StaticAndUv()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 2: Static / UVチェック", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("ライトマップに焼きたい床・壁・家具は Contribute GI を有効にします。UV2が無いFBXはGenerate Lightmap UVsを自動ONにできます。");

                if (GUILayout.Button(selectionOnly ? "選択RendererをContribute GI化" : "シーン内RendererをContribute GI化"))
                {
                    int changed = SetContributeGIForTargets();
                    Debug.Log($"[VRC Bake Assistant] Contribute GI set: {changed} objects");
                    lastReport = ScanScene();
                }

                if (GUILayout.Button("UV2が無いモデルのGenerate Lightmap UVsを有効化"))
                {
                    int changed = EnableGenerateLightmapUvsForModelImporters();
                    Debug.Log($"[VRC Bake Assistant] ModelImporter.generateSecondaryUV enabled: {changed} assets");
                    AssetDatabase.Refresh();
                    lastReport = ScanScene();
                }
            }
        }

        private void DrawStep3LightingPreset()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 3: Lighting Settings", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("選んだプリセットのLighting Settings Assetを作成・更新して、現在のシーンに割り当てます。");

                if (GUILayout.Button("プリセットを適用"))
                {
                    ApplyLightingPreset(preset);
                    lastReport = ScanScene();
                }
            }
        }

        private void DrawStep4ProbeGeneration()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 4: Probe生成", EditorStyles.boldLabel);
                probeSpacing = EditorGUILayout.Slider("Light Probe間隔", probeSpacing, 1.0f, 10.0f);
                probePadding = EditorGUILayout.Slider("Bounds余白", probePadding, 0.0f, 5.0f);
                maxProbeCount = EditorGUILayout.IntSlider("Light Probe最大数", maxProbeCount, 64, 3000);
                reflectionProbeResolution = EditorGUILayout.IntPopup("Reflection Probe解像度", reflectionProbeResolution,
                    new[] { "64", "128", "256", "512" },
                    new[] { 64, 128, 256, 512 });
                reflectionProbeBoxProjection = EditorGUILayout.ToggleLeft("Box Projectionを有効化", reflectionProbeBoxProjection);

                if (GUILayout.Button(selectionOnly ? "選択BoundsからLight Probe Gridを作成" : "シーンBoundsからLight Probe Gridを作成"))
                {
                    CreateLightProbeGrid();
                    lastReport = ScanScene();
                }

                if (GUILayout.Button(selectionOnly ? "選択BoundsからReflection Probeを作成" : "シーンBoundsからReflection Probeを1つ作成"))
                {
                    CreateReflectionProbeForBounds();
                    lastReport = ScanScene();
                }

                if (GUILayout.Button("選択RendererのProbe参照を推奨設定へ"))
                {
                    int count = ConfigureSelectedRendererProbeUsage();
                    Debug.Log($"[VRC Bake Assistant] Renderer probe usage configured: {count}");
                }
            }
        }

        private void DrawStep5Bake()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 5: Bake", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Lightmapping.BakeAsyncで非同期ベイクを開始します。Reflection Probeのみの個別ベイクも可能です。");

                using (new EditorGUI.DisabledScope(Lightmapping.isRunning))
                {
                    if (GUILayout.Button("Generate Lighting / BakeAsync"))
                    {
                        StartBakeAsync();
                    }

                    if (GUILayout.Button("Baked Reflection Probeだけをベイク"))
                    {
                        BakeAllBakedReflectionProbes();
                    }
                }
            }
        }

        private void DrawStep6Cleanup()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Step 6: やり直し・掃除", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Lighting Dataの削除は元に戻せません。必要なら先にシーンとプロジェクトをバックアップしてください。", MessageType.Warning);

                if (GUILayout.Button("Lighting Data Assetをクリア"))
                {
                    if (EditorUtility.DisplayDialog("Lighting Dataを削除", "現在のLighting Data Assetと関連ライトマップを削除します。続行しますか？", "削除", "キャンセル"))
                    {
                        Lightmapping.ClearLightingDataAsset();
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                    }
                }
            }
        }

        private ScanReport ScanScene()
        {
            var report = new ScanReport();

            if (Lightmapping.TryGetLightingSettings(out var settings))
            {
                report.hasLightingSettings = settings != null;
                report.autoGenerate = settings != null && settings.autoGenerate;
            }

            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;

                report.renderers++;
                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                bool contributeGI = (flags & StaticEditorFlags.ContributeGI) != 0;
                bool reflectionStatic = (flags & StaticEditorFlags.ReflectionProbeStatic) != 0;

                if (contributeGI) report.giRenderers++;
                if (reflectionStatic) report.reflectionStaticRenderers++;

                if (contributeGI && RendererHasMissingUv2(renderer))
                {
                    report.renderersWithoutUv2++;
                }
            }

            foreach (var light in FindSceneComponents<Light>(includeInactive))
            {
                report.lights++;
                switch (light.lightmapBakeType)
                {
                    case LightmapBakeType.Baked:
                        report.bakedLights++;
                        break;
                    case LightmapBakeType.Mixed:
                        report.mixedLights++;
                        break;
                    case LightmapBakeType.Realtime:
                        report.realtimeLights++;
                        break;
                }
            }

            foreach (var group in FindSceneComponents<LightProbeGroup>(includeInactive))
            {
                report.lightProbeGroups++;
                if (group.probePositions != null) report.lightProbeCount += group.probePositions.Length;
            }

            report.reflectionProbes = FindSceneComponents<ReflectionProbe>(includeInactive).Count();

            if (!report.hasLightingSettings)
                report.warnings.Add("Lighting Settings Assetが割り当てられていません。Step 3でプリセットを適用してください。");
            if (report.autoGenerate)
                report.warnings.Add("Auto GenerateがONです。VRChatワールド制作では手動ベイクのほうが事故が少ないため、Step 3でOFFにします。");
            if (report.renderers > 0 && report.giRenderers == 0)
                report.warnings.Add("Contribute GIのRendererがありません。ライトマップに焼きたい床・壁・家具をStatic化してください。");
            if (report.renderersWithoutUv2 > 0)
                report.warnings.Add($"UV2が無い可能性のあるContribute GI Rendererが {report.renderersWithoutUv2} 個あります。Generate Lightmap UVsを有効化してください。");
            if (report.lightProbeGroups == 0)
                report.warnings.Add("Light Probe Groupがありません。アバターや動的オブジェクトの馴染みが弱くなりやすいです。");
            if (report.reflectionProbes == 0)
                report.warnings.Add("Reflection Probeがありません。金属・ガラス・水面などの反射が環境に馴染みにくくなります。");

            Debug.Log($"[VRC Bake Assistant] Scan complete. Renderers:{report.renderers}, GI:{report.giRenderers}, Lights:{report.lights}");
            return report;
        }

        private int SetContributeGIForTargets()
        {
            int changed = 0;
            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;
                var go = renderer.gameObject;
                Undo.RecordObject(go, "Set Contribute GI");
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                var newFlags = flags | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
                if (newFlags != flags)
                {
                    GameObjectUtility.SetStaticEditorFlags(go, newFlags);
                    changed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return changed;
        }

        private int EnableGenerateLightmapUvsForModelImporters()
        {
            var paths = new HashSet<string>();

            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;
                if (!IsContributeGI(renderer.gameObject)) continue;
                if (!RendererHasMissingUv2(renderer)) continue;

                Mesh mesh = null;
                if (renderer.TryGetComponent<MeshFilter>(out var meshFilter))
                    mesh = meshFilter.sharedMesh;
                else if (renderer is SkinnedMeshRenderer skinned)
                    mesh = skinned.sharedMesh;

                if (mesh == null) continue;
                var path = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }

            int changed = 0;
            foreach (var path in paths)
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                {
                    if (!importer.generateSecondaryUV)
                    {
                        importer.generateSecondaryUV = true;
                        importer.SaveAndReimport();
                        changed++;
                    }
                }
            }

            return changed;
        }

        private void ApplyLightingPreset(BakePreset selectedPreset)
        {
            EnsureFolder(LightingFolder);

            string assetPath = $"{LightingFolder}/VRCBake_{selectedPreset}.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(assetPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = $"VRCBake_{selectedPreset}" };
                AssetDatabase.CreateAsset(settings, assetPath);
            }

            Undo.RecordObject(settings, "Apply Lighting Preset");

            settings.autoGenerate = false;
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.realtimeEnvironmentLighting = false;
            settings.lightmapper = preferGpu ? LightingSettings.Lightmapper.ProgressiveGPU : LightingSettings.Lightmapper.ProgressiveCPU;
            settings.prioritizeView = true;
            settings.indirectScale = 1.0f;
            settings.albedoBoost = 1.0f;
            settings.lightProbeSampleCountMultiplier = 4;
            settings.environmentImportanceSampling = true;
            settings.extractAO = false;

            switch (selectedPreset)
            {
                case BakePreset.Practice_Fast:
                    settings.lightmapResolution = 5;
                    settings.lightmapPadding = 2;
                    settings.lightmapMaxSize = 512;
                    settings.directSampleCount = 16;
                    settings.indirectSampleCount = 32;
                    settings.environmentSampleCount = 32;
                    settings.minBounces = 1;
                    settings.maxBounces = 1;
                    settings.ao = false;
                    settings.directionalityMode = LightmapsMode.NonDirectional;
                    settings.lightmapCompression = LightmapCompression.LowQuality;
                    settings.mixedBakeMode = MixedLightingMode.Subtractive;
                    break;

                case BakePreset.Quest_Light:
                    settings.lightmapResolution = 12;
                    settings.lightmapPadding = 4;
                    settings.lightmapMaxSize = 1024;
                    settings.directSampleCount = 32;
                    settings.indirectSampleCount = 128;
                    settings.environmentSampleCount = 64;
                    settings.minBounces = 1;
                    settings.maxBounces = 2;
                    settings.ao = true;
                    settings.aoMaxDistance = 1.0f;
                    settings.aoExponentDirect = 1.0f;
                    settings.aoExponentIndirect = 1.0f;
                    settings.directionalityMode = LightmapsMode.NonDirectional;
                    settings.lightmapCompression = LightmapCompression.NormalQuality;
                    settings.mixedBakeMode = MixedLightingMode.Subtractive;
                    break;

                case BakePreset.PC_Balanced:
                    settings.lightmapResolution = 20;
                    settings.lightmapPadding = 4;
                    settings.lightmapMaxSize = 2048;
                    settings.directSampleCount = 64;
                    settings.indirectSampleCount = 256;
                    settings.environmentSampleCount = 128;
                    settings.minBounces = 1;
                    settings.maxBounces = 3;
                    settings.ao = true;
                    settings.aoMaxDistance = 1.5f;
                    settings.aoExponentDirect = 1.0f;
                    settings.aoExponentIndirect = 1.0f;
                    settings.directionalityMode = LightmapsMode.CombinedDirectional;
                    settings.lightmapCompression = LightmapCompression.NormalQuality;
                    settings.mixedBakeMode = MixedLightingMode.Shadowmask;
                    break;

                case BakePreset.Final_Quality:
                    settings.lightmapResolution = 35;
                    settings.lightmapPadding = 6;
                    settings.lightmapMaxSize = 4096;
                    settings.directSampleCount = 128;
                    settings.indirectSampleCount = 512;
                    settings.environmentSampleCount = 256;
                    settings.minBounces = 2;
                    settings.maxBounces = 4;
                    settings.ao = true;
                    settings.aoMaxDistance = 2.0f;
                    settings.aoExponentDirect = 1.0f;
                    settings.aoExponentIndirect = 1.0f;
                    settings.directionalityMode = LightmapsMode.CombinedDirectional;
                    settings.lightmapCompression = LightmapCompression.HighQuality;
                    settings.mixedBakeMode = MixedLightingMode.Shadowmask;
                    break;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Lightmapping.lightingSettings = settings;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[VRC Bake Assistant] Applied lighting preset: {selectedPreset} -> {assetPath}");
        }

        private void CreateLightProbeGrid()
        {
            if (!TryGetTargetBounds(out var bounds))
            {
                EditorUtility.DisplayDialog("Light Probe作成失敗", "対象RendererのBoundsが見つかりません。", "OK");
                return;
            }

            bounds.Expand(probePadding * 2f);
            var positions = GenerateProbePositions(bounds, probeSpacing, maxProbeCount);
            if (positions.Count == 0)
            {
                EditorUtility.DisplayDialog("Light Probe作成失敗", "Probe位置を生成できませんでした。", "OK");
                return;
            }

            var existing = GameObject.Find("__VRC_BakeAssistant_LightProbes");
            var go = existing != null ? existing : new GameObject("__VRC_BakeAssistant_LightProbes");
            if (existing == null)
                Undo.RegisterCreatedObjectUndo(go, "Create Light Probe Group");
            else
                Undo.RecordObject(go, "Update Light Probe Group");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var group = go.GetComponent<LightProbeGroup>();
            if (group == null) group = go.AddComponent<LightProbeGroup>();
            Undo.RecordObject(group, "Set Light Probe Positions");
            group.probePositions = positions.ToArray();
            EditorUtility.SetDirty(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log($"[VRC Bake Assistant] Light probes generated: {positions.Count}");
        }

        private static List<Vector3> GenerateProbePositions(Bounds bounds, float spacing, int maxCount)
        {
            spacing = Mathf.Max(0.5f, spacing);
            var result = new List<Vector3>();

            for (int attempt = 0; attempt < 8; attempt++)
            {
                result.Clear();
                float minX = bounds.min.x;
                float maxX = bounds.max.x;
                float minZ = bounds.min.z;
                float maxZ = bounds.max.z;
                float floorY = bounds.min.y;
                float ceilY = bounds.max.y;

                var yLayers = new List<float>
                {
                    floorY + 0.4f,
                    floorY + 1.6f,
                    Mathf.Min(floorY + 3.0f, ceilY)
                };

                if (ceilY - floorY > 5f)
                {
                    for (float y = floorY + 4.5f; y < ceilY; y += spacing)
                        yLayers.Add(y);
                }

                for (float x = minX; x <= maxX; x += spacing)
                {
                    for (float z = minZ; z <= maxZ; z += spacing)
                    {
                        foreach (float y in yLayers)
                        {
                            if (y >= bounds.min.y && y <= bounds.max.y)
                                result.Add(new Vector3(x, y, z));
                        }
                    }
                }

                if (result.Count <= maxCount) break;
                spacing *= 1.35f;
            }

            return result.Take(maxCount).ToList();
        }

        private void CreateReflectionProbeForBounds()
        {
            if (!TryGetTargetBounds(out var bounds))
            {
                EditorUtility.DisplayDialog("Reflection Probe作成失敗", "対象RendererのBoundsが見つかりません。", "OK");
                return;
            }

            bounds.Expand(probePadding * 2f);
            var go = new GameObject(selectionOnly ? "__VRC_ReflectionProbe_SelectedBounds" : "__VRC_ReflectionProbe_SceneBounds");
            Undo.RegisterCreatedObjectUndo(go, "Create Reflection Probe");
            go.transform.position = bounds.center;
            go.transform.rotation = Quaternion.identity;

            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = bounds.size;
            probe.center = Vector3.zero;
            probe.resolution = reflectionProbeResolution;
            probe.hdr = true;
            probe.boxProjection = reflectionProbeBoxProjection;
            probe.blendDistance = Mathf.Max(0.1f, Mathf.Min(bounds.extents.magnitude * 0.1f, 3.0f));
            probe.importance = 1;
            probe.intensity = 1.0f;
            probe.shadowDistance = 50.0f;
            probe.cullingMask = ~0;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = go;
            Debug.Log($"[VRC Bake Assistant] Reflection Probe created: {go.name}");
        }

        private int ConfigureSelectedRendererProbeUsage()
        {
            int changed = 0;
            foreach (var renderer in Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<Renderer>(true)).Distinct())
            {
                Undo.RecordObject(renderer, "Configure Probe Usage");
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                EditorUtility.SetDirty(renderer);
                changed++;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return changed;
        }

        private void StartBakeAsync()
        {
            if (!Lightmapping.TryGetLightingSettings(out var settings) || settings == null)
            {
                ApplyLightingPreset(preset);
                Lightmapping.TryGetLightingSettings(out settings);
            }

            if (settings != null && settings.autoGenerate)
            {
                settings.autoGenerate = false;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            bool started = Lightmapping.BakeAsync();
            if (!started)
            {
                EditorUtility.DisplayDialog("Bake開始失敗", "BakeAsyncを開始できませんでした。Consoleの警告を確認してください。", "OK");
            }
        }

        private void BakeAllBakedReflectionProbes()
        {
            EnsureFolder(ReflectionFolder);
            var probes = FindSceneComponents<ReflectionProbe>(includeInactive)
                .Where(p => p.mode == ReflectionProbeMode.Baked)
                .ToArray();

            if (probes.Length == 0)
            {
                EditorUtility.DisplayDialog("Reflection Probeなし", "Baked Reflection Probeが見つかりません。", "OK");
                return;
            }

            try
            {
                for (int i = 0; i < probes.Length; i++)
                {
                    var probe = probes[i];
                    EditorUtility.DisplayProgressBar("Bake Reflection Probes", probe.name, (float)i / probes.Length);
                    string safeName = MakeSafeFileName(probe.name);
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{ReflectionFolder}/{safeName}.exr");
                    bool ok = Lightmapping.BakeReflectionProbe(probe, path);
                    if (!ok) Debug.LogError($"[VRC Bake Assistant] Failed to bake Reflection Probe: {probe.name}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }

        private void CreatePracticeScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureFolder(MaterialFolder);

            var wallMat = CreateOrLoadMaterial("VRCBake_Practice_WarmWall", new Color(0.78f, 0.72f, 0.62f), 0f, 0.35f);
            var floorMat = CreateOrLoadMaterial("VRCBake_Practice_Floor", new Color(0.45f, 0.42f, 0.38f), 0f, 0.45f);
            var metalMat = CreateOrLoadMaterial("VRCBake_Practice_Metal", new Color(0.8f, 0.75f, 0.68f), 1f, 0.9f);
            var blueMat = CreateOrLoadMaterial("VRCBake_Practice_Blue", new Color(0.2f, 0.35f, 0.75f), 0f, 0.5f);

            CreateCube("Floor_Static", new Vector3(0, -0.05f, 0), new Vector3(10, 0.1f, 10), floorMat, true);
            CreateCube("BackWall_Static", new Vector3(0, 2.5f, 5), new Vector3(10, 5, 0.2f), wallMat, true);
            CreateCube("LeftWall_Static", new Vector3(-5, 2.5f, 0), new Vector3(0.2f, 5, 10), wallMat, true);
            CreateCube("RightWall_Static", new Vector3(5, 2.5f, 0), new Vector3(0.2f, 5, 10), wallMat, true);
            CreateCube("StaticBox_A", new Vector3(-2.5f, 0.5f, 1.5f), Vector3.one, blueMat, true);
            CreateCube("StaticBox_B", new Vector3(2.2f, 0.75f, 0.5f), new Vector3(1.5f, 1.5f, 1.5f), wallMat, true);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Reflection_Check_Sphere";
            sphere.transform.position = new Vector3(0, 1.0f, 0);
            sphere.transform.localScale = Vector3.one * 1.2f;
            sphere.GetComponent<Renderer>().sharedMaterial = metalMat;

            var sunGo = new GameObject("Mixed Directional Light");
            sunGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.intensity = 1.0f;
            sun.shadows = LightShadows.Soft;

            var pointGo = new GameObject("Baked Warm Point Light");
            pointGo.transform.position = new Vector3(-2, 2.5f, -2);
            var point = pointGo.AddComponent<Light>();
            point.type = LightType.Point;
            point.lightmapBakeType = LightmapBakeType.Baked;
            point.intensity = 2.5f;
            point.range = 6;
            point.color = new Color(1.0f, 0.78f, 0.55f);
            point.shadows = LightShadows.Soft;

            var cameraGo = new GameObject("Preview Camera");
            cameraGo.transform.position = new Vector3(0, 2.5f, -8);
            cameraGo.transform.rotation = Quaternion.Euler(15, 0, 0);
            cameraGo.AddComponent<Camera>();
            cameraGo.tag = "MainCamera";

            ApplyLightingPreset(BakePreset.Practice_Fast);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.reflectionIntensity = 0.5f;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[VRC Bake Assistant] Practice scene created.");
        }

        private static GameObject CreateCube(string name, Vector3 position, Vector3 scale, Material material, bool contributeGI)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            if (contributeGI)
            {
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
            }

            return go;
        }

        private static Material CreateOrLoadMaterial(string name, Color color, float metallic, float smoothness)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            var shader = Shader.Find("Standard");
            mat = new Material(shader) { name = name, color = color };
            if (shader != null)
            {
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Glossiness", smoothness);
            }
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private IEnumerable<Renderer> GetTargetRenderers()
        {
            if (selectionOnly && Selection.gameObjects.Length > 0)
            {
                return Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<Renderer>(includeInactive)).Distinct();
            }

            return FindSceneComponents<Renderer>(includeInactive);
        }

        private static IEnumerable<T> FindSceneComponents<T>(bool includeInactiveObjects) where T : Component
        {
            foreach (var component in Resources.FindObjectsOfTypeAll<T>())
            {
                if (component == null) continue;
                var go = component.gameObject;
                if (go == null) continue;
                if (!go.scene.IsValid()) continue;
                if (EditorUtility.IsPersistent(go)) continue;
                if (!includeInactiveObjects && !go.activeInHierarchy) continue;
                yield return component;
            }
        }

        private static bool IsBakeTargetRenderer(Renderer renderer)
        {
            if (renderer == null) return false;
            if (renderer is ParticleSystemRenderer) return false;
            if (renderer is TrailRenderer) return false;
            if (renderer is LineRenderer) return false;
            return renderer.enabled;
        }

        private static bool RendererHasMissingUv2(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer.TryGetComponent<MeshFilter>(out var meshFilter)) mesh = meshFilter.sharedMesh;
            if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            if (mesh == null) return false;
            var uv2 = mesh.uv2;
            return uv2 == null || uv2.Length == 0;
        }

        private static bool IsContributeGI(GameObject go)
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            return (flags & StaticEditorFlags.ContributeGI) != 0;
        }

        private bool TryGetTargetBounds(out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;

            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return initialized;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static string MakeSafeFileName(string fileName)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
            return string.IsNullOrWhiteSpace(fileName) ? "ReflectionProbe" : fileName;
        }

        private void OnBakeStarted()
        {
            Debug.Log("[VRC Bake Assistant] Bake started.");
        }

        private void OnBakeCompleted()
        {
            Debug.Log("[VRC Bake Assistant] Bake completed.");
            lastReport = ScanScene();
            Repaint();
        }

        private void RepaintWhileBaking()
        {
            if (Lightmapping.isRunning) Repaint();
        }
    }
}
#endif
