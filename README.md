# Render Kit — Unity 光照体检 + 后处理调参

[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)
[![URP](https://img.shields.io/badge/URP-10%2B-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)

两个**编辑器工具**，专门解决一个很常见的美术问题：**场景"亮暗不分明"、画面发灰发平**。

不是靠拉 `Contrast` 滑块硬凑，而是先把**光照结构**做对（环境光 / 主光 / 光照探针 / 烘焙预算），
再用**后处理**去区分已有的明暗。两个工具的所有改动都支持 **Undo（Ctrl+Z）** 和**文件级一键回退**。

```
工具 ▸ Render Kit ▸ 光照体检与一键设置 (Lighting Doctor)
工具 ▸ Render Kit ▸ 后处理调参 (Post FX Tuner)
```

---

## 1. Lighting Doctor · 光照体检与一键设置

| 功能 | 说明 |
|---|---|
| **① 压暗环境光** | 一键把环境光从默认 1.0 压到 0.4~0.6，并按天空/地平线/地面三段给冷色梯度 → **暗部第一次真正出现** |
| **② 主光对比** | 强度 / 阴影强度（0.85）/ 略暖色 / 软阴影；可选把阳光俯角压低（斜射更立体） |
| **③ 光照探针** | 按参数一键生成网格状 `LightProbeGroup`（覆盖范围、高度、行列数可调），让动态物体有间接光 |
| **④ 烘焙预算诊断** | 估算整个场景在光图里占多少 **百万 texel**，判定「轻 / 中 / 重」，并列出**吃光图最多的 Top 8 物体** |
| **⚡ 一键瘦身** | 按尺寸自动调 `ScaleInLightmap`（>80m 的大平面直接退出 GI）→ **把十几小时的烘焙压到十几分钟** |
| **快速烘焙设置** | 自动创建 `LightingSettings` 并写入低分辨率 / 小图集 / 2 次反弹 / 关 AO / 低采样 |
| **UV2 体检** | 统计有多少网格缺少光照贴图 UV（UV2），提前告诉你哪些模型需要勾 `Generate Lightmap UVs` |
| **↩ 还原** | 按记录还原环境光、主光、静态标记，并删除本工具生成的探针组 |

**硬件自适应**：自动读取 `SystemInfo.graphicsMemorySize`（显存）与显卡名，给出对应的分辨率与图集上限建议
（≤4GB → 分辨率 1 / 图集 512；6~8GB → 2 / 1024；≥12GB → 3~4 / 1024）。
**全渲染管线通用**（Built-in / URP / HDRP 都可以用）。

## 2. Post FX Tuner · 后处理调参

针对"亮暗不分明"的**病因**做的调参面板（需要 **URP 10+** 的 Volume 框架）：

| 预设 | 效果 |
|---|---|
| **④ 中性化** | 色相 / 滤镜 / 色温 / 色调 / 饱和度 / 颗粒 / 暗角全部归零，并移除 `ChannelMixer` → 从干净状态重调 |
| **① 干净对比**（推荐起手） | 曝光归零、对比 25、饱和 +12、颗粒 0.12、暗角 0.35、阴影压暗到 0.85 |
| **② 阴郁高对比** | 曝光 −0.15、对比 40、阴影压到 0.55 → 电影感暗调 |
| **③ 明亮通透** | 白天户外关卡 |

还有：逐项实时滑条（曝光/对比/饱和/色相/色偏/阴影压暗/高光/暗角/辉光/颗粒/色调映射）、
快速动作（压暗阴影 / 加强对比 / 去掉颗粒）、**↩ 恢复调参前的数值**（文件级还原）。

---

## 安装

### 方式 A：UPM（推荐，可用 Git URL 一键安装）

Package Manager ▸ `+` ▸ **Add package from git URL**：

```
https://github.com/ljunein-oss/unity-ue-render-kit.git
```

或在 `Packages/manifest.json` 里加：

```json
"com.rtools.renderkit": "https://github.com/ljunein-oss/unity-ue-render-kit.git"
```

### 方式 B：手动

把整个文件夹拷到工程的 `Packages/` 目录下（或只把 `Editor/` 里的脚本拷到 `Assets/Editor/`）。

---

## 使用顺序（建议）

```
① 环境光压暗（秒级，立刻能看出对比变化）
        ↓
② 主光对比（阴影强度调高、必要时把阳光压更低）
        ↓
③ 生成光照探针（覆盖动态物体经过的区域）
        ↓
④ 标记静态 → ⚡ 一键瘦身 → 看光图估算掉到「轻」→ 开始烘焙
        ↓
回后处理调参：点「中性化」→ 点「干净对比」→ 按美术意见微调
```

---

## FAQ：烘焙要 13 个小时，正常吗？

**不正常。** 这个量级的场景正常是 **10 分钟 ~ 1 小时**。常见两个原因：

1. **光照贴图分辨率是"每米多少 texel"**
   如果海床是 500 × 600 米、分辨率 40 → 单块平面的光图就是 20000 × 24000 texel（天文数字）。
   → 用工具里的 **⚡ 一键瘦身**，或把分辨率降到 **1~4**。
2. **显存不够 → Progressive GPU 退回 CPU**
   6GB 显存还要跟编辑器抢内存，装不下就退回 CPU，再慢 10~50 倍。
   → 用低分辨率 + 小图集，烘焙时关掉 Game 视图预览。

> 提示：**大面积平整的地面/海床根本不需要烘焙** —— 它那么平，靠压暗的环境光就够了。
> 值得烘焙的是**码头、礁石、建筑**这类有体积的道具。

---

## 安全说明

- 每次修改前会把原值与物体路径记录到 `<项目根目录>/RenderKitBackup/<时间戳>/record.txt`
- Post FX Tuner 会把整个 `VolumeProfile` **文件级备份**，回退是逐字节还原
- 两个工具都**不会自动保存场景**，你确认后再 `Ctrl+S`
- 烘焙是异步的，可以随时取消

## 兼容性

| | 版本 |
|---|---|
| Unity | 2021.3 及以上（2022.3 实测） |
| Lighting Doctor | 全管线通用（Built-in / URP / HDRP） |
| Post FX Tuner | 需要 URP 10+（用到 Volume 框架与 URP 的 Override 组件）；未安装 URP 时该工具会自动跳过、不报错 |

## License

MIT — 随便用、随便改，保留版权声明即可。
