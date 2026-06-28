#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// VRChat world creators向けのライトベイク補助ツール。
    /// Unity 2022.3.22f1 / Built-in Render Pipeline想定。
    /// Tools/YushimaTenjin/VRC Bake Assistant から開けます。
    /// </summary>
    public sealed class VRCBakeAssistantWindow : EditorWindow
    {
        private const string RootFolder = "Assets/VRCBakeAssistant";
        private const string LightingFolder = RootFolder + "/LightingSettings";
        private const string ReflectionFolder = RootFolder + "/BakedReflectionProbes";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string ReportFolder = RootFolder + "/Reports";
        private const string BakeSnapshotAssetPath = ReportFolder + "/LastBakeSnapshot.json";

        private const string AssistantRootName = "VRC Bake Assistant Generated";
        private const string PracticeRootName = "00_SampleScene_練習シーン全体";
        private const string GuideRootName = "00_Guide_原点と説明";
        private const string GeometryRootName = "01_BakeTargets_焼く床壁家具";
        private const string DynamicRootName = "02_NotBaked_動く物と確認用";
        private const string LightsRootName = "03_Lights_ベイク用ライト";
        private const string ProbesRootName = "04_Probes_LightProbeとReflectionProbe";
        private const string CameraRootName = "05_Camera_確認用";
        private const string LightProbeGroupName = "LightProbeGroup_アバターの明るさ用";
        private const string ReflectionProbeName = "ReflectionProbe_反射確認用";
        private const string ProbeBoundsGuideName = "LightProbe_BoundsGuide_NotBaked";
        private const string AxisGuideName = "AxisGuide_XYZ_NotBaked";

        private BakePreset preset = BakePreset.QuickCheck;
        private bool preferGpu = true;
        private bool includeInactive = true;
        private bool selectionOnly = false;

        private float probeSpacing = 3.0f;
        private float probePadding = 1.0f;
        private int maxProbeCount = 768;
        private int reflectionProbeResolution = 128;
        private bool reflectionProbeBoxProjection = true;
        private bool useManualProbeBounds = false;
        private Vector3 manualProbeCenter = new Vector3(0f, 2.4f, 0f);
        private Vector3 manualProbeSize = new Vector3(11.5f, 5.2f, 11.5f);
        private bool showProbeAdvanced = false;

        private bool showBakeTargetList = false;
        private bool showOnlyNotContributeGI = false;
        private string rendererSearch = string.Empty;
        private int rendererListLimit = 50;
        private readonly List<RendererListItem> rendererItems = new List<RendererListItem>();

        private bool showLightList = false;
        private string lightSearch = string.Empty;
        private int lightListLimit = 50;
        private int rendererRefreshScanLimit = 1200;
        private bool rendererListUvCheck = false;
        private bool rendererListHitLimit = false;
        private bool lightListHitLimit = false;
        private const int LightRefreshScanLimit = 2000;
        private readonly List<LightListItem> lightItems = new List<LightListItem>();

        private Vector2 scroll;
        private ScanReport lastReport;
        private BakeSnapshot cachedSnapshot;

        private string statusMessage = "安全モード: Windowを開いただけではRenderer/Light一覧を自動取得しません。まずは練習シーン、または『現在のシーンを診断』から始めてください。";
        private MessageType statusType = MessageType.Info;
        private Action queuedAction;
        private string queuedActionLabel;

        private enum BakePreset
        {
            QuickCheck,
            QuestLight,
            PCStandard,
            FinalCheck
        }

        private static readonly BakePreset[] PresetValues =
        {
            BakePreset.QuickCheck,
            BakePreset.QuestLight,
            BakePreset.PCStandard,
            BakePreset.FinalCheck
        };

        private static readonly GUIContent[] PresetLabels =
        {
            new GUIContent("試し焼き（早い）", "短時間で光の方向・色・大まかな明るさを見るための設定です。完成品質ではありません。"),
            new GUIContent("Quest向け軽量", "Quest/Android向けに、Lightmap容量と負荷を抑えるための設定です。"),
            new GUIContent("PC向け標準", "PCワールドで見た目とベイク時間のバランスを取る設定です。迷ったらここから。"),
            new GUIContent("仕上げ確認（重い）", "最終確認用です。時間と容量が増えやすいので、最後だけ使う想定です。")
        };

        private sealed class ScanReport
        {
            public int renderers;
            public int giRenderers;
            public int notGiRenderers;
            public int reflectionStaticRenderers;
            public int renderersWithoutUv2;
            public int lights;
            public int enabledLights;
            public int bakedLights;
            public int mixedLights;
            public int realtimeLights;
            public int bakeRelevantLights;
            public int directionalLights;
            public int pointLights;
            public int spotLights;
            public int areaLights;
            public int disabledLights;
            public int lightProbeGroups;
            public int lightProbeCount;
            public int reflectionProbes;
            public int bakedReflectionProbes;
            public bool hasLightingSettings;
            public bool autoGenerate;
            public bool hasLightingDataAsset;
            public bool hasBakeSnapshot;
            public string snapshotCapturedAt = string.Empty;
            public int newBakeLightsSinceSnapshot;
            public int changedBakeLightsSinceSnapshot;
            public int removedBakeLightsSinceSnapshot;
            public readonly List<string> warnings = new List<string>();
            public readonly List<string> okMessages = new List<string>();
            public readonly List<string> changedLightNames = new List<string>();
        }

        private sealed class RendererListItem
        {
            public Renderer renderer;
            public string path;
            public bool contributeGI;
            public bool reflectionStatic;
            public bool missingUv2;
            public bool likelyDynamic;
            public string typeName;
        }

        private sealed class LightListItem
        {
            public Light light;
            public string path;
            public string typeName;
            public bool changedSinceLastBake;
            public bool newSinceLastBake;
        }

        [Serializable]
        private sealed class BakeSnapshot
        {
            public string capturedAt;
            public string unityVersion;
            public string scenePath;
            public string sceneName;
            public List<LightSnapshot> lights = new List<LightSnapshot>();
        }

        [Serializable]
        private sealed class LightSnapshot
        {
            public string globalId;
            public string path;
            public string signature;
            public string type;
            public string bakeType;
            public bool enabled;
        }

        [MenuItem("Tools/YushimaTenjin/VRC Bake Assistant", false, 1000)]
        public static void OpenFromYushimaTenjinMenu()
        {
            OpenWindow();
        }

        private static void OpenWindow()
        {
            var window = GetWindow<VRCBakeAssistantWindow>("ライトベイク手順");
            window.minSize = new Vector2(560, 760);
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
            cachedSnapshot = LoadBakeSnapshot();
        }

        private void OnDisable()
        {
            Lightmapping.bakeStarted -= OnBakeStarted;
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            EditorApplication.update -= RepaintWhileBaking;
        }

        private void OnGUI()
        {
            queuedAction = null;
            queuedActionLabel = null;

            scroll = EditorGUILayout.BeginScrollView(scroll);
            try
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("VRC Bake Assistant", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "VRChatワールドのライトベイクを、上から順番に確認できるようにするツールです。0.1.5では、0.1.3の見える化機能に加えてUnity 2022.3でのコンパイルエラーを修正しました。",
                    MessageType.Info);

                DrawStatusPanel();
                DrawBakeStatus();
                DrawTargetOptions();
                DrawStep0PracticeScene();
                DrawStep1Scan();
                DrawStep2StaticAndUv();
                DrawStep3LightingPreset();
                DrawStep4LightPlacement();
                DrawStep5ProbeGeneration();
                DrawStep6Bake();
                DrawStep7Cleanup();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }

            RunQueuedActionAfterGuiIfNeeded();
        }

        private void DrawStatusPanel()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("フィードバック", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(statusMessage, statusType);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("重複コピーを確認"))
                    {
                        QueueOperation("重複コピー確認", CheckDuplicateScriptCopies);
                    }

                    if (GUILayout.Button("一覧キャッシュを空にする"))
                    {
                        rendererItems.Clear();
                        lightItems.Clear();
                        SetStatus("Renderer/Light一覧キャッシュを空にしました。Windowを閉じずに軽くしたいときに使えます。", MessageType.Info);
                    }
                }
            }
        }

        private void CheckDuplicateScriptCopies()
        {
            string[] guids = AssetDatabase.FindAssets("VRCBakeAssistantWindow t:Script");
            var paths = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct()
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count <= 1)
            {
                string only = paths.Count == 1 ? paths[0] : "見つかりませんでした";
                SetStatus("重複コピーは見つかりませんでした。検出: " + only, MessageType.Info);
                Debug.Log("[VRC Bake Assistant] 重複コピー確認: " + only);
                return;
            }

            string message = "VRCBakeAssistantWindow.cs が複数あります。Toolsメニューが二重に出る原因になります。残すのは Packages/com.yushimatenjin.vrc-bake-assistant 側だけです。";
            Debug.LogWarning("[VRC Bake Assistant] " + message + "\n" + string.Join("\n", paths.ToArray()));
            SetStatus(message + " Consoleに場所を出しました。", MessageType.Warning);
        }

        private void DrawBakeStatus()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("現在の状態", EditorStyles.boldLabel);
                if (Lightmapping.isRunning)
                {
                    EditorGUILayout.LabelField($"ベイク中: {Lightmapping.buildProgress:P1}");
                    if (GUILayout.Button("ベイクをキャンセル"))
                    {
                        QueueOperation("ベイクのキャンセル", () =>
                        {
                            Lightmapping.Cancel();
                            SetStatus("ベイクをキャンセルしました。", MessageType.Warning);
                        });
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("ベイクは停止中です。設定や配置を変更できます。");
                }

                var snapshot = cachedSnapshot ?? LoadBakeSnapshot();
                if (snapshot != null && !string.IsNullOrEmpty(snapshot.capturedAt))
                {
                    EditorGUILayout.LabelField("前回Bakeメモ", snapshot.capturedAt);
                    EditorGUILayout.LabelField("記録ライト", $"{snapshot.lights.Count} 個");
                }
                else
                {
                    EditorGUILayout.LabelField("前回Bakeメモ", "まだありません。Bake完了時、または手動記録で作成されます。");
                }
            }
        }

        private void DrawTargetOptions()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("対象範囲", EditorStyles.boldLabel);
                selectionOnly = EditorGUILayout.ToggleLeft("選択オブジェクトだけを対象にする", selectionOnly);
                includeInactive = EditorGUILayout.ToggleLeft("非アクティブなオブジェクトも数える", includeInactive);
                preferGpu = EditorGUILayout.ToggleLeft("ベイク時にProgressive GPUを優先する", preferGpu);
                EditorGUILayout.HelpBox(
                    selectionOnly
                        ? "今は選択したオブジェクト配下だけを対象にします。既存ワールドの一部だけ試したいとき向けです。"
                        : "今はシーン全体を対象にします。練習シーンや小さめのワールドではこちらが分かりやすいです。",
                    MessageType.None);
            }
        }

        private void DrawStep0PracticeScene()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("0. 練習シーン・見た目リセット", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "床・壁・箱・金属球・基本ライト・Light Probe Group・Reflection Probe・原点の軸を、分かりやすい親オブジェクトに分けて作ります。現在のシーンは保存確認後に空の新規シーンへ切り替わります。",
                    MessageType.Info);

                if (GUILayout.Button("0から練習シーンを作る"))
                {
                    QueueOperation("練習シーン作成", () =>
                    {
                        CreatePracticeScene();
                        rendererItems.Clear();
                        lightItems.Clear();
                        lastReport = ScanScene();
                        SetStatus("練習シーンを作成しました。Hierarchyは『Guide / BakeTargets / NotBaked / Lights / Probes / Camera』に分けています。次は『現在のシーンを診断』で確認してください。", MessageType.Info);
                    });
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("SkyboxをOFFにする"))
                    {
                        QueueOperation("Skybox OFF", () =>
                        {
                            DisableSkyboxAndUseFlatAmbient();
                            SetStatus("SkyboxをOFFにしました。背景光を弱くしたので、ライトの効果を確認しやすくなります。", MessageType.Info);
                        });
                    }

                    if (GUILayout.Button("原点の軸を追加/更新"))
                    {
                        QueueOperation("原点の軸を追加", () =>
                        {
                            CreateOrUpdateAxisGuide();
                            rendererItems.Clear();
                            SetStatus("原点の軸を追加しました。赤=X、緑=Y、青=Zの目印です。必要ならRenderer一覧を更新してください。", MessageType.Info);
                        });
                    }
                }
            }
        }

        private void DrawStep1Scan()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("1. 現在のシーンを診断", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("足りないもの、Bakeに使うライト、前回Bakeメモとの差分を確認します。", MessageType.None);

                if (GUILayout.Button("現在のシーンを診断"))
                {
                    QueueOperation("シーン診断", () =>
                    {
                        lastReport = ScanScene();
                        SetStatus(lastReport.warnings.Count == 0
                            ? "診断完了。ベイクに必要な基本要素はそろっています。"
                            : $"診断完了。確認したい項目が {lastReport.warnings.Count} 件あります。下の黄色い表示を見てください。",
                            lastReport.warnings.Count == 0 ? MessageType.Info : MessageType.Warning);
                    });
                }

                if (lastReport != null)
                {
                    EditorGUILayout.Space(4);
                    DrawReportLine("Renderer", $"{lastReport.renderers} 個 / ライトマップ対象 {lastReport.giRenderers} 個 / 未参加 {lastReport.notGiRenderers} 個", lastReport.giRenderers > 0);
                    DrawReportLine("Light", $"{lastReport.lights} 個  Bake使用:{lastReport.bakeRelevantLights}  Baked:{lastReport.bakedLights} Mixed:{lastReport.mixedLights} Realtime:{lastReport.realtimeLights}", lastReport.lights > 0);
                    DrawReportLine("Light種類", $"Directional:{lastReport.directionalLights} Area:{lastReport.areaLights} Point:{lastReport.pointLights} Spot:{lastReport.spotLights}", lastReport.lights > 0);
                    DrawReportLine("Light Probe Group", $"{lastReport.lightProbeGroups} 個 / Probe数 {lastReport.lightProbeCount}", lastReport.lightProbeGroups > 0);
                    DrawReportLine("Reflection Probe", $"{lastReport.reflectionProbes} 個 / Baked {lastReport.bakedReflectionProbes} 個", lastReport.reflectionProbes > 0);
                    DrawReportLine("Lighting Settings", lastReport.hasLightingSettings ? "設定済み" : "未設定", lastReport.hasLightingSettings);
                    DrawReportLine("Lighting Data", lastReport.hasLightingDataAsset ? "ベイクデータあり" : "まだ無い / クリア済み", lastReport.hasLightingDataAsset);

                    if (lastReport.hasBakeSnapshot)
                    {
                        int needsBake = lastReport.newBakeLightsSinceSnapshot + lastReport.changedBakeLightsSinceSnapshot + lastReport.removedBakeLightsSinceSnapshot;
                        DrawReportLine("前回Bakeメモとの差分", $"新規:{lastReport.newBakeLightsSinceSnapshot} 変更:{lastReport.changedBakeLightsSinceSnapshot} 削除:{lastReport.removedBakeLightsSinceSnapshot}  → 再Bake目安:{needsBake}", needsBake == 0);
                        if (lastReport.changedLightNames.Count > 0)
                        {
                            EditorGUILayout.LabelField("差分ライト", string.Join(", ", lastReport.changedLightNames.Take(6).ToArray()), EditorStyles.wordWrappedMiniLabel);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("前回Bakeメモがありません。Bake完了時に自動保存されます。外部のLightingウィンドウでBakeした場合は『今のライト状態を前回Bakeメモとして記録』を押してください。", MessageType.None);
                    }

                    foreach (var ok in lastReport.okMessages)
                    {
                        EditorGUILayout.HelpBox(ok, MessageType.None);
                    }

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
                EditorGUILayout.LabelField("2. ライトマップに焼く対象を決める", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Bake対象 = 『この床・壁・家具をLightmap計算に参加させる』という意味です。動かないものはON、アバター・持てる小物・動く扉などはOFFが基本です。",
                    MessageType.Info);
                EditorGUILayout.HelpBox(
                    "UV2 = ライトマップ用の展開図です。FBXモデルにUV2が無いと、影や明るさがきれいに焼けないことがあります。UnityのGenerate Lightmap UVsで作れる場合があります。",
                    MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(selectionOnly ? "選択中をBake対象ON" : "シーン内RendererをBake対象ON"))
                    {
                        QueueOperation("Bake対象ON", () =>
                        {
                            int changed = SetContributeGIForTargets(true);
                            RefreshRendererList();
                            lastReport = ScanScene();
                            SetStatus(changed == 0
                                ? "変更はありませんでした。すでにBake対象になっている可能性があります。"
                                : $"{changed} 個のRendererをBake対象にしました。床・壁・動かない家具向けの設定です。",
                                MessageType.Info);
                        });
                    }

                    if (GUILayout.Button("選択中をBake対象OFF"))
                    {
                        QueueOperation("Bake対象OFF", () =>
                        {
                            int changed = SetContributeGIForSelected(false);
                            RefreshRendererList();
                            lastReport = ScanScene();
                            SetStatus(changed == 0
                                ? "選択中に変更できるRendererがありませんでした。"
                                : $"選択中のRenderer {changed} 個をBake対象から外しました。動く物・確認用オブジェクト向けです。",
                                changed == 0 ? MessageType.Warning : MessageType.Info);
                        });
                    }
                }

                if (GUILayout.Button("おすすめ自動設定：静止物っぽいMeshRendererをON、動く物っぽいものをOFF"))
                {
                    QueueOperation("Bake対象おすすめ自動設定", () =>
                    {
                        int changed = ApplyRecommendedBakeTargetRules();
                        RefreshRendererList();
                        lastReport = ScanScene();
                        SetStatus($"おすすめ自動設定を実行しました。変更 {changed} 個。最後は下の一覧で見て、必要な物だけ手で切り替えてください。", MessageType.Info);
                    });
                }

                if (GUILayout.Button("UV2不足のモデルでGenerate Lightmap UVsをONにする"))
                {
                    QueueOperation("Generate Lightmap UVs", () =>
                    {
                        int changed = EnableGenerateLightmapUvsForModelImporters();
                        AssetDatabase.Refresh();
                        RefreshRendererList();
                        lastReport = ScanScene();
                        SetStatus(changed == 0
                            ? "変更対象のFBXは見つかりませんでした。Primitiveや既にUV2があるモデルでは何もしません。"
                            : $"{changed} 個のモデルImporterでGenerate Lightmap UVsをONにしました。再インポート後にもう一度診断してください。",
                            MessageType.Info);
                    });
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("大きいワールドではRenderer一覧の取得が重いことがあります。0.1.6では自動取得せず、『一覧を更新』を押したときだけ集めます。OOM対策として、まずは最大取得数を小さめにしてください。", MessageType.None);
                rendererRefreshScanLimit = EditorGUILayout.IntSlider(new GUIContent("一覧取得の上限", "大きいシーンでメモリを使いすぎないため、Renderer一覧に集める最大数です。"), rendererRefreshScanLimit, 100, 5000);
                rendererListUvCheck = EditorGUILayout.ToggleLeft(new GUIContent("一覧更新時にUV2不足も確認する（重い場合OFF推奨）", "MeshのUVチャンネル確認も行います。重いワールドではOFFのままがおすすめです。"), rendererListUvCheck);

                DrawRendererBakeTargetList();
            }
        }

        private void DrawRendererBakeTargetList()
        {
            EditorGUILayout.Space(6);
            showBakeTargetList = EditorGUILayout.Foldout(showBakeTargetList, "Renderer一覧：Bake対象をポチポチ切り替える", true);
            if (!showBakeTargetList) return;

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.HelpBox("未参加 = このままだとLightmapに焼かれません。動かない床・壁・家具なら『焼く』をON、動く物や確認用モデルならOFFのままでOKです。", MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("一覧を更新", GUILayout.Width(96)))
                    {
                        RefreshRendererList();
                        SetStatus($"Renderer一覧を更新しました。{rendererItems.Count} 個見つかりました。", MessageType.Info);
                    }

                    showOnlyNotContributeGI = EditorGUILayout.ToggleLeft("未参加だけ", showOnlyNotContributeGI, GUILayout.Width(92));
                    EditorGUILayout.LabelField("検索", GUILayout.Width(32));
                    rendererSearch = EditorGUILayout.TextField(rendererSearch);
                }

                rendererListLimit = EditorGUILayout.IntSlider("最大表示数", rendererListLimit, 20, 300);

                if (rendererItems.Count == 0)
                {
                    EditorGUILayout.HelpBox("Renderer一覧はまだ取得していません。必要なときだけ『一覧を更新』を押してください。", MessageType.None);
                    return;
                }

                if (rendererListHitLimit)
                {
                    EditorGUILayout.HelpBox("一覧取得の上限に達しました。検索や選択範囲モードを使うか、必要な場合だけ上限を上げてください。", MessageType.Warning);
                }

                var filtered = GetFilteredRendererItems().ToList();
                EditorGUILayout.LabelField("表示", $"{Mathf.Min(filtered.Count, rendererListLimit)} / {filtered.Count} 件（全 {rendererItems.Count} 件）");

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("焼く", GUILayout.Width(44));
                    GUILayout.Label("反射", GUILayout.Width(44));
                    GUILayout.Label("選択", GUILayout.Width(44));
                    GUILayout.Label("状態", GUILayout.Width(86));
                    GUILayout.Label("Renderer名");
                }

                foreach (var item in filtered.Take(rendererListLimit))
                {
                    if (item.renderer == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool nextGI = GUILayout.Toggle(item.contributeGI, GUIContent.none, GUILayout.Width(44));
                        if (nextGI != item.contributeGI)
                        {
                            SetRendererBakeFlags(item.renderer, nextGI, item.reflectionStatic);
                            item.contributeGI = nextGI;
                            SetStatus($"{item.renderer.name} のBake対象を {(nextGI ? "ON" : "OFF")} にしました。", MessageType.Info);
                        }

                        bool nextReflection = GUILayout.Toggle(item.reflectionStatic, GUIContent.none, GUILayout.Width(44));
                        if (nextReflection != item.reflectionStatic)
                        {
                            SetRendererBakeFlags(item.renderer, item.contributeGI, nextReflection);
                            item.reflectionStatic = nextReflection;
                            SetStatus($"{item.renderer.name} のReflection Probe Staticを {(nextReflection ? "ON" : "OFF")} にしました。", MessageType.Info);
                        }

                        if (GUILayout.Button("選択", GUILayout.Width(44)))
                        {
                            Selection.activeGameObject = item.renderer.gameObject;
                            EditorGUIUtility.PingObject(item.renderer.gameObject);
                        }

                        string state = item.contributeGI ? "Bake対象" : "未参加";
                        if (item.missingUv2) state += " / UV2?";
                        if (item.likelyDynamic && item.contributeGI) state += " / 動的?";
                        GUILayout.Label(state, GUILayout.Width(86));
                        GUILayout.Label($"{item.path}  [{item.typeName}]", EditorStyles.wordWrappedMiniLabel);
                    }
                }

                if (filtered.Count > rendererListLimit)
                {
                    EditorGUILayout.HelpBox("表示数を超えています。検索するか最大表示数を上げてください。", MessageType.None);
                }
            }
        }

        private void DrawStep3LightingPreset()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("3. ベイク品質プリセット", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "ここで変わるのは、Lightmap解像度・サンプル数・Directional/Non-Directionalなどの『焼き込み品質』です。ライトやProbeの位置は変わりません。",
                    MessageType.Info);

                int currentIndex = Mathf.Max(0, Array.IndexOf(PresetValues, preset));
                int nextIndex = EditorGUILayout.Popup(new GUIContent("使う品質", "まずは試し焼き。問題なければPC標準や仕上げ確認へ上げます。"), currentIndex, PresetLabels);
                preset = PresetValues[Mathf.Clamp(nextIndex, 0, PresetValues.Length - 1)];

                EditorGUILayout.HelpBox(GetPresetDescription(preset), MessageType.None);
                EditorGUILayout.LabelField("設定目安", GetPresetTechnicalSummary(preset));

                if (GUILayout.Button($"「{GetPresetName(preset)}」をシーンに適用"))
                {
                    QueueOperation("ベイク品質プリセット適用", () =>
                    {
                        ApplyLightingPreset(preset);
                        lastReport = ScanScene();
                        SetStatus($"「{GetPresetName(preset)}」を適用しました。次はライト配置とProbe配置を確認してください。", MessageType.Info);
                    });
                }
            }
        }

        private void DrawStep4LightPlacement()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("4. ライトを配置・確認する", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Bakeに使われるのは主にBaked / Mixedライトです。Realtimeライトはリアルタイムで効くので便利ですが、LightmapやLight Probeには焼き込まれません。",
                    MessageType.Info);
                EditorGUILayout.HelpBox(
                    "Baked/Mixedライトを消したり動かしたら、見た目を正しくするために再Bakeが必要です。このツールは前回Bakeメモとの差分を見て『新規・変更ライト』を数えます。",
                    MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("基本ライトセットを追加/更新"))
                    {
                        QueueOperation("基本ライトセット", () =>
                        {
                            int count = CreateOrUpdateStarterLights();
                            RefreshLightList();
                            lastReport = ScanScene();
                            SetStatus($"基本ライトセットを追加/更新しました。Directional、Area、Pointの {count} 個です。Hierarchyの『03_Lights_ベイク用ライト』に入っています。", MessageType.Info);
                        });
                    }

                    if (GUILayout.Button("ツール作成ライトだけ削除"))
                    {
                        QueueOperation("ツール作成ライト削除", () =>
                        {
                            int count = DeleteStarterLightsWithConfirm();
                            RefreshLightList();
                            lastReport = ScanScene();
                            SetStatus(count == 0
                                ? "削除したツール作成ライトはありません。"
                                : $"ツール作成ライトを {count} 個削除しました。必要なら『基本ライトセット』で戻せます。",
                                count == 0 ? MessageType.Info : MessageType.Warning);
                        });
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("選択LightをBakedにする"))
                    {
                        QueueOperation("選択LightをBaked", () =>
                        {
                            int count = SetSelectedLightsBakeType(LightmapBakeType.Baked);
                            RefreshLightList();
                            lastReport = ScanScene();
                            SetStatus(count == 0 ? "選択中のLightがありません。" : $"選択Light {count} 個をBakedにしました。次回BakeでLightmap/Probeに焼き込まれます。", count == 0 ? MessageType.Warning : MessageType.Info);
                        });
                    }

                    if (GUILayout.Button("選択LightをRealtimeにする"))
                    {
                        QueueOperation("選択LightをRealtime", () =>
                        {
                            int count = SetSelectedLightsBakeType(LightmapBakeType.Realtime);
                            RefreshLightList();
                            lastReport = ScanScene();
                            SetStatus(count == 0 ? "選択中のLightがありません。" : $"選択Light {count} 個をRealtimeにしました。Lightmap/Light Probeには焼き込まれません。", count == 0 ? MessageType.Warning : MessageType.Info);
                        });
                    }
                }

                DrawLightList();

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("ライトのざっくり用途", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("Directional: 太陽・月のような全体光。屋外や全体の影方向を決めます。", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Area: 窓・看板・天井照明のような面の光。柔らかい明るさを焼き込みます。Built-inでは基本的にBake向きです。", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("Point: 電球・ランタンのような点の光。近い範囲を丸く照らします。", EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawLightList()
        {
            showLightList = EditorGUILayout.Foldout(showLightList, "Light一覧：Bakeに使うライトを確認する", true);
            if (!showLightList) return;

            using (new EditorGUILayout.VerticalScope("box"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("一覧を更新", GUILayout.Width(96)))
                    {
                        RefreshLightList();
                        SetStatus($"Light一覧を更新しました。{lightItems.Count} 個見つかりました。", MessageType.Info);
                    }

                    EditorGUILayout.LabelField("検索", GUILayout.Width(32));
                    lightSearch = EditorGUILayout.TextField(lightSearch);
                }

                lightListLimit = EditorGUILayout.IntSlider("最大表示数", lightListLimit, 20, 300);
                if (lightItems.Count == 0)
                {
                    EditorGUILayout.HelpBox("Light一覧はまだ取得していません。必要なときだけ『一覧を更新』を押してください。", MessageType.None);
                    return;
                }

                if (lightListHitLimit)
                {
                    EditorGUILayout.HelpBox("Light一覧の取得上限に達しました。検索や選択範囲モードを使うか、不要なLightを整理してください。", MessageType.Warning);
                }

                var filtered = GetFilteredLightItems().ToList();
                EditorGUILayout.LabelField("表示", $"{Mathf.Min(filtered.Count, lightListLimit)} / {filtered.Count} 件（全 {lightItems.Count} 件）");

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("有効", GUILayout.Width(42));
                    GUILayout.Label("選択", GUILayout.Width(44));
                    GUILayout.Label("BakeType", GUILayout.Width(100));
                    GUILayout.Label("種類", GUILayout.Width(78));
                    GUILayout.Label("状態", GUILayout.Width(96));
                    GUILayout.Label("Light名");
                }

                foreach (var item in filtered.Take(lightListLimit))
                {
                    if (item.light == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool nextEnabled = GUILayout.Toggle(item.light.enabled, GUIContent.none, GUILayout.Width(42));
                        if (nextEnabled != item.light.enabled)
                        {
                            Undo.RecordObject(item.light, "Toggle Light Enabled");
                            item.light.enabled = nextEnabled;
                            EditorUtility.SetDirty(item.light);
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                            SetStatus($"{item.light.name} を {(nextEnabled ? "有効" : "無効")} にしました。", MessageType.Info);
                        }

                        if (GUILayout.Button("選択", GUILayout.Width(44)))
                        {
                            Selection.activeGameObject = item.light.gameObject;
                            EditorGUIUtility.PingObject(item.light.gameObject);
                        }

                        var nextType = (LightmapBakeType)EditorGUILayout.EnumPopup(item.light.lightmapBakeType, GUILayout.Width(100));
                        if (nextType != item.light.lightmapBakeType)
                        {
                            Undo.RecordObject(item.light, "Change Light Bake Type");
                            item.light.lightmapBakeType = nextType;
                            EditorUtility.SetDirty(item.light);
                            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                            item.newSinceLastBake = true;
                            SetStatus($"{item.light.name} のBakeTypeを {nextType} にしました。", MessageType.Info);
                        }

                        GUILayout.Label(item.typeName, GUILayout.Width(78));
                        string state = GetLightBakeStateLabel(item.light);
                        if (item.newSinceLastBake) state = "新規/要Bake";
                        else if (item.changedSinceLastBake) state = "変更/要Bake";
                        GUILayout.Label(state, GUILayout.Width(96));
                        GUILayout.Label(item.path, EditorStyles.wordWrappedMiniLabel);
                    }
                }

                if (filtered.Count > lightListLimit)
                {
                    EditorGUILayout.HelpBox("表示数を超えています。検索するか最大表示数を上げてください。", MessageType.None);
                }
            }
        }

        private void DrawStep5ProbeGeneration()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("5. Probeを配置する", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Light Probeはアバターや動く物をワールドの明るさに馴染ませるための目印です。Reflection Probeは金属・ガラス・水面などの反射を周囲に馴染ませます。",
                    MessageType.Info);

                showProbeAdvanced = EditorGUILayout.Foldout(showProbeAdvanced, "Light Probe配置の大きさ・密度を調整する", true);
                if (showProbeAdvanced)
                {
                    probeSpacing = EditorGUILayout.Slider(new GUIContent("Light Probe間隔", "数値が小さいほどProbeが増えます。まずは3m前後がおすすめです。"), probeSpacing, 1.0f, 10.0f);
                    probePadding = EditorGUILayout.Slider(new GUIContent("Bounds余白", "自動範囲より少し外側までProbeを置くための余白です。"), probePadding, 0.0f, 5.0f);
                    maxProbeCount = EditorGUILayout.IntSlider(new GUIContent("Light Probe最大数", "多すぎると管理しづらいため上限を設けます。"), maxProbeCount, 64, 3000);
                    useManualProbeBounds = EditorGUILayout.ToggleLeft("配置範囲を手動指定する", useManualProbeBounds);
                    using (new EditorGUI.DisabledScope(!useManualProbeBounds))
                    {
                        manualProbeCenter = EditorGUILayout.Vector3Field("手動範囲 Center", manualProbeCenter);
                        manualProbeSize = EditorGUILayout.Vector3Field("手動範囲 Size", manualProbeSize);
                    }

                    int estimate = EstimateProbeCountForCurrentSettings();
                    EditorGUILayout.LabelField("Probe数の目安", estimate > 0 ? $"約 {estimate} 個" : "対象範囲なし");

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("選択/シーン範囲を手動範囲へコピー"))
                        {
                            QueueOperation("Probe範囲コピー", () =>
                            {
                                if (TryGetTargetBounds(out var bounds))
                                {
                                    bounds.Expand(probePadding * 2f);
                                    manualProbeCenter = bounds.center;
                                    manualProbeSize = bounds.size;
                                    useManualProbeBounds = true;
                                    SetStatus("現在の対象範囲をLight Probe手動範囲へコピーしました。必要ならSizeを調整してください。", MessageType.Info);
                                }
                                else
                                {
                                    SetStatus("対象Rendererが無いため、Probe範囲をコピーできませんでした。", MessageType.Warning);
                                }
                            });
                        }

                        if (GUILayout.Button("Probe範囲ガイドを表示/更新"))
                        {
                            QueueOperation("Probe範囲ガイド", () =>
                            {
                                bool ok = CreateOrUpdateProbeBoundsGuide();
                                rendererItems.Clear();
                                SetStatus(ok ? "Light Probe配置範囲のガイドを表示しました。黄色い枠の中にProbeを置くイメージです。必要ならRenderer一覧を更新してください。" : "Probe範囲ガイドを作成できませんでした。", ok ? MessageType.Info : MessageType.Warning);
                            });
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("Reflection Probe解像度", "反射の解像度です。まずは128、重い場合は64。"), GUILayout.Width(170));
                    reflectionProbeResolution = EditorGUILayout.IntPopup(reflectionProbeResolution,
                        new[] { "64", "128", "256", "512" },
                        new[] { 64, 128, 256, 512 });
                }
                reflectionProbeBoxProjection = EditorGUILayout.ToggleLeft("Box Projectionを有効化", reflectionProbeBoxProjection);

                if (GUILayout.Button(selectionOnly ? "選択範囲にLight Probe Gridを作成/更新" : "シーン範囲にLight Probe Gridを作成/更新"))
                {
                    QueueOperation("Light Probe Grid作成", () =>
                    {
                        int count = CreateLightProbeGrid();
                        lastReport = ScanScene();
                        SetStatus(count > 0
                            ? $"Light Probeを {count} 個配置しました。アバターの明るさ確認に使えます。Hierarchyの『04_Probes』に入っています。"
                            : "Light Probeを作成できませんでした。対象Rendererがあるか確認してください。",
                            count > 0 ? MessageType.Info : MessageType.Warning);
                    });
                }

                if (GUILayout.Button(selectionOnly ? "選択範囲にReflection Probeを作成/更新" : "シーン範囲にReflection Probeを作成/更新"))
                {
                    QueueOperation("Reflection Probe作成", () =>
                    {
                        bool ok = CreateReflectionProbeForBounds();
                        lastReport = ScanScene();
                        SetStatus(ok
                            ? "Reflection Probeを作成/更新しました。金属球やガラス系マテリアルで反射を確認してください。"
                            : "Reflection Probeを作成できませんでした。対象Rendererがあるか確認してください。",
                            ok ? MessageType.Info : MessageType.Warning);
                    });
                }

                if (GUILayout.Button("選択RendererのProbe参照を推奨設定へ"))
                {
                    QueueOperation("Renderer Probe参照設定", () =>
                    {
                        int count = ConfigureSelectedRendererProbeUsage();
                        SetStatus(count == 0
                            ? "選択中のRendererがありません。反射確認用の球などを選んでから押してください。"
                            : $"選択Renderer {count} 個をLight Probe / Reflection Probeを使う設定にしました。",
                            count == 0 ? MessageType.Warning : MessageType.Info);
                    });
                }
            }
        }

        private void DrawStep6Bake()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("6. ベイクする", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "まずは『試し焼き（早い）』で見た目を確認し、良さそうなら『PC向け標準』や『仕上げ確認』に上げます。いきなり仕上げ設定にすると待ち時間が長くなりがちです。",
                    MessageType.Info);

                using (new EditorGUI.DisabledScope(Lightmapping.isRunning))
                {
                    if (GUILayout.Button("ライトマップをベイク開始"))
                    {
                        QueueOperation("ライトマップベイク開始", () =>
                        {
                            bool started = StartBakeAsync();
                            SetStatus(started
                                ? "ライトマップのベイクを開始しました。進捗は上の『現在の状態』に表示されます。完了時に前回Bakeメモも保存します。"
                                : "ベイクを開始できませんでした。Consoleの警告とLighting Settingsを確認してください。",
                                started ? MessageType.Info : MessageType.Error);
                        });
                    }

                    if (GUILayout.Button("Baked Reflection Probeだけをベイク"))
                    {
                        QueueOperation("Reflection Probeベイク", () =>
                        {
                            int count = BakeAllBakedReflectionProbes();
                            SetStatus(count > 0
                                ? $"Baked Reflection Probeを {count} 個ベイクしました。"
                                : "Baked Reflection Probeが見つかりませんでした。",
                                count > 0 ? MessageType.Info : MessageType.Warning);
                        });
                    }

                    if (GUILayout.Button("今のライト状態を前回Bakeメモとして記録"))
                    {
                        QueueOperation("Bakeメモ手動記録", () =>
                        {
                            SaveBakeSnapshot();
                            cachedSnapshot = LoadBakeSnapshot();
                            lightItems.Clear();
                            lastReport = ScanScene();
                            SetStatus("現在のBaked/Mixedライト状態を前回Bakeメモとして記録しました。外部のLightingウィンドウでBakeした後の基準合わせに使えます。", MessageType.Info);
                        });
                    }
                }
            }
        }

        private void DrawStep7Cleanup()
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("7. やり直し・掃除", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Lighting Dataの削除は元に戻せません。必要なら先にシーンとプロジェクトをバックアップしてください。", MessageType.Warning);

                if (GUILayout.Button("Lighting Data Assetをクリア"))
                {
                    QueueOperation("Lighting Dataクリア", () =>
                    {
                        if (!EditorUtility.DisplayDialog("Lighting Dataを削除", "現在のLighting Data Assetと関連ライトマップを削除します。続行しますか？", "削除", "キャンセル"))
                        {
                            SetStatus("Lighting Dataの削除をキャンセルしました。", MessageType.Info);
                            return;
                        }

                        Lightmapping.ClearLightingDataAsset();
                        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                        lastReport = ScanScene();
                        SetStatus("Lighting Data Assetをクリアしました。次のベイクで作り直されます。", MessageType.Warning);
                    });
                }

                if (GUILayout.Button("前回Bakeメモを削除"))
                {
                    QueueOperation("Bakeメモ削除", () =>
                    {
                        DeleteBakeSnapshot();
                        cachedSnapshot = null;
                        lightItems.Clear();
                        lastReport = ScanScene();
                        SetStatus("前回Bakeメモを削除しました。次回Bake完了時に作り直されます。", MessageType.Warning);
                    });
                }
            }
        }

        private static void DrawReportLine(string label, string value, bool ok)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(ok ? "OK" : "確認", GUILayout.Width(42));
                EditorGUILayout.LabelField(label, value);
            }
        }

        private void QueueOperation(string label, Action action)
        {
            queuedActionLabel = label;
            queuedAction = action;
        }

        private void RunQueuedActionAfterGuiIfNeeded()
        {
            if (queuedAction == null) return;

            var action = queuedAction;
            var label = queuedActionLabel;
            queuedAction = null;
            queuedActionLabel = null;

            SetStatus($"{label}を実行します。", MessageType.Info);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    SetStatus($"{label}でエラーが発生しました: {ex.Message}", MessageType.Error);
                }
                finally
                {
                    Repaint();
                }
            };

            GUIUtility.ExitGUI();
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private ScanReport ScanScene()
        {
            var report = new ScanReport();

            if (Lightmapping.TryGetLightingSettings(out var settings))
            {
                report.hasLightingSettings = settings != null;
                report.autoGenerate = settings != null && settings.autoGenerate;
            }

            report.hasLightingDataAsset = Lightmapping.lightingDataAsset != null;

            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;

                report.renderers++;
                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                bool contributeGI = (flags & StaticEditorFlags.ContributeGI) != 0;
                bool reflectionStatic = (flags & StaticEditorFlags.ReflectionProbeStatic) != 0;

                if (contributeGI) report.giRenderers++;
                else report.notGiRenderers++;
                if (reflectionStatic) report.reflectionStaticRenderers++;

                if (contributeGI && RendererHasMissingUv2(renderer))
                {
                    report.renderersWithoutUv2++;
                }
            }

            var currentBakeLights = new List<LightSnapshot>();
            foreach (var light in FindSceneComponents<Light>(includeInactive))
            {
                report.lights++;
                if (!light.enabled) report.disabledLights++;
                else report.enabledLights++;

                switch (light.type)
                {
                    case LightType.Directional:
                        report.directionalLights++;
                        break;
                    case LightType.Point:
                        report.pointLights++;
                        break;
                    case LightType.Spot:
                        report.spotLights++;
                        break;
                    case LightType.Rectangle:
                    case LightType.Disc:
                        report.areaLights++;
                        break;
                }

                switch (light.lightmapBakeType)
                {
                    case LightmapBakeType.Baked:
                        report.bakedLights++;
                        report.bakeRelevantLights++;
                        currentBakeLights.Add(CreateLightSnapshot(light));
                        break;
                    case LightmapBakeType.Mixed:
                        report.mixedLights++;
                        report.bakeRelevantLights++;
                        currentBakeLights.Add(CreateLightSnapshot(light));
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

            foreach (var probe in FindSceneComponents<ReflectionProbe>(includeInactive))
            {
                report.reflectionProbes++;
                if (probe.mode == ReflectionProbeMode.Baked) report.bakedReflectionProbes++;
            }

            CompareLightSnapshot(report, currentBakeLights);

            if (!report.hasLightingSettings)
                report.warnings.Add("Lighting Settings Assetが割り当てられていません。3番でベイク品質プリセットを適用してください。");
            else
                report.okMessages.Add("Lighting Settingsは設定済みです。");

            if (report.autoGenerate)
                report.warnings.Add("Auto GenerateがONです。VRChatワールド制作では、意図しない再ベイクを避けるため手動ベイクがおすすめです。3番でOFFにします。");

            if (report.renderers > 0 && report.giRenderers == 0)
                report.warnings.Add("ライトマップ対象のRendererがありません。床・壁・動かない家具を2番でBake対象ONにしてください。");
            else if (report.giRenderers > 0)
                report.okMessages.Add("ライトマップ対象のRendererがあります。床・壁・動かない家具が含まれていればOKです。");

            if (report.renderersWithoutUv2 > 0)
                report.warnings.Add($"UV2が無い可能性のあるライトマップ対象Rendererが {report.renderersWithoutUv2} 個あります。必要に応じてGenerate Lightmap UVsをONにしてください。");

            if (report.lights == 0)
                report.warnings.Add("Lightがありません。4番で基本ライトセットを追加すると確認しやすくなります。");
            else
                report.okMessages.Add($"Lightがあります。Bakeに使うライトはBaked/Mixedの {report.bakeRelevantLights} 個です。");

            if (report.realtimeLights > 0)
                report.warnings.Add($"Realtimeライトが {report.realtimeLights} 個あります。Realtimeは実行時に効きますが、Lightmap/Light Probeには焼き込まれません。Bakeで見た目を固定したいライトはBakedかMixedにしてください。");

            if (report.lightProbeGroups == 0)
                report.warnings.Add("Light Probe Groupがありません。アバターや動くオブジェクトの明るさがワールドに馴染みにくくなります。5番で作成してください。");
            else
                report.okMessages.Add("Light Probe Groupがあります。アバターの明るさ確認に使えます。");

            if (report.reflectionProbes == 0)
                report.warnings.Add("Reflection Probeがありません。金属・ガラス・水面などの反射が環境に馴染みにくくなります。5番で作成してください。");
            else
                report.okMessages.Add("Reflection Probeがあります。金属やガラスの反射確認に使えます。");

            int bakeLightDiff = report.newBakeLightsSinceSnapshot + report.changedBakeLightsSinceSnapshot + report.removedBakeLightsSinceSnapshot;
            if (report.hasBakeSnapshot && bakeLightDiff > 0)
            {
                report.warnings.Add($"前回Bakeメモ以降、Bakeに関係するライト差分が {bakeLightDiff} 件あります。見た目確認のため再Bakeがおすすめです。");
            }

            Debug.Log($"[VRC Bake Assistant] 診断完了。Renderer:{report.renderers}, GI:{report.giRenderers}, Lights:{report.lights}, BakeLights:{report.bakeRelevantLights}, LightProbes:{report.lightProbeCount}, ReflectionProbes:{report.reflectionProbes}");
            return report;
        }

        private void CompareLightSnapshot(ScanReport report, List<LightSnapshot> currentBakeLights)
        {
            var snapshot = cachedSnapshot ?? LoadBakeSnapshot();
            if (snapshot == null || snapshot.lights == null || snapshot.lights.Count == 0)
            {
                report.hasBakeSnapshot = false;
                return;
            }

            report.hasBakeSnapshot = true;
            report.snapshotCapturedAt = snapshot.capturedAt;

            var oldById = snapshot.lights
                .Where(l => !string.IsNullOrEmpty(l.globalId))
                .GroupBy(l => l.globalId)
                .ToDictionary(g => g.Key, g => g.First());

            var seen = new HashSet<string>();
            foreach (var current in currentBakeLights)
            {
                if (string.IsNullOrEmpty(current.globalId)) continue;
                seen.Add(current.globalId);
                if (!oldById.TryGetValue(current.globalId, out var old))
                {
                    report.newBakeLightsSinceSnapshot++;
                    report.changedLightNames.Add("新規:" + current.path);
                }
                else if (!string.Equals(old.signature, current.signature, StringComparison.Ordinal))
                {
                    report.changedBakeLightsSinceSnapshot++;
                    report.changedLightNames.Add("変更:" + current.path);
                }
            }

            foreach (var old in snapshot.lights)
            {
                if (!string.IsNullOrEmpty(old.globalId) && !seen.Contains(old.globalId))
                {
                    report.removedBakeLightsSinceSnapshot++;
                    report.changedLightNames.Add("削除:" + old.path);
                }
            }
        }

        private void RefreshRendererList()
        {
            rendererItems.Clear();
            rendererListHitLimit = false;
            int scanned = 0;

            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;
                if (scanned >= rendererRefreshScanLimit)
                {
                    rendererListHitLimit = true;
                    break;
                }

                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                rendererItems.Add(new RendererListItem
                {
                    renderer = renderer,
                    path = GetHierarchyPath(renderer.transform),
                    contributeGI = (flags & StaticEditorFlags.ContributeGI) != 0,
                    reflectionStatic = (flags & StaticEditorFlags.ReflectionProbeStatic) != 0,
                    missingUv2 = rendererListUvCheck && RendererHasMissingUv2(renderer),
                    likelyDynamic = IsLikelyDynamicRenderer(renderer),
                    typeName = renderer.GetType().Name
                });
                scanned++;
            }
        }

        private IEnumerable<RendererListItem> GetFilteredRendererItems()
        {
            IEnumerable<RendererListItem> items = rendererItems.Where(i => i.renderer != null);
            if (showOnlyNotContributeGI) items = items.Where(i => !i.contributeGI);
            if (!string.IsNullOrWhiteSpace(rendererSearch))
            {
                string q = rendererSearch.Trim();
                items = items.Where(i => i.path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || i.typeName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return items.OrderBy(i => i.contributeGI).ThenBy(i => i.path, StringComparer.OrdinalIgnoreCase);
        }

        private void RefreshLightList()
        {
            cachedSnapshot = LoadBakeSnapshot();
            var oldById = cachedSnapshot == null || cachedSnapshot.lights == null
                ? new Dictionary<string, LightSnapshot>()
                : cachedSnapshot.lights.Where(l => !string.IsNullOrEmpty(l.globalId)).GroupBy(l => l.globalId).ToDictionary(g => g.Key, g => g.First());

            lightItems.Clear();
            lightListHitLimit = false;
            int scanned = 0;

            foreach (var light in FindSceneComponents<Light>(includeInactive))
            {
                if (scanned >= LightRefreshScanLimit)
                {
                    lightListHitLimit = true;
                    break;
                }

                var current = CreateLightSnapshot(light);
                bool isNew = light.lightmapBakeType != LightmapBakeType.Realtime && cachedSnapshot != null && !oldById.ContainsKey(current.globalId);
                bool changed = false;
                if (!isNew && cachedSnapshot != null && oldById.TryGetValue(current.globalId, out var old))
                {
                    changed = light.lightmapBakeType != LightmapBakeType.Realtime && !string.Equals(old.signature, current.signature, StringComparison.Ordinal);
                }

                lightItems.Add(new LightListItem
                {
                    light = light,
                    path = GetHierarchyPath(light.transform),
                    typeName = GetLightTypeName(light),
                    newSinceLastBake = isNew,
                    changedSinceLastBake = changed
                });
                scanned++;
            }
        }

        private IEnumerable<LightListItem> GetFilteredLightItems()
        {
            IEnumerable<LightListItem> items = lightItems.Where(i => i.light != null);
            if (!string.IsNullOrWhiteSpace(lightSearch))
            {
                string q = lightSearch.Trim();
                items = items.Where(i => i.path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || i.typeName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return items.OrderByDescending(i => i.light.lightmapBakeType != LightmapBakeType.Realtime).ThenBy(i => i.path, StringComparer.OrdinalIgnoreCase);
        }

        private int SetContributeGIForTargets(bool enabled)
        {
            int changed = 0;
            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;
                if (SetRendererBakeFlags(renderer, enabled, enabled)) changed++;
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[VRC Bake Assistant] Bake対象 {(enabled ? "ON" : "OFF")}: {changed} objects");
            return changed;
        }

        private int SetContributeGIForSelected(bool enabled)
        {
            int changed = 0;
            foreach (var renderer in Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<Renderer>(includeInactive)).Distinct())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;
                if (SetRendererBakeFlags(renderer, enabled, enabled)) changed++;
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return changed;
        }

        private bool SetRendererBakeFlags(Renderer renderer, bool contributeGI, bool reflectionStatic)
        {
            if (renderer == null) return false;
            var go = renderer.gameObject;
            Undo.RecordObject(go, "Change Bake Target Flags");
            var flags = GameObjectUtility.GetStaticEditorFlags(go);
            var newFlags = flags;

            if (contributeGI) newFlags |= StaticEditorFlags.ContributeGI;
            else newFlags &= ~StaticEditorFlags.ContributeGI;

            if (reflectionStatic) newFlags |= StaticEditorFlags.ReflectionProbeStatic;
            else newFlags &= ~StaticEditorFlags.ReflectionProbeStatic;

            if (newFlags == flags) return false;

            GameObjectUtility.SetStaticEditorFlags(go, newFlags);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return true;
        }

        private int ApplyRecommendedBakeTargetRules()
        {
            if (!EditorUtility.DisplayDialog(
                    "Bake対象のおすすめ自動設定",
                    "MeshRendererで、名前や種類から静止物っぽいものをBake対象ONにします。SkinnedMeshRenderer、動的っぽい名前、Guide、Axis、CameraなどはOFFにします。最後は一覧で必ず確認してください。",
                    "実行",
                    "キャンセル"))
            {
                return 0;
            }

            int changed = 0;
            foreach (var renderer in GetTargetRenderers())
            {
                if (!IsBakeTargetRenderer(renderer)) continue;
                bool shouldBake = renderer is MeshRenderer && !IsLikelyDynamicRenderer(renderer) && !IsAssistantGuideRenderer(renderer) && renderer.bounds.size.sqrMagnitude > 0.01f;
                if (SetRendererBakeFlags(renderer, shouldBake, shouldBake)) changed++;
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

            Debug.Log($"[VRC Bake Assistant] Generate Lightmap UVs enabled: {changed} assets");
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
                case BakePreset.QuickCheck:
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

                case BakePreset.QuestLight:
                    settings.lightmapResolution = 10;
                    settings.lightmapPadding = 2;
                    settings.lightmapMaxSize = 1024;
                    settings.directSampleCount = 32;
                    settings.indirectSampleCount = 64;
                    settings.environmentSampleCount = 64;
                    settings.minBounces = 1;
                    settings.maxBounces = 2;
                    settings.ao = true;
                    settings.aoMaxDistance = 1.0f;
                    settings.aoExponentDirect = 0.8f;
                    settings.aoExponentIndirect = 0.8f;
                    settings.directionalityMode = LightmapsMode.NonDirectional;
                    settings.lightmapCompression = LightmapCompression.NormalQuality;
                    settings.mixedBakeMode = MixedLightingMode.Subtractive;
                    break;

                case BakePreset.PCStandard:
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

                case BakePreset.FinalCheck:
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
            Debug.Log($"[VRC Bake Assistant] ベイク品質プリセット適用: {GetPresetName(selectedPreset)} -> {assetPath}");
        }

        private int EstimateProbeCountForCurrentSettings()
        {
            if (!TryGetProbeBounds(out var bounds)) return 0;
            var positions = GenerateProbePositions(bounds, probeSpacing, maxProbeCount);
            return positions.Count;
        }

        private int CreateLightProbeGrid()
        {
            if (!TryGetProbeBounds(out var bounds))
            {
                EditorUtility.DisplayDialog("Light Probe作成失敗", "対象RendererのBoundsが見つかりません。", "OK");
                return 0;
            }

            return CreateOrUpdateLightProbeGroup(bounds, LightProbeGroupName, probeSpacing, maxProbeCount);
        }

        private bool TryGetProbeBounds(out Bounds bounds)
        {
            bounds = default;
            if (useManualProbeBounds)
            {
                var size = new Vector3(Mathf.Abs(manualProbeSize.x), Mathf.Abs(manualProbeSize.y), Mathf.Abs(manualProbeSize.z));
                if (size.x <= 0.01f || size.y <= 0.01f || size.z <= 0.01f) return false;
                bounds = new Bounds(manualProbeCenter, size);
                return true;
            }

            if (!TryGetTargetBounds(out bounds)) return false;
            bounds.Expand(probePadding * 2f);
            return true;
        }

        private int CreateOrUpdateLightProbeGroup(Bounds bounds, string objectName, float spacing, int maxCount)
        {
            var positions = GenerateProbePositions(bounds, spacing, maxCount);
            if (positions.Count == 0)
            {
                EditorUtility.DisplayDialog("Light Probe作成失敗", "Probe位置を生成できませんでした。", "OK");
                return 0;
            }

            var parent = GetOrCreateAssistantChild(ProbesRootName);
            var existing = FindChildByName(parent.transform, objectName);
            var go = existing != null ? existing : new GameObject(objectName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Create Light Probe Group");
                go.transform.SetParent(parent.transform, false);
            }
            else
            {
                Undo.RecordObject(go, "Update Light Probe Group");
            }

            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var group = go.GetComponent<LightProbeGroup>();
            if (group == null) group = go.AddComponent<LightProbeGroup>();
            Undo.RecordObject(group, "Set Light Probe Positions");
            group.probePositions = positions.ToArray();
            EditorUtility.SetDirty(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log($"[VRC Bake Assistant] Light Probe配置: {positions.Count}");
            return positions.Count;
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

        private bool CreateReflectionProbeForBounds()
        {
            Bounds bounds;
            if (useManualProbeBounds)
            {
                if (!TryGetProbeBounds(out bounds))
                {
                    EditorUtility.DisplayDialog("Reflection Probe作成失敗", "手動範囲のSizeが小さすぎます。", "OK");
                    return false;
                }
            }
            else
            {
                if (!TryGetTargetBounds(out bounds))
                {
                    EditorUtility.DisplayDialog("Reflection Probe作成失敗", "対象RendererのBoundsが見つかりません。", "OK");
                    return false;
                }
                bounds.Expand(probePadding * 2f);
            }

            CreateOrUpdateReflectionProbe(bounds, selectionOnly ? ReflectionProbeName + "_Selected" : ReflectionProbeName, reflectionProbeResolution, reflectionProbeBoxProjection);
            return true;
        }

        private ReflectionProbe CreateOrUpdateReflectionProbe(Bounds bounds, string objectName, int resolution, bool boxProjection)
        {
            var parent = GetOrCreateAssistantChild(ProbesRootName);
            var existing = FindChildByName(parent.transform, objectName);
            var go = existing != null ? existing : new GameObject(objectName);
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(go, "Create Reflection Probe");
                go.transform.SetParent(parent.transform, false);
            }
            else
            {
                Undo.RecordObject(go, "Update Reflection Probe");
            }

            go.transform.position = bounds.center;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var probe = go.GetComponent<ReflectionProbe>();
            if (probe == null) probe = go.AddComponent<ReflectionProbe>();
            Undo.RecordObject(probe, "Configure Reflection Probe");
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = bounds.size;
            probe.center = Vector3.zero;
            probe.resolution = resolution;
            probe.hdr = true;
            probe.boxProjection = boxProjection;
            probe.blendDistance = Mathf.Max(0.1f, Mathf.Min(bounds.extents.magnitude * 0.1f, 3.0f));
            probe.importance = 1;
            probe.intensity = 1.0f;
            probe.shadowDistance = 50.0f;
            probe.cullingMask = ~0;

            EditorUtility.SetDirty(probe);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = go;
            Debug.Log($"[VRC Bake Assistant] Reflection Probe作成/更新: {go.name}");
            return probe;
        }

        private bool CreateOrUpdateProbeBoundsGuide()
        {
            if (!TryGetProbeBounds(out var bounds)) return false;

            var parent = GetOrCreateAssistantChild(ProbesRootName);
            var existing = FindChildByName(parent.transform, ProbeBoundsGuideName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var guideRoot = new GameObject(ProbeBoundsGuideName);
            Undo.RegisterCreatedObjectUndo(guideRoot, "Create Probe Bounds Guide");
            guideRoot.transform.SetParent(parent.transform, false);

            var mat = CreateOrLoadMaterial("VRCBake_Guide_Yellow", new Color(1.0f, 0.85f, 0.1f), 0f, 0.2f);
            CreateBoundsEdgeCubes(guideRoot.transform, bounds, mat);
            Selection.activeGameObject = guideRoot;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return true;
        }

        private static void CreateBoundsEdgeCubes(Transform parent, Bounds bounds, Material material)
        {
            float t = 0.035f;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            float xLen = bounds.size.x;
            float yLen = bounds.size.y;
            float zLen = bounds.size.z;

            for (int yi = 0; yi < 2; yi++)
            {
                float y = yi == 0 ? min.y : max.y;
                CreateCube("Guide_Edge_X_Front_" + yi, new Vector3(bounds.center.x, y, min.z), new Vector3(xLen, t, t), material, false).transform.SetParent(parent, true);
                CreateCube("Guide_Edge_X_Back_" + yi, new Vector3(bounds.center.x, y, max.z), new Vector3(xLen, t, t), material, false).transform.SetParent(parent, true);
                CreateCube("Guide_Edge_Z_Left_" + yi, new Vector3(min.x, y, bounds.center.z), new Vector3(t, t, zLen), material, false).transform.SetParent(parent, true);
                CreateCube("Guide_Edge_Z_Right_" + yi, new Vector3(max.x, y, bounds.center.z), new Vector3(t, t, zLen), material, false).transform.SetParent(parent, true);
            }

            for (int xi = 0; xi < 2; xi++)
            {
                for (int zi = 0; zi < 2; zi++)
                {
                    float x = xi == 0 ? min.x : max.x;
                    float z = zi == 0 ? min.z : max.z;
                    CreateCube("Guide_Edge_Y_" + xi + "_" + zi, new Vector3(x, bounds.center.y, z), new Vector3(t, yLen, t), material, false).transform.SetParent(parent, true);
                }
            }
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
            Debug.Log($"[VRC Bake Assistant] Renderer Probe参照設定: {changed}");
            return changed;
        }

        private bool StartBakeAsync()
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
            return started;
        }

        private int BakeAllBakedReflectionProbes()
        {
            EnsureFolder(ReflectionFolder);
            var probes = FindSceneComponents<ReflectionProbe>(includeInactive)
                .Where(p => p.mode == ReflectionProbeMode.Baked)
                .ToArray();

            if (probes.Length == 0)
            {
                EditorUtility.DisplayDialog("Reflection Probeなし", "Baked Reflection Probeが見つかりません。", "OK");
                return 0;
            }

            int bakedCount = 0;
            try
            {
                for (int i = 0; i < probes.Length; i++)
                {
                    var probe = probes[i];
                    EditorUtility.DisplayProgressBar("Bake Reflection Probes", probe.name, (float)i / probes.Length);
                    string safeName = MakeSafeFileName(probe.name);
                    string path = AssetDatabase.GenerateUniqueAssetPath($"{ReflectionFolder}/{safeName}.exr");
                    bool ok = Lightmapping.BakeReflectionProbe(probe, path);
                    if (ok) bakedCount++;
                    else Debug.LogError($"[VRC Bake Assistant] Reflection Probeのベイクに失敗: {probe.name}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            return bakedCount;
        }

        private void CreatePracticeScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureFolder(MaterialFolder);

            var root = new GameObject(AssistantRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create VRC Bake Assistant Root");

            var sampleRoot = CreateChild(root.transform, PracticeRootName);
            var guideRoot = CreateChild(sampleRoot.transform, GuideRootName);
            var geometryRoot = CreateChild(sampleRoot.transform, GeometryRootName);
            var dynamicRoot = CreateChild(sampleRoot.transform, DynamicRootName);
            var lightsRoot = CreateChild(sampleRoot.transform, LightsRootName);
            var probesRoot = CreateChild(sampleRoot.transform, ProbesRootName);
            var cameraRoot = CreateChild(sampleRoot.transform, CameraRootName);

            var wallMat = CreateOrLoadMaterial("VRCBake_Practice_WarmWall", new Color(0.78f, 0.72f, 0.62f), 0f, 0.35f);
            var floorMat = CreateOrLoadMaterial("VRCBake_Practice_Floor", new Color(0.45f, 0.42f, 0.38f), 0f, 0.45f);
            var metalMat = CreateOrLoadMaterial("VRCBake_Practice_Metal", new Color(0.8f, 0.75f, 0.68f), 1f, 0.9f);
            var blueMat = CreateOrLoadMaterial("VRCBake_Practice_Blue", new Color(0.2f, 0.35f, 0.75f), 0f, 0.5f);

            var objects = new List<GameObject>
            {
                CreateCube("Floor_BakeTarget_床", new Vector3(0, -0.05f, 0), new Vector3(10, 0.1f, 10), floorMat, true),
                CreateCube("BackWall_BakeTarget_奥の壁", new Vector3(0, 2.5f, 5), new Vector3(10, 5, 0.2f), wallMat, true),
                CreateCube("LeftWall_BakeTarget_左の壁", new Vector3(-5, 2.5f, 0), new Vector3(0.2f, 5, 10), wallMat, true),
                CreateCube("RightWall_BakeTarget_右の壁", new Vector3(5, 2.5f, 0), new Vector3(0.2f, 5, 10), wallMat, true),
                CreateCube("StaticBox_BakeTarget_影確認A", new Vector3(-2.5f, 0.5f, 1.5f), Vector3.one, blueMat, true),
                CreateCube("StaticBox_BakeTarget_影確認B", new Vector3(2.2f, 0.75f, 0.5f), new Vector3(1.5f, 1.5f, 1.5f), wallMat, true)
            };

            foreach (var go in objects)
                go.transform.SetParent(geometryRoot.transform, true);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Reflection_Check_Sphere_NotBaked_金属反射確認";
            sphere.transform.position = new Vector3(0, 1.0f, 0);
            sphere.transform.localScale = Vector3.one * 1.2f;
            sphere.GetComponent<Renderer>().sharedMaterial = metalMat;
            sphere.transform.SetParent(dynamicRoot.transform, true);

            var avatarGuide = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            avatarGuide.name = "Avatar_Height_Guide_NotBaked_アバター高さ確認";
            avatarGuide.transform.position = new Vector3(3.8f, 0.9f, -2.8f);
            avatarGuide.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);
            avatarGuide.GetComponent<Renderer>().sharedMaterial = blueMat;
            avatarGuide.transform.SetParent(dynamicRoot.transform, true);

            CreateOrUpdateAxisGuide(guideRoot.transform);
            CreateOrUpdateStarterLights(lightsRoot.transform);

            var practiceBounds = new Bounds(new Vector3(0, 2.4f, 0), new Vector3(11.5f, 5.2f, 11.5f));
            manualProbeCenter = practiceBounds.center;
            manualProbeSize = practiceBounds.size;
            useManualProbeBounds = false;
            CreateOrUpdateLightProbeGroup(practiceBounds, LightProbeGroupName, 3.0f, 256);
            CreateOrUpdateReflectionProbe(practiceBounds, ReflectionProbeName, 128, true);
            CreateOrUpdateProbeBoundsGuide();

            var cameraGo = new GameObject("PreviewCamera_全体確認");
            cameraGo.transform.position = new Vector3(0, 2.5f, -8);
            cameraGo.transform.rotation = Quaternion.Euler(15, 0, 0);
            cameraGo.AddComponent<Camera>();
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(cameraRoot.transform, true);

            ApplyLightingPreset(BakePreset.QuickCheck);
            DisableSkyboxAndUseFlatAmbient();

            Selection.activeGameObject = sampleRoot;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[VRC Bake Assistant] 練習シーンを作成しました。");
        }

        private int CreateOrUpdateStarterLights()
        {
            return CreateOrUpdateStarterLights(GetOrCreateAssistantChild(LightsRootName).transform);
        }

        private int CreateOrUpdateStarterLights(Transform lightsRootTransform)
        {
            for (int i = lightsRootTransform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(lightsRootTransform.GetChild(i).gameObject);
            }

            int count = 0;

            var sunGo = new GameObject("01_Mixed_Directional_全体の影方向");
            sunGo.transform.SetParent(lightsRootTransform, false);
            sunGo.transform.rotation = Quaternion.Euler(50, -30, 0);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            sun.intensity = 0.75f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            count++;

            var areaGo = new GameObject("02_Baked_Area_窓や天井照明");
            areaGo.transform.SetParent(lightsRootTransform, false);
            areaGo.transform.position = new Vector3(-1.8f, 3.8f, -3.5f);
            areaGo.transform.rotation = Quaternion.Euler(75, 0, 0);
            var area = areaGo.AddComponent<Light>();
            area.type = LightType.Rectangle;
            area.lightmapBakeType = LightmapBakeType.Baked;
            area.intensity = 5.0f;
            area.range = 7.0f;
            area.areaSize = new Vector2(3.0f, 1.6f);
            area.color = new Color(1.0f, 0.88f, 0.72f);
            area.shadows = LightShadows.Soft;
            count++;

            var pointGo = new GameObject("03_Baked_Point_電球やランタン");
            pointGo.transform.SetParent(lightsRootTransform, false);
            pointGo.transform.position = new Vector3(-3.0f, 2.2f, -1.5f);
            var point = pointGo.AddComponent<Light>();
            point.type = LightType.Point;
            point.lightmapBakeType = LightmapBakeType.Baked;
            point.intensity = 2.0f;
            point.range = 5.0f;
            point.color = new Color(1.0f, 0.74f, 0.50f);
            point.shadows = LightShadows.Soft;
            count++;

            Selection.activeGameObject = lightsRootTransform.gameObject;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[VRC Bake Assistant] 基本ライトセットを追加/更新しました。");
            return count;
        }

        private int DeleteStarterLightsWithConfirm()
        {
            var root = GetOrCreateAssistantChild(LightsRootName);
            var lights = root.GetComponentsInChildren<Light>(true);
            if (lights.Length == 0) return 0;

            if (!EditorUtility.DisplayDialog(
                    "ツール作成ライトを削除",
                    $"'{LightsRootName}' 配下のLightを含むGameObjectを {lights.Length} 個削除します。既存ワールドの他のLightは消しません。続行しますか？",
                    "削除",
                    "キャンセル"))
            {
                return 0;
            }

            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[VRC Bake Assistant] ツール作成Light削除: {lights.Length}");
            return lights.Length;
        }

        private int SetSelectedLightsBakeType(LightmapBakeType bakeType)
        {
            int count = 0;
            foreach (var light in Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<Light>(includeInactive)).Distinct())
            {
                Undo.RecordObject(light, "Set Light Bake Type");
                light.lightmapBakeType = bakeType;
                EditorUtility.SetDirty(light);
                count++;
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return count;
        }

        private void DisableSkyboxAndUseFlatAmbient()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.038f, 0.045f);
            RenderSettings.reflectionIntensity = 0.25f;
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[VRC Bake Assistant] SkyboxをOFFにしました。");
        }

        private void CreateOrUpdateAxisGuide()
        {
            CreateOrUpdateAxisGuide(GetOrCreateAssistantChild(GuideRootName).transform);
        }

        private void CreateOrUpdateAxisGuide(Transform parent)
        {
            EnsureFolder(MaterialFolder);
            var existing = FindChildByName(parent, AxisGuideName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var axisRoot = new GameObject(AxisGuideName);
            Undo.RegisterCreatedObjectUndo(axisRoot, "Create Axis Guide");
            axisRoot.transform.SetParent(parent, false);

            var red = CreateOrLoadMaterial("VRCBake_Axis_X_Red", new Color(0.9f, 0.1f, 0.1f), 0f, 0.2f);
            var green = CreateOrLoadMaterial("VRCBake_Axis_Y_Green", new Color(0.1f, 0.8f, 0.1f), 0f, 0.2f);
            var blue = CreateOrLoadMaterial("VRCBake_Axis_Z_Blue", new Color(0.1f, 0.25f, 0.9f), 0f, 0.2f);

            CreateCube("Axis_X_Red_NotBaked", new Vector3(2.5f, 0.03f, 0), new Vector3(5.0f, 0.05f, 0.05f), red, false).transform.SetParent(axisRoot.transform, true);
            CreateCube("Axis_Y_Green_NotBaked", new Vector3(0, 1.25f, 0), new Vector3(0.05f, 2.5f, 0.05f), green, false).transform.SetParent(axisRoot.transform, true);
            CreateCube("Axis_Z_Blue_NotBaked", new Vector3(0, 0.035f, 2.5f), new Vector3(0.05f, 0.05f, 5.0f), blue, false).transform.SetParent(axisRoot.transform, true);

            Selection.activeGameObject = axisRoot;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[VRC Bake Assistant] 原点の軸を追加/更新しました。");
        }

        private static GameObject CreateCube(string name, Vector3 position, Vector3 scale, Material material, bool contributeGI)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            var collider = go.GetComponent<Collider>();
            if (collider != null && !contributeGI && (name.IndexOf("Guide", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

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

        private GameObject GetOrCreateAssistantRoot()
        {
            var root = GameObject.Find(AssistantRootName);
            if (root != null) return root;

            root = new GameObject(AssistantRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create VRC Bake Assistant Root");
            return root;
        }

        private GameObject GetOrCreateAssistantChild(string childName)
        {
            var root = GetOrCreateAssistantRoot();

            var sampleRoot = FindChildByName(root.transform, PracticeRootName);
            Transform parent = sampleRoot != null ? sampleRoot.transform : root.transform;

            var child = FindChildByName(parent, childName);
            if (child != null) return child;

            child = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(child, "Create VRC Bake Assistant Child");
            child.transform.SetParent(parent, false);
            return child;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Child");
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject FindChildByName(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child.gameObject;
            }
            return null;
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
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) yield break;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null) continue;
                if (!includeInactiveObjects && !root.activeInHierarchy) continue;

                var components = root.GetComponentsInChildren<T>(includeInactiveObjects);
                for (int c = 0; c < components.Length; c++)
                {
                    var component = components[c];
                    if (component == null) continue;
                    yield return component;
                }
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

        private static bool IsBoundsSourceRenderer(Renderer renderer)
        {
            if (!IsBakeTargetRenderer(renderer)) return false;
            if (IsAssistantGuideRenderer(renderer)) return false;
            return true;
        }

        private static bool IsAssistantGuideRenderer(Renderer renderer)
        {
            if (renderer == null) return false;
            string path = GetHierarchyPath(renderer.transform);
            return path.IndexOf("Guide", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("Axis", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("BoundsGuide", StringComparison.OrdinalIgnoreCase) >= 0
                   || path.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLikelyDynamicRenderer(Renderer renderer)
        {
            if (renderer == null) return false;
            if (renderer is SkinnedMeshRenderer) return true;
            string path = GetHierarchyPath(renderer.transform).ToLowerInvariant();
            string[] dynamicWords =
            {
                "dynamic", "notbaked", "not_baked", "avatar", "pickup", "vrc", "player", "mirror",
                "door", "button", "switch", "particle", "fx", "audio", "camera", "guide", "axis",
                "bounds", "reflection_check"
            };
            return dynamicWords.Any(w => path.Contains(w));
        }

        private static bool RendererHasMissingUv2(Renderer renderer)
        {
            Mesh mesh = null;
            if (renderer.TryGetComponent<MeshFilter>(out var meshFilter)) mesh = meshFilter.sharedMesh;
            if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
            if (mesh == null) return false;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path)) return false;
            if (path.Contains("unity_builtin_extra")) return false;

            // mesh.uv2 は配列を返すため、大きいMeshではEditorメモリを大きく消費します。
            // チャンネルの有無だけを確認するため、配列を作らない HasVertexAttribute を使います。
            return !mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
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
                if (!IsBoundsSourceRenderer(renderer)) continue;
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

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string GetLightTypeName(Light light)
        {
            if (light == null) return string.Empty;
            switch (light.type)
            {
                case LightType.Directional: return "Directional";
                case LightType.Point: return "Point";
                case LightType.Spot: return "Spot";
                case LightType.Rectangle: return "Area";
                case LightType.Disc: return "Area";
                default: return light.type.ToString();
            }
        }

        private static string GetLightBakeStateLabel(Light light)
        {
            if (light == null) return string.Empty;
            if (!light.enabled) return "無効";
            switch (light.lightmapBakeType)
            {
                case LightmapBakeType.Baked: return "Bake使用";
                case LightmapBakeType.Mixed: return "Bake+実行時";
                case LightmapBakeType.Realtime: return "実行時のみ";
                default: return light.lightmapBakeType.ToString();
            }
        }

        private static string GetPresetName(BakePreset selectedPreset)
        {
            switch (selectedPreset)
            {
                case BakePreset.QuickCheck: return "試し焼き（早い）";
                case BakePreset.QuestLight: return "Quest向け軽量";
                case BakePreset.PCStandard: return "PC向け標準";
                case BakePreset.FinalCheck: return "仕上げ確認（重い）";
                default: return selectedPreset.ToString();
            }
        }

        private static string GetPresetDescription(BakePreset selectedPreset)
        {
            switch (selectedPreset)
            {
                case BakePreset.QuickCheck:
                    return "用途: 最初の確認用。光の方向、部屋の明るさ、影の出方を短時間で見るための低品質設定です。最終提出用ではありません。";
                case BakePreset.QuestLight:
                    return "用途: Quest/Android向け。Lightmap容量と負荷を抑えたいときに使います。見た目より軽さを優先します。";
                case BakePreset.PCStandard:
                    return "用途: PCワールドの普段使い。見た目とベイク時間のバランスを取りたいときの基準設定です。迷ったらこれ。";
                case BakePreset.FinalCheck:
                    return "用途: 公開前の最終確認。きれいになりやすい代わりに、ベイク時間とLightmap容量が増えます。最後だけ使う想定です。";
                default:
                    return string.Empty;
            }
        }

        private static string GetPresetTechnicalSummary(BakePreset selectedPreset)
        {
            switch (selectedPreset)
            {
                case BakePreset.QuickCheck: return "低解像度 / 少サンプル / Non-Directional / かなり早い";
                case BakePreset.QuestLight: return "軽量寄り / Non-Directional / Quest確認向け";
                case BakePreset.PCStandard: return "中品質 / Directional / Shadowmask / PC基準";
                case BakePreset.FinalCheck: return "高品質 / 高解像度 / 高サンプル / 時間と容量に注意";
                default: return string.Empty;
            }
        }

        private static LightSnapshot CreateLightSnapshot(Light light)
        {
            return new LightSnapshot
            {
                globalId = GetGlobalObjectId(light),
                path = GetHierarchyPath(light.transform),
                signature = BuildLightSignature(light),
                type = light.type.ToString(),
                bakeType = light.lightmapBakeType.ToString(),
                enabled = light.enabled
            };
        }

        private static string GetGlobalObjectId(UnityEngine.Object obj)
        {
            try
            {
                return GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
            }
            catch
            {
                return obj != null ? obj.GetInstanceID().ToString(CultureInfo.InvariantCulture) : string.Empty;
            }
        }

        private static string BuildLightSignature(Light light)
        {
            var t = light.transform;
            return string.Join("|", new[]
            {
                light.enabled.ToString(),
                light.type.ToString(),
                light.lightmapBakeType.ToString(),
                F(light.intensity),
                F(light.range),
                F(light.spotAngle),
                light.color.r.ToString("F4", CultureInfo.InvariantCulture),
                light.color.g.ToString("F4", CultureInfo.InvariantCulture),
                light.color.b.ToString("F4", CultureInfo.InvariantCulture),
                light.shadows.ToString(),
                F(light.areaSize.x),
                F(light.areaSize.y),
                Vec(t.position),
                Vec(t.eulerAngles),
                Vec(t.lossyScale)
            });
        }

        private static string F(float value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }

        private static string Vec(Vector3 v)
        {
            return v.x.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   v.y.ToString("F4", CultureInfo.InvariantCulture) + "," +
                   v.z.ToString("F4", CultureInfo.InvariantCulture);
        }

        private BakeSnapshot LoadBakeSnapshot()
        {
            try
            {
                if (!File.Exists(BakeSnapshotAssetPath)) return null;
                string json = File.ReadAllText(BakeSnapshotAssetPath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonUtility.FromJson<BakeSnapshot>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRC Bake Assistant] Bakeメモの読み込みに失敗: " + ex.Message);
                return null;
            }
        }

        private void SaveBakeSnapshot()
        {
            EnsureFolder(ReportFolder);
            var scene = SceneManager.GetActiveScene();
            var snapshot = new BakeSnapshot
            {
                capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scenePath = scene.path,
                sceneName = scene.name,
                lights = FindSceneComponents<Light>(includeInactive)
                    .Where(l => l.lightmapBakeType == LightmapBakeType.Baked || l.lightmapBakeType == LightmapBakeType.Mixed)
                    .Select(CreateLightSnapshot)
                    .ToList()
            };

            string json = JsonUtility.ToJson(snapshot, true);
            File.WriteAllText(BakeSnapshotAssetPath, json);
            AssetDatabase.Refresh();
            cachedSnapshot = snapshot;
            Debug.Log($"[VRC Bake Assistant] Bakeメモを保存しました: {snapshot.lights.Count} lights");
        }

        private void DeleteBakeSnapshot()
        {
            if (File.Exists(BakeSnapshotAssetPath)) File.Delete(BakeSnapshotAssetPath);
            string meta = BakeSnapshotAssetPath + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            AssetDatabase.Refresh();
        }

        private void OnBakeStarted()
        {
            SetStatus("ベイクを開始しました。完了までUnityのLighting処理を待ちます。", MessageType.Info);
            Debug.Log("[VRC Bake Assistant] ベイク開始。");
        }

        private void OnBakeCompleted()
        {
            Debug.Log("[VRC Bake Assistant] ベイク完了。");
            SaveBakeSnapshot();
            rendererItems.Clear();
            lightItems.Clear();
            lastReport = ScanScene();
            SetStatus("ベイクが完了しました。前回Bakeメモを保存しました。シーンビューで明るさ、アバター位置、金属球の反射を確認してください。", MessageType.Info);
            Repaint();
        }

        private void RepaintWhileBaking()
        {
            if (Lightmapping.isRunning) Repaint();
        }
    }
}
#endif
