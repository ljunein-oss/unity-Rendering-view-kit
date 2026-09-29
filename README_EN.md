# Render Kit

[![Unity](https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE.md)
[![URP](https://img.shields.io/badge/URP-10%2B-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@latest)

English | [中文](README.md)

Two Unity editor windows for the usual "my scene looks flat and washed out" problem. One deals with lighting, the other with post processing. Both support Ctrl+Z and file level rollback.

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

You can also just copy the folder into your project's `Packages` directory.

## Lighting Doctor

Menu: Tools ▸ Render Kit ▸ 光照体检与一键设置

1. Ambient: drops the intensity from the default 1.0 down to 0.4 ~ 0.6 and sets sky / equator / ground colors. This is what actually creates dark areas.
2. Sun: intensity, shadow strength (0.85 by default) and color. Optionally lowers the sun angle for more raking light.
3. Light probes: generates a grid of LightProbeGroup positions with configurable coverage, height and grid size. Useful for moving objects.
4. Bake budget: estimates how many texels the scene occupies in the lightmap, rates it light / medium / heavy, and lists the biggest consumers.
5. Slim down: any surface wider than 80 m is dropped from GI, medium sized ones get a lower ScaleInLightmap. Worth running before a big bake.
6. Fast bake settings: creates a LightingSettings asset with low resolution, small atlas, 2 bounces, AO off and low sample counts. Resolution and atlas size are picked from your VRAM.
7. UV2 check: counts meshes without lightmap UVs so you know which models need Generate Lightmap UVs.
8. Restore: puts ambient, sun and static flags back from the record file, and deletes the probe group it created.

The window shows your GPU and VRAM and suggests settings:

| VRAM | Lightmap resolution | Atlas size |
|---|---|---|
| ≤ 4 GB | 1 | 512 |
| 6 ~ 8 GB | 2 | 1024 |
| ≥ 12 GB | 3 ~ 4 | 1024 |

No SRP dependency, so it works on Built-in, URP and HDRP.

## Post FX Tuner

Menu: Tools ▸ Render Kit ▸ 后处理调参

Requires URP 10 or newer (it uses the Volume framework and URP's override components). Without URP the tool is skipped entirely instead of throwing compile errors.

Presets:

1. Neutralize: zeroes hue shift, color filter, white balance, saturation, film grain and vignette, and removes ChannelMixer. Good starting point if your grading has drifted.
2. Clean contrast: exposure 0, contrast 25, saturation +12, grain 0.12, vignette 0.35, shadows pushed down to 0.85.
3. Moody contrast: exposure -0.15, contrast 40, shadows at 0.55.
4. Bright: for daytime outdoor levels.

There are also per-parameter sliders (exposure, contrast, saturation, hue shift, color filter, shadows, highlights, vignette, bloom, grain, tonemapping) and three quick actions. The rollback button sits at the bottom of the window.

## Suggested order

1. Pull the ambient down first. It takes a second and you can see the difference right away.
2. Tune the sun. Raise shadow strength, lower the angle if you need more shape.
3. Generate light probes over the area where moving objects travel.
4. Mark static, run slim down, check the texel estimate, then start the bake. Baking is async and can be cancelled.
5. Go back to the post FX window, hit Neutralize, then Clean contrast, then adjust to taste.

## Notes

- Every change records the previous value and object path to `<project>/RenderKitBackup/<timestamp>/record.txt`.
- Post FX Tuner backs up the whole VolumeProfile file, so rollback is byte for byte.
- Neither window saves the scene for you. Press Ctrl+S when you are happy.
- Check the UV2 result before starting a bake.

## Compatibility

| | Version |
|---|---|
| Unity | 2021.3+ (tested on 2022.3) |
| Lighting Doctor | Built-in / URP / HDRP |
| Post FX Tuner | URP 10+ |

## License

MIT
