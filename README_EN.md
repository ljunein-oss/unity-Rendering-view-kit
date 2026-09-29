# Render Kit

[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)
[![URP](https://img.shields.io/badge/URP-10%2B-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)

English | [中文](README.md)

Two Unity editor windows, one for lighting and one for post processing, mainly for when a scene looks flat and washed out.

```
Tools ▸ Render Kit ▸ 光照体检与一键设置 (Lighting Doctor)
Tools ▸ Render Kit ▸ 后处理调参 (Post FX Tuner)
```

## Install

Package Manager ▸ `+` ▸ Add package from git URL:

```
https://github.com/ljunein-oss/unity-ue-render-kit.git
```

Pin a version with a tag:

```
https://github.com/ljunein-oss/unity-ue-render-kit.git#v1.0.0
```

Or just copy the folder into your project's `Packages` directory.

## Lighting Doctor

Menu: Tools ▸ Render Kit ▸ 光照体检与一键设置

1. Ambient: drops the intensity from the default 1.0 down to 0.4 ~ 0.6 and sets sky / equator / ground colors.
2. Sun: intensity, shadow strength (0.85 by default) and color. You can also lower the sun angle a bit.
3. Light probes: generates a grid of LightProbeGroup positions. Coverage, height and grid size are adjustable. Only really needed for moving objects.
4. Bake budget: estimates how many texels the scene takes up in the lightmap, rates it light / medium / heavy, and lists the biggest consumers.
5. Slim down: surfaces wider than 80 m are dropped from GI, medium sized ones get a lower ScaleInLightmap. Worth a click before baking a large scene.
6. Fast bake settings: creates a LightingSettings asset with low resolution, small atlas, 2 bounces, AO off and low sample counts. Resolution and atlas size follow your VRAM.
7. UV2 check: counts meshes without lightmap UVs so you find out before the bake, not after.
8. Restore: puts ambient, sun and static flags back from the record file and deletes the probe group it created.

The window shows your GPU and VRAM and suggests values:

| VRAM | Lightmap resolution | Atlas size |
|---|---|---|
| ≤ 4 GB | 1 | 512 |
| 6 ~ 8 GB | 2 | 1024 |
| ≥ 12 GB | 3 ~ 4 | 1024 |

No SRP dependency, so Built-in, URP and HDRP all work. I have only actually used it on 2022.3 with URP 14 though.

## Post FX Tuner

Menu: Tools ▸ Render Kit ▸ 后处理调参

Needs URP 10 or newer (it uses the Volume framework and URP's override components). Without URP the tool is skipped, so it should not break your build.

Presets:

1. Neutralize: zeroes hue shift, color filter, white balance, saturation, grain and vignette, and removes ChannelMixer. Handy if someone has been messing with the grading.
2. Clean contrast: exposure 0, contrast 25, saturation +12, grain 0.12, vignette 0.35, shadows pushed down to 0.85.
3. Moody contrast: exposure -0.15, contrast 40, shadows at 0.55.
4. Bright: for daytime outdoor levels.

There are per-parameter sliders as well (exposure, contrast, saturation, hue shift, color filter, shadows, highlights, vignette, bloom, grain, tonemapping) plus three quick actions. The rollback button is at the bottom of the window.

## Notes

- Before changing anything it writes the previous values to `<project>/RenderKitBackup/<timestamp>/record.txt`, and the rollback reads that file. For anything important, save your own copy of the scene too, just in case.
- Post FX Tuner backs up the whole VolumeProfile file, and rolling back copies that file over the current one.
- Neither window saves the scene for you. Press Ctrl+S when you are happy.
- Check the UV2 result before baking. Meshes without lightmap UVs tend to come out blotchy.

## Compatibility

| | Version |
|---|---|
| Unity | 2021.3+ (only tested on 2022.3 here) |
| Lighting Doctor | Built-in / URP / HDRP |
| Post FX Tuner | URP 10+ |

## License

MIT
