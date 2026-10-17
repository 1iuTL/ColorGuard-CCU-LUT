# Root cause — measurements

## Environment where this was diagnosed

* MECHREVO laptop (Intel Core Ultra 7 255HX + RTX 5060 Laptop), Windows 11 26200.x
* Control Center `CCU.WinUI 5.56.60.34` (AiStone)
* Windows **Auto color management** enabled
* Panel connected through the Intel iGPU

## Symptom

* `原色域 / Default` mode renders correctly.
* `sRGB` mode looks **darker than it should and shows colour noise / banding** in
  gradients and dark areas.
* Toggling "colour management" off/on in the Control Center used to fix it; in
  `5.56.60.34` it no longer does.

## Measurements

The display ramp was read with `GetDeviceGammaRamp` (768 `USHORT`, 3 x 256) and summed as
a fingerprint:

| State | LUT sum | Notes |
|---|---|---|
| Windows default (identity ramp) | 25,165,440 | 0,257,514,...,65535 per channel |
| After selecting **Default / 原色域** | 22,835,221 | `R[255]=61680 G[255]=61937 B[255]=65535` |
| After switching to **sRGB** | **22,835,221** | **byte-identical to the Default curve — nothing was written** |

Registry after the same switch:

```
HKLM\SOFTWARE\OEM\GamingCenter2\MySetting
    GCUUtil = [ASICC][...\ICCProfile\Default.icm]      -> ...sRGB.icm   (changes)
HKCU\...\Microsoft\Windows NT\CurrentVersion\ICM\ProfileAssociations\Display\...
    ICMProfile = sRGB.icm,Default.icm                  (changes, follows the CCU)
```

ICC tag inventory of the two profiles shipped by the OEM:

```
Default.icm : desc chad cprt wtpt lumi rXYZ gXYZ bXYZ rTRC gTRC bTRC vcgt   <- has vcgt
sRGB.icm    : desc chad cprt wtpt lumi rXYZ gXYZ bXYZ rTRC gTRC bTRC MHC2 CxF DDPS meta  <- NO vcgt
```

`vcgt` is the "video card gamma table" tag: it is what makes a component write the display
LUT. `MHC2` is a Microsoft colour-management tag consumed by the Windows compositor
(ACM), not a gamma table.

## Conclusion

1. In `5.56.60.34` the Control Center **records** the profile switch but does not perform
   the ramp write that `5.56.60.17` still did (the old "toggle" workaround relied on it).
2. Because `sRGB.icm` has no `vcgt`, nothing else writes a ramp for the sRGB mode either,
   so the LUT keeps the Default-mode curve.
3. With ACM enabled the OS then applies the sRGB transform **on top of** the stale default
   curve => double correction => darker picture + colour noise.

## Verification of the fix

* Writing an **identity** ramp while in sRGB mode makes the picture correct (no noise).
* Switching to Default and back to sRGB reproduces the problem immediately (the Default
  curve stays in the LUT because nothing rewrites it).
* Therefore: whenever the profile changes, write the ramp that belongs to the new mode.
  That is exactly what ColorGuard does.

## Why not parse the `vcgt` tag instead of capturing?

On the affected machine the `vcgt` table of `Default.icm` did **not** reproduce the ramp
that was actually applied by the Control Center (decoded curve did not match the measured
`22,835,221` fingerprint), so deriving the curve from the ICC file is unreliable.
Capturing the ramp that the vendor actually applies is the safer approach.

## Known behaviour that this tool cannot change

The Control Center writes the registry more than once per click (two notifications about
500 ms apart) and Windows ACM applies its transform asynchronously; during that window the
OS transform and the LUT briefly disagree, which shows up as a short flash
(colour noise / warm tint). Measured with the tool disabled: the flash still occurs.
