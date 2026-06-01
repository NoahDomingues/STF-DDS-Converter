# STF DDS Converter (OFDR fork)

Converts Operation Flashpoint: Dragon Rising **TXPC** `.stf` textures to standard `.dds` for editing, and back.

**This copy in `tools/STF-DDS-Converter` fixes the original tool’s 2048-byte header bug** that caused landscape textures to break at distance (bad mip chain).

## What was wrong (original)

The old tool treated byte `0x0C` (value 2048) as “header size” and sliced texture data from offset `0x800`. Real OFDR files use:

- **70-byte** TXPC header (`header_size` at `+0x04`)
- **Mip offset table** at `+0x10`
- **Mip blobs** starting at the first table entry (e.g. `grass.stf` → `0x200800`, `dirt.stf` → `0x80800`)

Distance rendering uses lower mips; corrupt slices → black or striped textures far away.

## Correct workflow

1. **STF → DDS** — produces three files beside the `.stf`:
   - `name.dds` — standard 128-byte DDS header + DXT mip chain (for GIMP / tex tools)
   - `name.header` — everything **before** the mip blobs (TXPC + tables; must keep for import)
   - `name.stfmips.json` — mip segment sizes and OFDR per-mip prefixes

2. Edit `name.dds` with a tool that **keeps all mip levels** when saving (GIMP often drops mips — see below).

3. **DDS → STF** — needs `name.dds`, `name.header`, and `name.stfmips.json` in the same folder.

4. Copy the `.stf` into `data_win` (backup originals first).

### Landscape textures (this mod)

`\_UNPACKED\graphics\texture\terr\` — e.g. `grass.stf`, `dirt.stf`, `rock.stf`, `mulch.stf`, etc.

## Editing tips

| Tool | Notes |
|------|--------|
| **GIMP** | May export only mip 0 → distant mips break. Prefer NVIDIA Texture Tools, Compressonator, or `texconv` with mip generation. |
| **Resolution** | Same size as exported DDS unless you rebuild TXPC offsets manually. |
| **Header width** | UI may show engine “max” width (e.g. 1024) while DDS export uses **actual stored mip0** size (e.g. 512 for `dirt.stf`). |

## Build with Visual Studio

Project: **.NET 6** WPF (`net6.0-windows`).

**Build fails with NU1101?** See **[BUILD.md](BUILD.md)** (NuGet.org + SDK install).

### Workloads (Visual Studio 2022)

1. **.NET desktop development**
2. Individual component: **.NET 6 SDK**
3. Enable **nuget.org** under **Tools → NuGet Package Manager → Package Sources**

### Build

1. Open `STF DDS Converter.sln` → **Release** → **Rebuild**
2. Or double-click `Build STF DDS Converter.bat`
3. EXE: `STF DDS Converter\bin\Release\net6.0-windows\STF DDS Converter.exe`

## Files

| File | Purpose |
|------|---------|
| `TxpcTexture.cs` | TXPC parse, mip strip/rebuild, DDS wrap |
| `MainWindow.xaml.cs` | UI + convert commands |

Upstream: [NoahDomingues/STF-DDS-Converter](https://github.com/NoahDomingues/STF-DDS-Converter)
