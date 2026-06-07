# Ladybug Screensaver

<div align="center">
  <img src="./preview 1.png" width="45%" alt="preview image one">
  <img src="./preview 2.png" width="45%" alt="preview image two">
  <br><br>
  <img src="./preview 3.png" width="45%" alt="preview image three">
</div>


A cute, ladybug-themed screensaver for Windows!

Features ladybugs randomly crawling around the screen and leaving a trail of dots behind them

Perfect for any ladybug enthusiasts

## How to Install

### Using the prebuilt release

In `dist\`, the `.scr` and the asset folder needed are both already there. Copy both directly into `C:\Windows\System32\`. Make sure both the `.scr` file and the `Assets\` folder are directly under `System32`, or it will not work. After copying, you can freely select it in your screensaver settings.

### Building from source

You will need the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8) and an IDE like [Rider](https://www.jetbrains.com/rider/) or Visual Studio (Rider was specifically used to create this).

Clone the repo, then run the following in a terminal inside the `LadybugScreensaver\` *sub*folder:

```bash dotnet publish LadybugScreensaver.csproj -c Release -r win-x64 --self-contained true```

The `.scr` and `Assets\` folder will automatically be placed in `dist\` upon build completion. Copy these into `System32`.

### Customization

Most settings can be found in the following files:

| Setting | File |
|---|---|
| Loop radius, bug speed, bug size | `LadybugScreensaver/Models/Ladybug.cs` |
| Trail dot size and lifetime | `LadybugScreensaver/Models/TrailDot.cs` |
| Background colors, stripe width, spawn interval | `LadybugScreensaver/ScreensaverForm.cs` |

## Credits
#### Ladybug PNG credit: 
[Ladybug SVG by Maskymius Designs](https://designbundles.net/maksymius/1586213-lady-bug-ladybug-cut-file-svg-png-eps-ai)

## Disclaimer!!!

This was built in its entirety by Claude Code.

I have no knowledge of C#, let alone the deep the understanding likely needed to correctly comprehend the code in this project. I am aware AI can make mistakes and suboptimal code, so if you feel inclined to open an issue for potential code optimization, I am more than happy and receptive to receive your feedback! 🐞

