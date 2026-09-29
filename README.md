# Render Kit

[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)
[![URP](https://img.shields.io/badge/URP-10%2B-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)

[English](README_EN.md) | 中文

一个Unity引擎快捷改灯光和后处理的插件。


里头有俩窗口一个管光照，一个管后处理，主要拿来处理"场景发灰、亮暗不分"这类问题。（主要是UE转到unity后一直觉得发灰，图形学的插件以后再写了，现在就只看看灯光和后处理能不能解决——当然是不能，只能说稍微改善改善） 

```
工具 ▸ Render Kit ▸ 光照体检与一键设置 (Lighting Doctor)
工具 ▸ Render Kit ▸ 后处理调参 (Post FX Tuner)
```
<img width="455" height="306" alt="Find_big" src="https://github.com/user-attachments/assets/2d391d37-9eb9-47e0-8fa2-7530d81637a7" />

其实还有一个原因就是这边的要求面积很大，但是只有1660跌丝袜所以有了这个可以一边调光一边优化烘焙的工具

## 安装

Package Manager ▸ `+` ▸ Add package from git URL：

```
https://github.com/ljunein-oss/unity-ue-render-kit.git
```

要固定版本就带上 tag：

```
https://github.com/ljunein-oss/unity-ue-render-kit.git#v1.0.0
```

也可以直接把文件夹拷进工程的 `Packages` 目录。

## Lighting Doctor

菜单：工具 ▸ Render Kit ▸ 光照体检与一键设置

1. 环境光：把强度从默认的 1.0 压到 0.4~0.6，天空/地平线/地面三段渐变各给一个颜色。
2. 主光：强度、阴影强度（默认 0.85）、颜色，也可以顺手把阳光角度压低一点。
3. 光照探针：按参数生成网格状 LightProbeGroup，覆盖范围、高度、行列数都能调。动态物件用得到。
4. 烘焙预算诊断：估一下整个场景在光图里占多少 texel，给个轻/中/重，并列出占用量最大的几个物件。
5. 一键瘦身：超过 80 米的平面退出 GI，中等尺寸按大小把 ScaleInLightmap 调小。大场景烘焙前可以先点一下。
6. 快速烘焙设置：新建 LightingSettings，写入低分辨率、小图集、2 次反弹、关 AO、低采样。分辨率和图集上限按显存来。
7. UV2 体检：数一下有多少网格缺光照贴图 UV，省得烘完才发现。
8. 还原：按记录把环境光、主光、静态标记改回去，并删掉工具生成的探针组。

窗口顶部会显示显卡和显存，并给出建议值：

| 显存 | 光照贴图分辨率 | 图集上限 |
|---|---|---|
| ≤ 4 GB | 1 | 512 |
| 6 ~ 8 GB | 2 | 1024 |
| ≥ 12 GB | 3 ~ 4 | 1024 |

没有 SRP 依赖，Built-in、URP、HDRP 都能跑，不过我这边只在 2022.3 + URP 14 上实际用过。

## Post FX Tuner

菜单：工具 ▸ Render Kit ▸ 后处理调参

需要 URP 10 以上（用到 Volume 框架和 URP 的 Override 组件）。没装 URP 的话这个工具会被跳过，不会连累整个工程编译不过。

预设：

1. 中性化：色相、颜色滤镜、色温、色调、饱和度、颗粒、暗角全部归零，并移除 ChannelMixer。之前乱调过的话，从这一步开始比较省事。
2. 干净对比：曝光 0，对比 25，饱和 +12，颗粒 0.12，暗角 0.35，阴影压到 0.85。
3. 阴郁高对比：曝光 -0.15，对比 40，阴影压到 0.55。
4. 明亮通透：白天户外那种。

另外有逐项滑条（曝光、对比、饱和、色相、色偏、阴影压暗、高光、暗角、辉光、颗粒、色调映射），还有压暗阴影、加强对比、去掉颗粒三个快捷按钮。回退按钮在窗口底部。

## 说明

- 改动前会把原值记到 `<项目根目录>/RenderKitBackup/<时间戳>/record.txt`，回退是按这个记录还原的。重要改动前建议自己也存一份场景，别全指望它。
- Post FX Tuner 会把整个 VolumeProfile 文件备份一份，回退就是把这个文件覆盖回去。
- 两个窗口都不会帮你保存场景，确认没问题再 Ctrl+S。
- 烘焙之前先看一眼 UV2 体检的结果，缺 UV 的网格烘出来容易有脏污。

## 兼容性

| | 版本 |
|---|---|
| Unity | 2021.3 及以上（我这边只测过 2022.3） |
| Lighting Doctor | Built-in / URP / HDRP |
| Post FX Tuner | URP 10+ |

## License

MIT
