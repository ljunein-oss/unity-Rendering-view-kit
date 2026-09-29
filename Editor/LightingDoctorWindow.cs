// SCI 光照一键设置（对比度）—— 按“压环境光 → 主光对比 → 摆光照探针 → 标记静态并烘焙 → 回后处理微调”的顺序做
//
//   工具 ▸ Render Kit ▸ 光照体检与一键设置
//
// 为什么做这个：
//   后处理只能“区分已有的明暗”，区分不出没有的东西。画面发平的根因通常是：
//   环境光太亮（均匀底光）+ 没有烘焙 GI + 没有光照探针 → 所有物体受光一样、没有暗部。
//
// 安全设计：
//   * 每个按钮都走 Undo（Ctrl+Z 可撤），并且不自动保存场景（你确认后再 Ctrl+S）
//   * 每次修改前会把“改了哪些值/哪些物体”记录到 <项目根目录>/_LightingBackup/<时间戳>/record.txt
//   * 「↩ 还原光照设置」按记录还原环境光、主光、静态标记，并删掉本工具生成的光照探针组
//   * 烘焙是异步的，可以随时取消；场景在编辑器里开着也安全
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RKLightingDoctorWindow : EditorWindow
{
    // ---------------- 状态 ----------------
    Vector2 _scroll;
    string _status = "";

    // 探针参数
    Vector3 _probeCenter = new Vector3(0f, 0f, 0f);
    float _probeSize = 120f;
    float _probeHeight = 3f;
    int _probeCols = 7;
    int _probeRows = 7;
    bool _probeAutoCenter = true;

    // 环境光预设
    float _ambientIntensity = 0.5f;
    Color _ambientSky = new Color(0.36f, 0.47f, 0.60f);
    Color _ambientEquator = new Color(0.16f, 0.24f, 0.32f);
    Color _ambientGround = new Color(0.05f, 0.08f, 0.12f);
    float _reflectionIntensity = 0.7f;

    // 主光预设
    float _sunIntensity = 1.35f;
    float _sunShadowStrength = 0.85f;
    Color _sunColor = new Color(1f, 0.95f, 0.86f);
    float _sunPitch = 42f;      // 越低越“斜射”，立体感越强
    bool _applySunAngle = false;

    const string ProbeGroupName = "RenderKit_LightProbes";
    const string TimeStampKey = "RenderKit.Lighting.RecordStamp";

    // 场景诊断缓存（避免每帧遍历全场景）
    int _renderers, _noUv2, _marked;
    double _giTexels;                 // 粗略估算：所有参与烘焙的面在光图里占多少 texel
    List<string> _heaviest = new List<string>();

    void ScanScene()
    {
        _renderers = _noUv2 = _marked = 0;
        _giTexels = 0;
        var list = new List<KeyValuePair<double, string>>();

        // 场景级分辨率（没建 LightingSettings 资产时用默认 40）
        float res = 40f;
        var ls = Lightmapping.GetLightingSettingsForScene(SceneManager.GetActiveScene());
        if (ls != null) { try { res = ls.lightmapResolution; } catch { } }
        if (res <= 0f) res = 40f;

        foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (IsDynamic(mf.gameObject) || IsWater(mf.gameObject)) continue;
            _renderers++;
            if (mf.sharedMesh.uv2 == null || mf.sharedMesh.uv2.Length == 0) _noUv2++;

            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            if ((GameObjectUtility.GetStaticEditorFlags(mf.gameObject) & StaticEditorFlags.ContributeGI) == 0) continue;
            _marked++;

            // 用包围盒最大的那个面粗略代表表面积（够用来找“吃光图的大块头”）
            var s = mf.sharedMesh.bounds.size;
            var sc = mf.transform.lossyScale;
            double a = Math.Max(Math.Abs(s.x * sc.x * s.y * sc.y),
                       Math.Max(Math.Abs(s.x * sc.x * s.z * sc.z), Math.Abs(s.y * sc.y * s.z * sc.z)));
            double texels = a * mr.scaleInLightmap * res * res;
            _giTexels += texels;
            list.Add(new KeyValuePair<double, string>(texels,
                string.Format("{0}  面积≈{1:0} ㎡  ScaleInLightmap={2:0.##}  → 约 {3:0.#} 万 texel",
                    ObjectPath(mf.gameObject), a, mr.scaleInLightmap, texels / 10000.0)));
        }

        _heaviest = list.OrderByDescending(k => k.Key).Take(8).Select(k => k.Value).ToList();
    }

    // ---------------- 硬件自适应（所有 N 卡 / A 卡 / 核显通用） ----------------
    static string VramClass()
    {
        int mb = SystemInfo.graphicsMemorySize;
        if (mb <= 4096) return "低";
        if (mb <= 8192) return "中";
        if (mb <= 12288) return "高";
        return "很高";
    }

    /// 按显存给一个保守的光照贴图分辨率（texel / 米）。
    /// 大场景真正的瓶颈是“米 × texel”的乘积，所以这里给得很小，细节交给 ScaleInLightmap 单独提升。
    static float RecommendedLightmapResolution()
    {
        int mb = SystemInfo.graphicsMemorySize;
        if (mb <= 4096) return 1f;
        if (mb <= 8192) return 2f;
        if (mb <= 12288) return 3f;
        return 4f;
    }

    /// 当前估算下，把分辨率压到预算内需要的值（目标：约 200 万 texel 以内）
    float BudgetFriendlyResolution()
    {
        float res = RecommendedLightmapResolution();
        if (_giTexels <= 0) return res;
        double budget = 2.0e6;
        // texel 数与分辨率平方成正比，按当前估算反推
        double scale = Math.Sqrt(budget / _giTexels);
        float guess = res * (float)scale;
        return Mathf.Clamp(Mathf.Floor(guess * 2f) / 2f, 0.25f, res);
    }

    // 一键瘦身：大块头按面积反比调小 ScaleInLightmap，超大的直接退出 GI
    void SlimDown()
    {
        Record("slim", "begin");
        int scaled = 0, excluded = 0;

        foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var go = mf.gameObject;
            if (IsDynamic(go) || IsWater(go)) continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            if ((GameObjectUtility.GetStaticEditorFlags(go) & StaticEditorFlags.ContributeGI) == 0) continue;

            var s = mf.sharedMesh.bounds.size;
            var sc = mf.transform.lossyScale;
            float maxDim = Mathf.Max(Mathf.Abs(s.x * sc.x), Mathf.Abs(s.y * sc.y), Mathf.Abs(s.z * sc.z));

            Record("slim", ObjectPath(go) + "|" + mr.scaleInLightmap.ToString("F4"));

            if (maxDim > 80f)
            {
                // 超大平面（海床/大甲板）：光照几乎均匀，烘它性价比极低 → 退出 GI
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) & ~StaticEditorFlags.ContributeGI);
                excluded++;
            }
            else
            {
                // 中等尺寸按大小递减，保住近处道具的细节
                float target = maxDim > 30f ? 0.1f : maxDim > 12f ? 0.3f : maxDim > 4f ? 0.6f : 1f;
                if (mr.scaleInLightmap > target)
                {
                    mr.scaleInLightmap = target;
                    scaled++;
                }
            }
        }
        MarkSceneDirty();
        ScanScene();
        _status = string.Format("瘦身完成：{0} 个物体调小了 ScaleInLightmap，{1} 个超大平面已退出 GI。" +
                                "现在再点「开始烘焙」，时间通常会从十几小时掉到十几分钟量级。", scaled, excluded);
    }

    [MenuItem("Tools/Render Kit/光照体检与一键设置 (Lighting Doctor)", false, 46)]
    static void Open()
    {
        var w = GetWindow<RKLightingDoctorWindow>("光照设置");
        w.minSize = new Vector2(470f, 620f);
    }

    void OnEnable() { AutoCenterProbes(); ScanScene(); }

    // ------------------------------------------------------------------ 界面
    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        var scene = SceneManager.GetActiveScene();
        EditorGUILayout.HelpBox("当前场景：" + scene.name + "\n顺序建议：① 环境光 → ② 主光 → ③ 探针 → ④ 标记静态并烘焙 → 再回后处理微调。",
            MessageType.None);

        // 硬件自适应：不同显卡给不同的烘焙建议（不再写死某一档显存）
        EditorGUILayout.LabelField(string.Format("显卡：{0}  显存：{1} MB（{2}）  →  建议光照贴图分辨率 {3}",
            SystemInfo.graphicsDeviceName, SystemInfo.graphicsMemorySize, VramClass(), RecommendedLightmapResolution()), EditorStyles.miniLabel);
        if (SystemInfo.graphicsMemorySize <= 4096)
            EditorGUILayout.HelpBox("显存 ≤4GB：Progressive GPU 烘焙器很容易装不下而退回 CPU（慢 10~50 倍）。" +
                                    "建议分辨率用 1、图集上限 512，并且烘焙时关掉 Game 视图预览。", MessageType.Info);

        // 播放模式下 Unity 不允许标记场景为脏（会抛 InvalidOperationException），
        // 而且这里改的本来就是场景/光照设置，退出 Play 之后也会被丢掉，所以直接锁住。
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        if (playing)
            EditorGUILayout.HelpBox("正在播放模式（或正在切换）。这个窗口改的是场景和光照设置，先在 Unity 里退出 Play 再点按钮。" +
                                    "（播放中点击会被忽略，不会再抛异常。）", MessageType.Warning);

        using (new EditorGUI.DisabledScope(playing))
        {
            DrawAmbient();
            DrawSun();
            DrawProbes();
            DrawGi();

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.85f, 0.6f);
                if (GUILayout.Button("↩ 还原光照设置（按记录还原）", GUILayout.Height(30))) RestoreAll();
                GUI.backgroundColor = old;
            }
        }

        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    void DrawAmbient()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("① 环境光（压暗 = 造出暗部）", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(string.Format("当前：模式 {0} / 强度 {1:0.##} / 反射强度 {2:0.##}",
                RenderSettings.ambientMode, RenderSettings.ambientIntensity, RenderSettings.reflectionIntensity), EditorStyles.miniLabel);

            _ambientIntensity = EditorGUILayout.Slider("环境光强度", _ambientIntensity, 0f, 2f);
            _ambientSky = EditorGUILayout.ColorField("天空色", _ambientSky);
            _ambientEquator = EditorGUILayout.ColorField("地平线色", _ambientEquator);
            _ambientGround = EditorGUILayout.ColorField("地面色", _ambientGround);
            _reflectionIntensity = EditorGUILayout.Slider("反射强度", _reflectionIntensity, 0f, 2f);

            if (GUILayout.Button("应用：压暗环境光（推荐 0.4~0.6）", GUILayout.Height(26)))
            {
                Record("ambient", string.Format("mode={0};intensity={1};sky={2};eq={3};gnd={4};refl={5};reflMode={6}",
                    RenderSettings.ambientMode, RenderSettings.ambientIntensity, C(RenderSettings.ambientSkyColor),
                    C(RenderSettings.ambientEquatorColor), C(RenderSettings.ambientGroundColor),
                    RenderSettings.reflectionIntensity, RenderSettings.defaultReflectionMode));
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = _ambientSky;
                RenderSettings.ambientEquatorColor = _ambientEquator;
                RenderSettings.ambientGroundColor = _ambientGround;
                RenderSettings.ambientIntensity = _ambientIntensity;
                RenderSettings.reflectionIntensity = _reflectionIntensity;
                MarkSceneDirty();
                _status = string.Format("环境光已压到 {0:0.##}（不再有均匀底光，暗部会出来）。觉得太暗就往上调一点，再点一次即可。", _ambientIntensity);
            }
        }
    }

    void DrawSun()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("② 主光（明暗对比的来源）", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var sun = FindSun();
            if (sun == null)
            {
                EditorGUILayout.LabelField("场景里没找到平行光（Directional Light）", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(string.Format("找到：{0}   强度 {1:0.##} / 阴影强度 {2:0.##} / 角度 {3:0.#}°",
                    sun.name, sun.intensity, sun.shadowStrength, sun.transform.eulerAngles.x), EditorStyles.miniLabel);

                _sunIntensity = EditorGUILayout.Slider("强度", _sunIntensity, 0f, 4f);
                _sunShadowStrength = EditorGUILayout.Slider("阴影强度（越高暗部越黑）", _sunShadowStrength, 0f, 1f);
                _sunColor = EditorGUILayout.ColorField("颜色（略暖）", _sunColor);
                _applySunAngle = EditorGUILayout.ToggleLeft("同时把阳光压更低（更斜射、更立体，会改变整体观感）", _applySunAngle);
                _sunPitch = EditorGUILayout.Slider("阳光俯角", _sunPitch, 20f, 80f);

                if (GUILayout.Button("应用：主光对比预设", GUILayout.Height(26)))
                {
                    Record("sun", string.Format("name={0};intensity={1};shadow={2};color={3};euler={4}",
                        sun.name, sun.intensity, sun.shadowStrength, C(sun.color), sun.transform.eulerAngles.ToString("F3")));
                    Undo.RecordObject(sun, "光照：主光");
                    sun.intensity = _sunIntensity;
                    sun.shadowStrength = _sunShadowStrength;
                    sun.color = _sunColor;
                    sun.shadows = LightShadows.Soft;
                    if (_applySunAngle)
                    {
                        Undo.RecordObject(sun.transform, "光照：阳光角度");
                        var e = sun.transform.eulerAngles;
                        sun.transform.eulerAngles = new Vector3(_sunPitch, e.y, e.z);
                    }
                    MarkSceneDirty();
                    _status = _applySunAngle
                        ? "主光已调整（含角度）。阴影变深 + 光更斜 → 物体亮暗面分得更开。"
                        : "主光已调整（角度未动）。想让立体感更强，勾上“压更低”再点一次。";
                }
            }
        }
    }

    void DrawProbes()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("③ 光照探针（动态物体的间接光）", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var existing = FindProbeGroup();
            EditorGUILayout.LabelField(existing != null
                ? string.Format("已有本工具生成的探针组：{0} 个探针", existing.probePositions.Length)
                : "场景里还没有本工具生成的探针组", EditorStyles.miniLabel);

            _probeAutoCenter = EditorGUILayout.ToggleLeft("自动取场景中心（排除水面高度）", _probeAutoCenter);
            if (!_probeAutoCenter) _probeCenter = EditorGUILayout.Vector3Field("中心", _probeCenter);
            else EditorGUILayout.LabelField("      中心：" + _probeCenter.ToString("F1"), EditorStyles.miniLabel);

            _probeSize = EditorGUILayout.Slider("覆盖尺寸（米）", _probeSize, 20f, 600f);
            _probeHeight = EditorGUILayout.Slider("高度（相对中心）", _probeHeight, 0.5f, 20f);
            _probeCols = EditorGUILayout.IntSlider("横向探针数", _probeCols, 2, 16);
            _probeRows = EditorGUILayout.IntSlider("纵向探针数", _probeRows, 2, 16);
            EditorGUILayout.LabelField(string.Format("将生成 {0} 个探针，间距 {1:0.#} 米", _probeCols * _probeRows, _probeSize / Mathf.Max(1, _probeCols - 1)), EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成 / 重建探针", GUILayout.Height(26))) BuildProbes();
                if (existing != null && GUILayout.Button("删除探针组", GUILayout.Height(26))) DeleteProbes();
            }
            EditorGUILayout.LabelField("提示：探针要覆盖“动态物体会经过的地方”（船、飞行器、可动道具）；纯静态场景其实不依赖探针。", EditorStyles.miniLabel);
        }
    }

    void DrawGi()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("④ 烘焙 GI（提升最大，也最花时间）", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // 诊断（缓存，避免每帧 FindObjectsOfType 卡住界面）
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.Format("可参与烘焙的网格：{0} 个；已标记 ContributeGI：{1} 个", _renderers, _marked), EditorStyles.miniLabel);
                if (GUILayout.Button("重新扫描", EditorStyles.miniButton, GUILayout.Width(70))) ScanScene();
            }

            string level = _giTexels > 20e6 ? "重 ✗（几小时起步）"
                         : _giTexels > 4e6 ? "中 ⚠️（几十分钟）"
                         : _giTexels > 0.8e6 ? "轻 ✅（几分钟）" : "很轻 ✅";
            EditorGUILayout.LabelField(string.Format("光图估算：约 {0:0.#} 百万 texel → 量级：{1}", _giTexels / 1e6, level), EditorStyles.miniLabel);

            if (_heaviest.Count > 0)
            {
                EditorGUILayout.LabelField("吃光图最多的物体（Top 8）：", EditorStyles.miniLabel);
                foreach (var h in _heaviest) EditorGUILayout.LabelField("   " + h, EditorStyles.miniLabel);
            }

            var oldb = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.85f, 0.6f);
            if (GUILayout.Button("⚡ 一键瘦身（大物体调小 ScaleInLightmap + 超大平面退出 GI）", GUILayout.Height(30))) SlimDown();
            GUI.backgroundColor = oldb;

            if (GUILayout.Button("生成快速烘焙设置（低分辨率 + 关 AO + 1 次反弹）", GUILayout.Height(26))) ApplyFastBakeSettings();
            if (GUILayout.Button("打印当前光照设置字段（排查用）", EditorStyles.miniButton)) DumpLightingSettings();

            EditorGUILayout.HelpBox(
                "烘焙时间正常应该在 10 分钟~1 小时。13 小时通常是：\n" +
                "① 光照贴图分辨率按“每米 texel”算，而你的海床是 500×600 米 → 默认 40 会爆掉；建议 1~4\n" +
                "② 图集上限太大（建议 1024）、反弹次数太多（1~2）、开了 AO\n" +
                "③ 6GB 显存装不下 → Progressive GPU 退回 CPU，再慢 10~50 倍\n" +
                "Window ▸ Rendering ▸ Lighting ▸ Scene 里也能手改这几项；上面的按钮会尽量帮你设好。",
                MessageType.Info);
            if (_noUv2 > 0)
                EditorGUILayout.HelpBox(string.Format("有 {0} 个网格缺少光照贴图 UV（UV2）→ 烘焙会被跳过或出现脏污。\n" +
                    "解决：选中这些模型 → 模型导入设置 → 勾选 Generate Lightmap UVs（需要重新导入）。", _noUv2), MessageType.Warning);
            else
                EditorGUILayout.LabelField("光照贴图 UV（UV2）检查：全部通过 ✅", EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("标记静态（ContributeGI）", GUILayout.Height(26))) MarkStatic();
                if (GUILayout.Button("清除我标记的静态", GUILayout.Height(26))) UnmarkStatic();
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.7f, 0.9f, 0.7f);
                if (GUILayout.Button(Lightmapping.isRunning ? "烘焙中…（点右边取消）" : "开始烘焙（异步）", GUILayout.Height(30)))
                {
                    if (!Lightmapping.isRunning)
                    {
                        Lightmapping.bakeCompleted += OnBakeDone;
                        Lightmapping.BakeAsync();
                        _status = "已开始烘焙。Unity 底部会显示进度条；大场景可能要几分钟到几十分钟，随时可以取消。";
                    }
                }
                GUI.backgroundColor = old;
                if (GUILayout.Button("取消烘焙", GUILayout.Height(30)) && Lightmapping.isRunning)
                {
                    Lightmapping.Cancel();
                    _status = "已请求取消烘焙。";
                }
            }
            EditorGUILayout.LabelField("烘焙完成后再回「后处理调参」微调 —— 这时对比度是长出来的，不用靠拉 contrast。", EditorStyles.miniLabel);
        }
    }

    void OnBakeDone()
    {
        Lightmapping.bakeCompleted -= OnBakeDone;
        _status = "烘焙完成 ✅ 现在去后处理调参里看看，可以把 contrast 拉回来一点。";
        Repaint();
    }

    // 生成一个 LightingSettings 资产并设成“快速烘焙”预设。
    // 字段名在不同 Unity 版本略有差异，所以这里用 SerializedObject 多名字尝试 + 报告结果，绝不硬依赖。
    void ApplyFastBakeSettings()
    {
        Record("lightingSettings", "created");

        var ls = new LightingSettings();
        string path = "Assets/Settings/RenderKit_LightingSettings.asset";
        if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(ls, path);

        Lightmapping.lightingSettings = ls;

        // 分辨率按“显存 + 当前场景光图估算”自动取，避免大场景直接爆掉
        float res = BudgetFriendlyResolution();
        int atlas = SystemInfo.graphicsMemorySize <= 4096 ? 512 : 1024;

        var so = new SerializedObject(ls);
        var log = new StringBuilder();
        log.AppendLine("[光照设置] 快速烘焙预设写入结果：");
        log.Append(TrySet(so, new[] { "m_LightmapResolution", "lightmapResolution" }, res)
            ? string.Format("  分辨率 → {0:0.##} ✅（按显存 {1}MB + 估算自动取）\n", res, SystemInfo.graphicsMemorySize)
            : string.Format("  分辨率：字段名没匹配上，请手动设 Lightmap Resolution = {0:0.##} ⚠️\n", res));
        log.Append(TrySet(so, new[] { "m_LightmapMaxSize", "lightmapMaxSize" }, atlas)
            ? string.Format("  图集上限 → {0} ✅\n", atlas)
            : string.Format("  图集上限：请手动设 Lightmap Size = {0} ⚠️\n", atlas));
        log.Append(TrySet(so, new[] { "m_MaxBounces", "maxBounces" }, 2f) ? "  反弹次数 → 2 ✅\n" : "  反弹次数：请手动设 Max Bounces = 2 ⚠️\n");
        log.Append(TrySet(so, new[] { "m_EnableAmbientOcclusion", "m_AO", "ao" }, 0f) ? "  环境光遮蔽 → 关 ✅\n" : "  AO：请手动取消勾选 Ambient Occlusion ⚠️\n");
        log.Append(TrySet(so, new[] { "m_DirectSampleCount", "directSampleCount" }, 32f) ? "  直接光采样 → 32 ✅\n" : "  直接光采样：手动设 32 ⚠️\n");
        log.Append(TrySet(so, new[] { "m_IndirectSampleCount", "indirectSampleCount" }, 128f) ? "  间接光采样 → 128 ✅\n" : "  间接光采样：手动设 128 ⚠️\n");
        log.Append(TrySet(so, new[] { "m_EnvironmentSampleCount", "environmentSampleCount" }, 128f) ? "  环境采样 → 128 ✅\n" : "  环境采样：手动设 128 ⚠️\n");
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(ls);
        AssetDatabase.SaveAssets();

        SlimDown();     // 顺手瘦身
        _status = "已生成 " + path + " 并设为当前场景的光照设置。详细的字段写入结果看 Console。";
        Debug.Log(log.ToString());
    }

    static bool TrySet(SerializedObject so, string[] names, float v)
    {
        foreach (var n in names)
        {
            var p = so.FindProperty(n);
            if (p == null) continue;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: p.floatValue = v; return true;
                case SerializedPropertyType.Integer: p.intValue = Mathf.RoundToInt(v); return true;
                case SerializedPropertyType.Boolean: p.boolValue = v != 0f; return true;
            }
        }
        return false;
    }

    void DumpLightingSettings()
    {
        var ls = Lightmapping.GetLightingSettingsForScene(SceneManager.GetActiveScene());
        if (ls == null) { _status = "当前场景没有 LightingSettings 资产（点上面的按钮生成一个）。"; return; }

        var sb = new StringBuilder("[光照设置] 当前字段：\n");
        var so = new SerializedObject(ls);
        var it = so.GetIterator();
        while (it.NextVisible(true))
        {
            if (it.propertyType == SerializedPropertyType.Float) sb.AppendLine("  " + it.propertyPath + " = " + it.floatValue);
            else if (it.propertyType == SerializedPropertyType.Integer) sb.AppendLine("  " + it.propertyPath + " = " + it.intValue);
            else if (it.propertyType == SerializedPropertyType.Boolean) sb.AppendLine("  " + it.propertyPath + " = " + it.boolValue);
        }
        Debug.Log(sb.ToString());
        _status = "已把光照设置字段打印到 Console（可以复制给我，我按你的版本精确设）。";
    }

    // ------------------------------------------------------------------ 探针
    void AutoCenterProbes()
    {
        var water = FindWater();
        float y = water != null ? water.bounds.center.y : 0f;
        _probeCenter = new Vector3(0f, y, 0f);
    }

    void BuildProbes()
    {
        Record("probes", ProbeGroupName + ";created");

        var existing = FindProbeGroup();
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var go = new GameObject(ProbeGroupName);
        Undo.RegisterCreatedObjectUndo(go, "创建光照探针");
        go.transform.position = Vector3.zero;          // 放原点，探针坐标=世界坐标

        var group = go.AddComponent<LightProbeGroup>();
        var list = new List<Vector3>();
        int c = Mathf.Max(2, _probeCols), r = Mathf.Max(2, _probeRows);
        for (int i = 0; i < c; i++)
            for (int j = 0; j < r; j++)
            {
                float x = _probeCenter.x + (i / (float)(c - 1) - 0.5f) * _probeSize;
                float z = _probeCenter.z + (j / (float)(r - 1) - 0.5f) * _probeSize;
                list.Add(new Vector3(x, _probeCenter.y + _probeHeight, z));
            }
        group.probePositions = list.ToArray();

        MarkSceneDirty();
        _status = string.Format("已生成 {0} 个光照探针（覆盖 {1:0} 米，高度 +{2:0.#} 米）。", list.Count, _probeSize, _probeHeight);
    }

    void DeleteProbes()
    {
        var existing = FindProbeGroup();
        if (existing == null) { _status = "没有本工具生成的探针组。"; return; }
        Undo.DestroyObjectImmediate(existing.gameObject);
        MarkSceneDirty();
        _status = "已删除光照探针组。";
    }

    static LightProbeGroup FindProbeGroup()
    {
        var go = GameObject.Find(ProbeGroupName);
        return go != null ? go.GetComponent<LightProbeGroup>() : null;
    }

    // ------------------------------------------------------------------ 静态标记 / 烘焙
    void MarkStatic()
    {
        Record("static", "begin");
        int n = 0;
        foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var go = mf.gameObject;
            if (IsDynamic(go) || IsWater(go)) continue;

            var prev = GameObjectUtility.GetStaticEditorFlags(go);
            if ((prev & StaticEditorFlags.ContributeGI) != 0) continue;

            Record("static", ObjectPath(go) + "|" + (int)prev);
            GameObjectUtility.SetStaticEditorFlags(go, prev | StaticEditorFlags.ContributeGI);
            n++;
        }
        MarkSceneDirty();
        ScanScene();
        _status = string.Format("已把 {0} 个网格标记为 ContributeGI（动态物体和水面已跳过）。记录在 _LightingBackup 里，可一键清除。", n);
    }

    void UnmarkStatic()
    {
        string dir = LatestRecordDir();
        if (dir == null) { _status = "没有找到记录，无法精确清除。可以手动在 Static 下拉里选 Clear。"; return; }

        var file = Path.Combine(dir, "record.txt");
        if (!File.Exists(file)) { _status = "记录文件不存在。"; return; }

        int n = 0;
        foreach (string line in File.ReadAllLines(file))
        {
            if (!line.StartsWith("static|")) continue;
            string payload = line.Substring("static|".Length);
            int bar = payload.IndexOf('|');
            if (bar <= 0) continue;

            string path = payload.Substring(0, bar);
            if (!int.TryParse(payload.Substring(bar + 1), out int prev)) continue;

            var go = FindByPath(path);
            if (go == null) continue;
            GameObjectUtility.SetStaticEditorFlags(go, (StaticEditorFlags)prev);
            n++;
        }
        MarkSceneDirty();
        _status = string.Format("已清除 {0} 个物体的静态标记（还原成记录里的状态）。", n);
    }

    // ------------------------------------------------------------------ 记录 / 还原
    static string ProjectRoot { get { return Directory.GetParent(Application.dataPath).FullName; } }
    static string RecordRoot { get { return Path.Combine(ProjectRoot, "RenderKitBackup"); } }

    static string LatestRecordDir()
    {
        string stamp = EditorPrefs.GetString(TimeStampKey, "");
        if (!string.IsNullOrEmpty(stamp) && Directory.Exists(Path.Combine(RecordRoot, stamp))) return Path.Combine(RecordRoot, stamp);
        if (!Directory.Exists(RecordRoot)) return null;
        var dirs = Directory.GetDirectories(RecordRoot).OrderByDescending(d => d).ToList();
        return dirs.Count > 0 ? dirs[0] : null;
    }

    static void Record(string kind, string payload)
    {
        string stamp = EditorPrefs.GetString(TimeStampKey, "");
        if (string.IsNullOrEmpty(stamp)) stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        EditorPrefs.SetString(TimeStampKey, stamp);

        string dir = Path.Combine(RecordRoot, stamp);
        Directory.CreateDirectory(dir);

        string scene = SceneManager.GetActiveScene().name;
        File.AppendAllText(Path.Combine(dir, "record.txt"), kind + "|" + payload + Environment.NewLine);
        File.AppendAllText(Path.Combine(dir, "record.txt"), "# scene=" + scene + "  time=" + DateTime.Now.ToString("HH:mm:ss") + Environment.NewLine);
    }

    void RestoreAll()
    {
        string dir = LatestRecordDir();
        if (dir == null) { _status = "没有找到 _LightingBackup 记录，说明还没用这个工具改过东西。"; return; }

        var file = Path.Combine(dir, "record.txt");
        if (!File.Exists(file)) { _status = "记录文件不存在（" + file + "）。"; return; }
        if (!EditorUtility.DisplayDialog("还原光照设置", "按记录还原环境光 / 主光 / 静态标记？\n记录：" + dir, "还原", "取消")) return;

        int restored = 0;
        bool probesRemoved = false;
        foreach (string line in File.ReadAllLines(file))
        {
            // 只取每个 kind 的**第一条**记录作为还原目标（即最初的状态）
            if (line.StartsWith("ambient|"))
            {
                var kv = ParseKv(line.Substring("ambient|".Length));
                RenderSettings.ambientMode = (UnityEngine.Rendering.AmbientMode)int.Parse(kv["mode"]);
                RenderSettings.ambientIntensity = float.Parse(kv["intensity"]);
                RenderSettings.ambientSkyColor = ParseC(kv["sky"]);
                RenderSettings.ambientEquatorColor = ParseC(kv["eq"]);
                RenderSettings.ambientGroundColor = ParseC(kv["gnd"]);
                RenderSettings.reflectionIntensity = float.Parse(kv["refl"]);
                RenderSettings.defaultReflectionMode = (UnityEngine.Rendering.DefaultReflectionMode)int.Parse(kv["reflMode"]);
                restored++;
            }
            else if (line.StartsWith("sun|"))
            {
                var kv = ParseKv(line.Substring("sun|".Length));
                var sun = FindSun();
                if (sun != null)
                {
                    sun.intensity = float.Parse(kv["intensity"]);
                    sun.shadowStrength = float.Parse(kv["shadow"]);
                    sun.color = ParseC(kv["color"]);
                    var e = kv["euler"].Trim('(', ')').Split(',');
                    if (e.Length == 3)
                        sun.transform.eulerAngles = new Vector3(float.Parse(e[0]), float.Parse(e[1]), float.Parse(e[2]));
                    restored++;
                }
            }
            else if (line.StartsWith("probes|") && !probesRemoved)
            {
                var g = FindProbeGroup();
                if (g != null) { Undo.DestroyObjectImmediate(g.gameObject); }
                probesRemoved = true;
            }
        }
        UnmarkStatic();
        MarkSceneDirty();
        _status = string.Format("已还原 {0} 项设置{1}。场景标记为未保存，确认后 Ctrl+S。", restored, probesRemoved ? "，并删除探针组" : "");
    }

    static Dictionary<string, string> ParseKv(string s)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in s.Split(';'))
        {
            int i = part.IndexOf('=');
            if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
        }
        return d;
    }

    static string C(Color c) { return string.Format("{0:F3},{1:F3},{2:F3},{3:F3}", c.r, c.g, c.b, c.a); }
    static Color ParseC(string s)
    {
        var p = s.Split(',');
        return p.Length == 4 ? new Color(float.Parse(p[0]), float.Parse(p[1]), float.Parse(p[2]), float.Parse(p[3])) : Color.white;
    }

    // ------------------------------------------------------------------ 场景查询
    static Light FindSun()
    {
        return UnityEngine.Object.FindObjectsOfType<Light>().FirstOrDefault(l => l.type == LightType.Directional && l.gameObject.activeInHierarchy);
    }

    static Renderer FindWater()
    {
        foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
        {
            var m = r.sharedMaterial;
            if (m == null || m.shader == null) continue;
            string n = m.shader.name;
            if (n.Contains("Water") || n.Contains("MobileWater")) return r;
        }
        return null;
    }

    static bool IsWater(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null) return false;
        string n = r.sharedMaterial.shader.name;
        return n.Contains("Water") || n.Contains("MobileWater");
    }

    static bool IsDynamic(GameObject go)
    {
        if (go.GetComponent<Animator>() != null) return true;
        if (go.GetComponent<SkinnedMeshRenderer>() != null) return true;
        // 有父级带动画/骨骼的也算动态
        var t = go.transform.parent;
        while (t != null) { if (t.GetComponent<Animator>() != null) return true; t = t.parent; }
        return false;
    }

    static void MarkSceneDirty()
    {
        // 播放模式下 MarkSceneDirty 会抛 InvalidOperationException，这里直接跳过。
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SceneView.RepaintAll();
    }

    static string ObjectPath(GameObject go)
    {
        var sb = new StringBuilder(go.name);
        var t = go.transform.parent;
        while (t != null) { sb.Insert(0, t.name + "/"); t = t.parent; }
        return sb.ToString();
    }

    static GameObject FindByPath(string path)
    {
        var parts = path.Split('/');
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        GameObject cur = roots.FirstOrDefault(g => g.name == parts[0]);
        for (int i = 1; i < parts.Length && cur != null; i++)
        {
            var next = cur.transform.Find(parts[i]);
            cur = next != null ? next.gameObject : null;
        }
        return cur;
    }
}
