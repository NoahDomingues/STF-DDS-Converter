# Building STF DDS Converter

## What the NU1101 errors mean

Errors like:

`Unable to find package Microsoft.NETCore.App.Ref … in source(s): Microsoft Visual Studio Offline Packages`

usually mean **two things**:

1. **NuGet.org is not enabled** (VS is only looking at the offline cache).
2. **The .NET SDK / targeting pack is not installed** (the `.Ref` packages ship with the SDK).

The `MC1000` / `mscorlib` message is a follow-on error from the failed restore.

## Fix in Visual Studio (recommended)

### A. Turn on NuGet.org

1. **Tools → NuGet Package Manager → Package Manager Settings**
2. **Package Sources**
3. Ensure **nuget.org** (`https://api.nuget.org/v3/index.json`) is **checked**
4. Click **OK**, then **Build → Rebuild Solution**

This repo includes a root `nuget.config` that points restore at nuget.org.

### B. Install the .NET desktop SDK

1. **Visual Studio Installer** → **Modify**
2. Workload: **.NET desktop development**
3. Individual component: **.NET 6 SDK** (this project targets `net6.0-windows`)
4. Apply, restart VS, **Rebuild**

Standalone: https://dotnet.microsoft.com/download/dotnet/6.0

### C. Build from terminal

```bat
cd "C:\Games\Operation Flashpoint Dragon Rising - MODDED\tools\STF-DDS-Converter"
dotnet restore "STF DDS Converter.sln"
dotnet build "STF DDS Converter.sln" -c Release
```

Output: `STF DDS Converter\bin\Release\net6.0-windows\STF DDS Converter.exe`
