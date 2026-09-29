// Render Kit · 后处理调参（亮暗对比）—— 直接在 Unity 里调 Volume Profile，带预设 + 一键回退
//
//   工具 ▸ Render Kit ▸ 后处理调参
//
// 为什么做这个：
//   主美说"亮暗不分明"，绝大多数情况不是灯不够亮，而是**后处理把黑位抬起来了 / 在降饱和 / 颗粒太大**。
//   这个窗口把所有会影响"明暗分明"的参数集中到一页，带推荐预设，改完立刻在 Game 视图看到效果。
//
// 安全设计：
//   * 第一次调参前会把整个 Volume Profile 文件备份到 <项目根目录>/RenderKitBackup/PostFX/<时间戳>/
//   * 「↩ 恢复调参前的数值」= 文件级还原（连组件增删一起还原）
//   * 所有改动走 Undo，Ctrl+Z 也能撤
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if RTK_URP

public class RKPostFxTunerWindow : EditorWindow
{
    const string BackupRootName = "RenderKitBackup";
    const string Key_Profile = "RenderKit.PostFx.Profile";

    [MenuItem("Tools/Render Kit/后处理调参 (Post FX Tuner)", false, 45)]
    static void Open()
    {
        var w = GetWindow<RKPostFxTunerWindow>("后处理调参");
        w.minSize = new Vector2(470f, 620f);
    }

    List<VolumeProfile> _profiles = new List<VolumeProfile>();
    int _index;
    Vector2 _scroll;
    string _status = "";

    void OnEnable() { Refresh(); }

    void Refresh()
    {
        _profiles = AssetDatabase.FindAssets("t:VolumeProfile")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<VolumeProfile>)
            .Where(p => p != null)
            .OrderBy(p => p.name == "SCI_SceneFilter" ? 0 : 1).ThenBy(p => p.name)
            .ToList();

        string saved = EditorPrefs.GetString(Key_Profile, "");
        if (!string.IsNullOrEmpty(saved))
        {
            int i = _profiles.FindIndex(p => AssetDatabase.GetAssetPath(p) == saved);
            if (i >= 0) _index = i;
        }
    }

    VolumeProfile Profile { get { return _profiles.Count > 0 ? _profiles[Mathf.Clamp(_index, 0, _profiles.Count - 1)] : null; } }

    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(50))) Refresh();
            if (_profiles.Count == 0) { EditorGUILayout.LabelField("没找到 Volume Profile"); EditorGUILayout.EndScrollView(); return; }

            var names = _profiles.Select(p => p.name).ToArray();
            int ni = EditorGUILayout.Popup(_index, names);
            if (ni != _index)
            {
                _index = ni;
                EditorPrefs.SetString(Key_Profile, AssetDatabase.GetAssetPath(Profile));
            }
        }
        EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(Profile), EditorStyles.miniLabel);

        if (Profile == null) { EditorGUILayout.EndScrollView(); return; }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("预设（点一下立刻生效，可回退）", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("① 干净对比\n（推荐起手）", GUILayout.Height(44))) ApplyPreset(Preset.Clean);
            if (GUILayout.Button("② 阴郁高对比\n（参考片那种暗调）", GUILayout.Height(44))) ApplyPreset(Preset.Moody);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("③ 明亮通透", GUILayout.Height(32))) ApplyPreset(Preset.Bright);
            if (GUILayout.Button("④ 中性化（清掉所有色偏）", GUILayout.Height(32))) ApplyPreset(Preset.Neutral);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("快速动作", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("压暗阴影（推荐先点）", GUILayout.Height(26))) { ApplySmh(0.8f, 1.0f, 1.05f); _status = "已压暗阴影（Shadows/Midtones/Highlights）"; }
            if (GUILayout.Button("加强对比（压阴影+提亮高光）", GUILayout.Height(26))) { ApplySmh(0.65f, 1.0f, 1.12f); _status = "已加强明暗对比"; }
            if (GUILayout.Button("去掉颗粒", GUILayout.Height(26))) { SetGrain(0f); _status = "FilmGrain 已关"; }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("逐项微调（实时生效）", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("— 明暗骨架 —", EditorStyles.miniBoldLabel);
        Float("ColorAdjustments", "postExposure", "整体曝光（抬高=发灰，建议 0~0.1）", -2f, 2f);
        Float("ColorAdjustments", "contrast", "对比度（分明的主力）", -100f, 100f);
        Color("ColorAdjustments", "colorFilter", "颜色滤镜（保持白 = 不加色偏）");
        Float("ColorAdjustments", "hueShift", "色相旋转（非必要别动，0 最安全）", -180f, 180f);
        Float("ColorAdjustments", "saturation", "饱和度（负数会发灰）", -100f, 100f);

        EditorGUILayout.LabelField("— 阴影压暗（最直接） —", EditorStyles.miniBoldLabel);
        Vector4Field("ShadowsMidtonesHighlights", "shadows", "阴影", 0.4f, 1.3f);
        Vector4Field("ShadowsMidtonesHighlights", "highlights", "高光", 0.7f, 1.5f);

        EditorGUILayout.LabelField("— 色偏 —", EditorStyles.miniBoldLabel);
        Float("WhiteBalance", "temperature", "色温", -100f, 100f);
        Float("WhiteBalance", "tint", "色调（品红/绿，建议 0）", -100f, 100f);
        Color("SplitToning", "shadows", "阴影染色");
        Color("SplitToning", "highlights", "高光染色");
        Float("SplitToning", "balance", "染色平衡", -100f, 100f);

        EditorGUILayout.LabelField("— 镜头/氛围（过量都会糊） —", EditorStyles.miniBoldLabel);
        Float("Vignette", "intensity", "暗角强度（1.0 太重，0.3~0.45）", 0f, 1f);
        Float("Vignette", "smoothness", "暗角柔和", 0.01f, 1f);
        Float("Bloom", "threshold", "辉光阈值（提高=只有很亮的才发光）", 0f, 3f);
        Float("Bloom", "intensity", "辉光强度", 0f, 2f);
        Float("ChromaticAberration", "intensity", "色散", 0f, 1f);
        Float("FilmGrain", "intensity", "颗粒（0.88 会毁掉暗部，建议 ≤0.15）", 0f, 1f);

        EditorGUILayout.LabelField("— 色调映射 —", EditorStyles.miniBoldLabel);
        TonemappingModePopup();

        EditorGUILayout.Space(8);
        using (new EditorGUILayout.HorizontalScope())
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.85f, 0.6f);
            if (GUILayout.Button("↩ 恢复调参前的数值（还原文件）", GUILayout.Height(32))) Restore();
            GUI.backgroundColor = old;
        }
        if (!string.IsNullOrEmpty(_status))
            EditorGUILayout.HelpBox(_status, MessageType.Info);

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------ UI 小工具
    void Float(string comp, string field, string label, float min, float max)
    {
        var t = TypeOf(comp);
        if (t == null) return;

        if (!HasType(t)) { EditorGUILayout.LabelField("   (缺组件) " + comp, EditorStyles.miniLabel); return; }
        Profile.TryGet(t, out VolumeComponent vc);

        var p = GetParam(vc, field) as ClampedFloatParameter;
        var fp = p as FloatParameter ?? GetParam(vc, field) as FloatParameter;
        float cur = p != null ? p.value : (fp != null ? fp.value : 0f);
        bool ovr = p != null ? p.overrideState : (fp != null && fp.overrideState);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            float nv = EditorGUILayout.Slider(label, cur, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                BackupOnce();
                Undo.RecordObject(Profile, "后处理调参");
                if (p != null) { p.value = nv; p.overrideState = true; }
                else if (fp != null) { fp.value = nv; fp.overrideState = true; }
                EditorUtility.SetDirty(Profile);
            }
            if (!ovr && GUILayout.Button("启用", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                if (p != null) p.overrideState = true; else if (fp != null) fp.overrideState = true;
                EditorUtility.SetDirty(Profile);
            }
        }
    }

    bool HasType(Type t)
    {
        return Profile != null && Profile.components.Any(c => c != null && c.GetType() == t);
    }

    void Color(string comp, string field, string label)
    {
        var t = TypeOf(comp);
        if (t == null || !HasType(t)) return;
        Profile.TryGet(t, out VolumeComponent vc);
        var p = GetParam(vc, field) as ColorParameter;
        if (p == null) return;

        EditorGUI.BeginChangeCheck();
        var nv = EditorGUILayout.ColorField(label, p.value);
        if (EditorGUI.EndChangeCheck())
        {
            BackupOnce();
            Undo.RecordObject(Profile, "后处理调参");
            p.value = nv; p.overrideState = true;
            EditorUtility.SetDirty(Profile);
        }
    }

    void Vector4Field(string comp, string field, string label, float min, float max)
    {
        var t = TypeOf(comp);
        if (t == null || !HasType(t)) return;
        Profile.TryGet(t, out VolumeComponent vc);
        var p = GetParam(vc, field) as Vector4Parameter;
        if (p == null) return;

        var v = p.value;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
            EditorGUI.BeginChangeCheck();
            float r = EditorGUILayout.Slider(v.x, min, max);
            float g = EditorGUILayout.Slider(v.y, min, max);
            float b = EditorGUILayout.Slider(v.z, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                BackupOnce();
                Undo.RecordObject(Profile, "后处理调参");
                p.value = new Vector4(r, g, b, v.w);
                p.overrideState = true;
                EditorUtility.SetDirty(Profile);
            }
        }
    }

    void TonemappingModePopup()
    {
        if (!Profile.Has<Tonemapping>()) { EditorGUILayout.LabelField("   (缺 Tonemapping 组件)", EditorStyles.miniLabel); return; }
        Profile.TryGet<Tonemapping>(out var tm);
        var modes = new[] { "None", "Neutral", "ACES" };
        EditorGUI.BeginChangeCheck();
        int cur = tm.mode.value == TonemappingMode.Neutral ? 1 : tm.mode.value == TonemappingMode.ACES ? 2 : 0;
        int ni = EditorGUILayout.Popup("色调映射", cur, modes);
        if (EditorGUI.EndChangeCheck())
        {
            BackupOnce();
            Undo.RecordObject(Profile, "后处理调参");
            tm.mode.value = ni == 2 ? TonemappingMode.ACES : ni == 1 ? TonemappingMode.Neutral : TonemappingMode.None;
            tm.mode.overrideState = true;
            EditorUtility.SetDirty(Profile);
        }
    }

    static Type TypeOf(string name)
    {
        switch (name)
        {
            case "ColorAdjustments": return typeof(ColorAdjustments);
            case "WhiteBalance": return typeof(WhiteBalance);
            case "SplitToning": return typeof(SplitToning);
            case "Vignette": return typeof(Vignette);
            case "Bloom": return typeof(Bloom);
            case "FilmGrain": return typeof(FilmGrain);
            case "ChromaticAberration": return typeof(ChromaticAberration);
            default: return null;
        }
    }

    static VolumeParameter GetParam(VolumeComponent c, string field)
    {
        var f = c.GetType().GetField(field);
        return f != null ? f.GetValue(c) as VolumeParameter : null;
    }

    T Ensure<T>() where T : VolumeComponent
    {
        if (Profile.TryGet<T>(out var c)) { c.active = true; return c; }
        var added = Profile.Add<T>(true);
        added.active = true;
        return added;
    }

    // ------------------------------------------------------------------ 预设与动作
    enum Preset { Clean, Moody, Bright, Neutral }

    void ApplyPreset(Preset p)
    {
        BackupOnce();
        Undo.RecordObject(Profile, "后处理预设");

        // 所有预设都先把 ChannelMixer 去掉（它是色偏的来源）
        if (Profile.Has<ChannelMixer>()) Profile.Remove<ChannelMixer>();

        var ca = Ensure<ColorAdjustments>();
        var wb = Ensure<WhiteBalance>();
        var st = Ensure<SplitToning>();
        var vg = Ensure<Vignette>();
        var bl = Ensure<Bloom>();
        var fg = Ensure<FilmGrain>();
        var tm = Ensure<Tonemapping>();

        switch (p)
        {
            case Preset.Clean:
                Set(ca.postExposure, 0f); Set(ca.contrast, 25f); Set(ca.saturation, 12f);
                Set(ca.hueShift, 0f); Set(ca.colorFilter, UnityEngine.Color.white);
                Set(wb.temperature, 0f); Set(wb.tint, 0f);
                Set(st.shadows, new UnityEngine.Color(0.62f, 0.72f, 0.95f)); Set(st.highlights, new UnityEngine.Color(1f, 0.95f, 0.82f)); Set(st.balance, 0f);
                Set(vg.intensity, 0.35f); Set(vg.smoothness, 0.45f); Set(vg.color, new UnityEngine.Color(0.02f, 0.03f, 0.08f));
                Set(bl.threshold, 1.0f); Set(bl.intensity, 0.35f); Set(bl.tint, UnityEngine.Color.white);
                Set(fg.intensity, 0.12f); Set(fg.response, 0.8f);
                tm.mode.value = TonemappingMode.ACES; tm.mode.overrideState = true;
                ApplySmh(0.85f, 1.0f, 1.06f);
                _status = "已应用「干净对比」：曝光归零、去色偏、颗粒降到 0.12、暗角 0.35、阴影压暗到 0.85。";
                break;

            case Preset.Moody:
                Set(ca.postExposure, -0.15f); Set(ca.contrast, 40f); Set(ca.saturation, 20f);
                Set(ca.hueShift, 0f); Set(ca.colorFilter, new UnityEngine.Color(0.88f, 0.95f, 1f));
                Set(wb.temperature, -8f); Set(wb.tint, 0f);
                Set(st.shadows, new UnityEngine.Color(0.35f, 0.55f, 1f)); Set(st.highlights, new UnityEngine.Color(1f, 0.88f, 0.68f)); Set(st.balance, -10f);
                Set(vg.intensity, 0.5f); Set(vg.smoothness, 0.4f); Set(vg.color, new UnityEngine.Color(0.0f, 0.01f, 0.06f));
                Set(bl.threshold, 1.2f); Set(bl.intensity, 0.5f);
                Set(fg.intensity, 0.15f);
                tm.mode.value = TonemappingMode.ACES; tm.mode.overrideState = true;
                ApplySmh(0.55f, 0.95f, 1.12f);
                _status = "已应用「阴郁高对比」：曝光 -0.15、对比 40、阴影压到 0.55 —— 参考封面那种暗调。";
                break;

            case Preset.Bright:
                Set(ca.postExposure, 0.1f); Set(ca.contrast, 12f); Set(ca.saturation, 6f);
                Set(ca.hueShift, 0f); Set(ca.colorFilter, UnityEngine.Color.white);
                Set(wb.temperature, 4f); Set(wb.tint, 0f);
                Set(vg.intensity, 0.2f); Set(vg.smoothness, 0.5f);
                Set(bl.threshold, 1.1f); Set(bl.intensity, 0.3f);
                Set(fg.intensity, 0.08f);
                ApplySmh(1.0f, 1.02f, 1.04f);
                _status = "已应用「明亮通透」：适合白天的海岛关卡。";
                break;

            case Preset.Neutral:
                Set(ca.postExposure, 0f); Set(ca.hueShift, 0f); Set(ca.colorFilter, UnityEngine.Color.white);
                Set(ca.saturation, 0f); Set(ca.contrast, 0f);
                Set(wb.temperature, 0f); Set(wb.tint, 0f);
                Set(fg.intensity, 0f);
                Set(vg.intensity, 0f);
                Set(st.shadows, UnityEngine.Color.gray); Set(st.highlights, UnityEngine.Color.gray); Set(st.balance, 0f);
                _status = "已中性化：色偏/饱和度/对比/颗粒全部归零，方便从干净状态重新调。";
                break;
        }

        EditorUtility.SetDirty(Profile);
        AssetDatabase.SaveAssets();
        Repaint();
    }

    void ApplySmh(float shadowMul, float midMul, float highMul)
    {
        var smh = Ensure<ShadowsMidtonesHighlights>();
        var s = smh.shadows.value; s.x = s.y = shadowMul; s.z = Mathf.Min(1f, shadowMul + 0.1f);
        smh.shadows.value = s; smh.shadows.overrideState = true;
        var m = smh.midtones.value; m.x = m.y = m.z = midMul;
        smh.midtones.value = m; smh.midtones.overrideState = true;
        var h = smh.highlights.value; h.x = h.y = h.z = highMul;
        smh.highlights.value = h; smh.highlights.overrideState = true;
        EditorUtility.SetDirty(Profile);
    }

    void SetGrain(float v)
    {
        BackupOnce();
        var fg = Ensure<FilmGrain>();
        fg.intensity.value = v; fg.intensity.overrideState = true;
        EditorUtility.SetDirty(Profile);
        AssetDatabase.SaveAssets();
    }

    static void Set(ClampedFloatParameter p, float v) { p.value = v; p.overrideState = true; }
    static void Set(FloatParameter p, float v) { p.value = v; p.overrideState = true; }
    static void Set(MinFloatParameter p, float v) { p.value = v; p.overrideState = true; }
    static void Set(ColorParameter p, UnityEngine.Color v) { p.value = v; p.overrideState = true; }

    // ------------------------------------------------------------------ 备份 / 还原
    static string ProjectRoot { get { return Directory.GetParent(Application.dataPath).FullName; } }
    static string BackupRoot { get { return Path.Combine(ProjectRoot, BackupRootName); } }
    static string StampKey { get { return "RenderKit.PostFx.BackupStamp"; } }

    void BackupOnce()
    {
        string path = AssetDatabase.GetAssetPath(Profile);
        if (string.IsNullOrEmpty(path)) return;

        string stamp = EditorPrefs.GetString(StampKey, "");
        if (!string.IsNullOrEmpty(stamp) && File.Exists(Path.Combine(BackupRoot, stamp, path)))
            return;   // 本次会话已经备份过这个文件

        if (string.IsNullOrEmpty(stamp)) stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string dst = Path.Combine(BackupRoot, stamp, path);
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        File.Copy(Path.Combine(ProjectRoot, path), dst, true);
        if (File.Exists(Path.Combine(ProjectRoot, path) + ".meta"))
            File.Copy(Path.Combine(ProjectRoot, path) + ".meta", dst + ".meta", true);
        EditorPrefs.SetString(StampKey, stamp);
    }

    void Restore()
    {
        string stamp = EditorPrefs.GetString(StampKey, "");
        string dir = Path.Combine(BackupRoot, stamp);
        if (string.IsNullOrEmpty(stamp) || !Directory.Exists(dir))
        {
            var dirs = Directory.Exists(BackupRoot) ? Directory.GetDirectories(BackupRoot).OrderByDescending(d => d).ToList() : new List<string>();
            if (dirs.Count == 0) { _status = "没有找到备份（还没调过参）。"; return; }
            dir = dirs[0];
        }

        int n = 0;
        foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = file.Substring(dir.Length).TrimStart('\\', '/');
            string dst = Path.Combine(ProjectRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(file, dst, true);
            if (!rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                AssetDatabase.ImportAsset(rel.Replace('\\', '/'), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            n++;
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        _status = "已从备份还原 " + n + " 个文件（" + dir + "）";
    }
}
#endif
