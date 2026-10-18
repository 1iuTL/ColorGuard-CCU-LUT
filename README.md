# ColorGuard — fix the MECHREVO / AiStone Control Center colour-mode LUT bug

> **中文用户请先看 [`使用说明.txt`](使用说明.txt)** —— 傻瓜式逐步操作说明（解压 → 解除锁定 → 捕获曲线 → 开启守护 → 常见问题）。


A ~15 KB WinForms utility that keeps the display gamma ramp (LUT) in sync with the
colour mode of MECHREVO's **Control Center** (AiStone `GamingCenter2`), because the
Control Center itself stopped doing it in version **5.56.60.34**.

## The problem

With Windows 11 **Auto color management (ACM)** enabled, switching the colour mode in
Control Center (`原色域 / Default` <-> `sRGB`) updates the ICC profile association but
**does not rewrite the display gamma ramp (LUT)**:

| Mode | ICC profile | Contains a `vcgt` (gamma) tag? | Display LUT written? | Result |
|---|---|---|---|---|
| Default / 原色域 | `Default.icm` | **yes** (102-entry table) | yes, by the CCU / Windows | looks correct |
| sRGB | `sRGB.icm` | **no** — only `MHC2` | **no** | stale Default curve + ACM transform = **double correction → dark, banded/"noisy" colours** |

Measured on a test unit (RTX 5060 Laptop, internal panel):

```
LUT sum in Default mode : 22,835,221
LUT sum after switching to sRGB : 22,835,221   <-- identical, nothing was written
Expected for sRGB (identity ramp) : 25,165,440
```

The old workaround — toggling "colour management" off/on in the Control Center — used
to force a ramp rewrite. In `5.56.60.34` that code path is gone, so the workaround no
longer works.

## What this tool does

* Watches `HKLM\SOFTWARE\OEM\GamingCenter2` with `RegNotifyChangeKeyValue`
  (event driven — 0 % CPU idle).
* When the Control Center profile changes, it writes the correct ramp:
  * **sRGB** → identity ramp (let ACM do the gamut transform),
  * **Default / 原色域** → the curve captured on *this* machine.
* Then re-asserts it a few times over 0.4 s to converge with the OS/ACM handover.
* Logs every action with millisecond timestamps to `ColorGuard.log`.

Measured reaction time: **3–8 ms** (registry notification → ramp written).

It **never autostarts** and has no background service: you launch it and press
**开启守护 / Enable guard**. Closing the window stops it.

## Requirements

* Windows 10 / 11, .NET Framework 4.x (present on virtually every Windows install)
* MECHREVO (or any other **AiStone-based**) Control Center, i.e. the registry key
  `HKLM\SOFTWARE\OEM\GamingCenter2` must exist
* Windows "Auto color management" enabled (required for the sRGB side to make sense)

On other brands the registry key is missing — the tool then simply does nothing.

## Build

```bat
build.cmd
```
or
```bat
%windir%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /optimize+ ^
  /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
  /out:dist\ColorGuard.exe src\ColorGuard.cs
```
No SDK, no NuGet, no Visual Studio needed.

## First run — capture your display's curves

The published build deliberately contains **no machine-specific curve**; both modes
default to identity. Calibrate once per machine:

1. Start `ColorGuard.exe`.
2. Control Center → **原色域 / Default**; make sure the picture looks **correct** →
   press **捕获当前曲线 (capture)** → writes `profile-default.ramp` next to the exe.
3. Control Center → **sRGB**; make sure the picture looks **correct** → press
   **捕获当前曲线** again → writes `profile-srgb.ramp`.
4. Press **开启守护 / Enable guard**.

> Capture only when the picture in that mode is already correct — you are recording the
> ramp that is *currently active*. If a mode is broken at that moment you would store
> the broken curve.

The profiles are plain text (768 numbers) and can be copied, backed up or replaced
freely. If `profile-srgb.ramp` is absent, identity is used (correct when ACM is on).

## Caveats

* Switching modes still shows a **0.1–0.3 s transition flash** (sometimes a colour-noise
  or warm tint). That comes from the Control Center + Windows ACM applying their changes
  asynchronously — it happens with this tool disabled as well. The tool only guarantees
  that the **final** state is correct. To avoid the flash entirely, stay in one mode.
* If a future Control Center version starts writing the LUT again, disable this tool.
* Use at your own risk; it only calls `GetDeviceGammaRamp` / `SetDeviceGammaRamp` and
  reads one registry key.

## Files

```
src/ColorGuard.cs        single-file C# source (WinForms)
build.cmd                one-command build with csc
docs/root-cause.md       measurements and reasoning behind the fix
profile-default.ramp     (generated) captured Default-mode ramp
profile-srgb.ramp        (generated) captured sRGB ramp
ColorGuard.log           (generated) action log
```

## 中文说明（简）

**问题**：机械革命控制中心 5.56.60.34 在切换色域（原色域 ⇄ sRGB）时**只改 ICC 关联、不重写显示器伽马曲线**。
`Default.icm` 自带 `vcgt`（所以原色域正常）；`sRGB.icm` 没有 `vcgt`、只有 `MHC2`，它的校色本应由
**Windows ACM** 在合成阶段完成 —— 结果 OS 在做 sRGB 变换、显示器下却还压着原色域的旧曲线，
两者叠加就出现**偏暗 + 彩噪**。旧版可用的"色彩管理关→开"招数在新版失效。

**本工具**：事件驱动监听 `HKLM\SOFTWARE\OEM\GamingCenter2`，档位一变就写正确曲线
（sRGB → 恒等；原色域 → 本机捕获的曲线），响应 3–8 ms；**不自启**、手动开关、关窗即停。

**用法**：打开 exe → 在"原色域"确认画面正常后点【捕获当前曲线】→ 切"sRGB"确认正常后再点一次 →
点【开启守护】。发布的版本不含任何机器专属曲线，必须自己捕获一次。

**限制**：只适用于 AiStone 系控制中心（机械革命等）；切换瞬间 0.1–0.3 s 的过渡闪烁由控制中心与
Windows ACM 的异步切换造成，本工具无法消除（关掉本工具同样存在），它只保证**最终状态正确**。

## License

MIT — see `LICENSE`.

---

## 更新记录 v1.1.0（2026-10-18）— 防呆加固

**背景**：v1.0 存在一个实际踩到的坑 —— 在"画面还不正常"时点了【捕获当前曲线】，
会把错误的曲线存成该档位的"正确曲线"，守护据此判定"已正确"而不再纠正，画面持续异常。

**v1.1 新增三道防线**：

1. **捕获时拒绝张冠李戴**：当前档位是 sRGB 但屏幕仍在使用原色域曲线（或反之）→ 直接拒绝捕获并提示等待画面刷新。
2. **sRGB 档捕获非恒等曲线要二次确认**：正常 sRGB 档应由系统按 ICC 校正、屏幕 LUT 保持恒等；偏离时弹窗要求确认。
3. **启动自检自动修复**：若 profile-srgb.ramp 与 profile-default.ramp 完全重合（误捕获的典型特征）→ 自动把 sRGB 恢复为恒等曲线，旧文件备份，日志留痕。

另：每次捕获前自动备份 .bak-时间，日志记录前后曲线总和，误操作可回溯。

> 已在真实机器上实测：模拟"把 default 复制成 srgb"的误捕获，程序启动后自动恢复恒等并写入 self-check: ... restored identity。