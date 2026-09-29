# Render Kit

[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)
[![URP](https://img.shields.io/badge/URP-10%2B-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)

[English](README_EN.md) | 中文

两个 Unity 编辑器窗口，用来处理"场景亮暗不分明、画面发灰"这类问题。一个管光照，一个管后处理，都支持 Ctrl+Z 和文件级回退。

```
工具 ▸ Render Kit ▸ 光照体检与一键设置 (Lighting Doctor)
工具 ▸ Render Kit ▸ 后处理调参 (Post FX Tuner)
```

## 安装

Package Manager ▸ `+` ▸ Add package from git URL：

```
https://github.com/ljunein-oss/unity-ue-render-kit.git
```

需要固定版本时加 tag：

```
https://github.com/ljunein-oss/unity-ue-render-kit.git#v1.0.0
```

也可以直接把文件夹拷进工程的 `Packages/` 目录。

## Lighting Doctor

菜单：工具 ▸ Render Kit ▸ 光照体检与一键设置

1. 环境光：把强度从默认 1.0 压到 0.4~0.6，天空/地平线/地面三段渐变各给一个颜色，暗部才会出来。
2. 主光：强度、阴影强度（默认给到 0.85）、颜色，可选把阳光角度压低一点，斜射更立体。
3. 光照探针：按参数生成网格状 LightProbeGroup，填覆盖范围、高度、行列数。动态物件用得到。
4. 烘焙预算诊断：估算整个场景在光图里占多少 texel，给出轻/中/重的判定，并列出占用量最大的几个物件。
5. 一键瘦身：超过 80 米的平面直接退出 GI，中等尺寸按大小把 ScaleInLightmap 调小。大场景烘焙前建议先点这个。
6. 快速烘焙设置：新建 LightingSettings 并写入低分辨率、小图集、2 次反弹、关闭 AO、低采样。分辨率和图集上限会按显存自动选。
7. UV2 体检：统计有多少网格缺光照贴图 UV，提前知道哪些模型要勾 Generate Lightmap UVs。
8. 还原：按记录把环境光、主光、静态标记改回去，并删掉工具生成的探针组。

窗口顶部会显示显卡和显存，并给出对应的分辨率建议：

| 显存 | 光照贴图分辨率 | 图集上限 |
|---|---|---|
| ≤ 4 GB | 1 | 512 |
| 6 ~ 8 GB | 2 | 1024 |
| ≥ 12 GB | 3 ~ 4 | 1024 |

这个窗口不依赖任何 SRP 包，Built-in、URP、HDRP 都能用。

## Post FX Tuner

菜单：工具 ▸ Render Kit ▸ 后处理调参

需要 URP 10 以上（用到 Volume 框架和 URP 的 Override 组件）。没装 URP 时这个工具会自动跳过，不会报编译错误。

预设：

1. 中性化：色相、颜色滤镜、色温、色调、饱和度、颗粒、暗角全部归零，并移除 ChannelMixer。想从干净状态重调就先点这个。
2. 干净对比：曝光归零，对比 25，饱和 +12，颗粒 0.12，暗角 0.35，阴影压暗到 0.85。
3. 阴郁高对比：曝光 -0.15，对比 40，阴影压到 0.55。
4. 明亮通透：白天户外。

另外有逐项滑条（曝光、对比、饱和、色相、色偏、阴影压暗、高光、暗角、辉光、颗粒、色调映射）和三个快捷按钮（压暗阴影、加强对比、去掉颗粒）。回退按钮在窗口底部。

## 使用顺序

1. 先压环境光。这一步是秒级的，做完就能看出差别。
2. 调主光。阴影强度拉高，必要时把阳光角度压低。
3. 生成光照探针，覆盖动态物件会经过的区域。
4. 标记静态，点一键瘦身，看光图估算掉到什么量级，然后开始烘焙。烘焙是异步的，可以随时取消。
5. 回到后处理调参，先点中性化，再点干净对比，然后按美术的意见微调。

## 说明

- 每次改动前会把原值和物体路径写到 `<项目根目录>/RenderKitBackup/<时间戳>/record.txt`。
- Post FX Tuner 会把整个 VolumeProfile 做文件级备份，回退是逐字节还原。
- 两个窗口都不会自动保存场景，确认后再 Ctrl+S。
- 烘焙前建议先看一眼 UV2 体检的结果。

## 兼容性

| | 版本 |
|---|---|
| Unity | 2021.3 及以上（2022.3 实测） |
| Lighting Doctor | Built-in / URP / HDRP |
| Post FX Tuner | URP 10+ |

## License

MIT
