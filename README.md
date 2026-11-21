# Saddlebag Manager

Move items in bulk between your main inventory and Chocobo Saddlebag in FINAL FANTASY XIV. Requires Dalamud API 15.

## Install

In `/xlsettings`, add the [Solterax plugin feed](https://raw.githubusercontent.com/Solterax/DalamudPlugins/main/pluginmaster.json) under **Experimental → Custom Plugin Repositories**. Enable and save it, then install **Saddlebag Manager** from `/xlplugins`.

## Use

1. Open the Chocobo Saddlebag with `/saddlebag`.
2. Hold **Ctrl** and click **Deposit All** or **Withdraw All** in the saddlebag footer to start the transfer.

Hover over either button for the keyboard shortcut and transfer status. Use `/saddlebagmanager` to enable **Include premium saddlebag** or access **Stop transfer**.

Bulk transfers skip submersible hulls, sterns, bows, and bridges in both directions.

The plugin uses compatible stacks and empty slots in the destination. Items that cannot fit or that the game refuses to move stay in their original slots. **Stop transfer** finishes checking the current move before stopping. Closing the saddlebag also stops the transfer.

## Build

Build with the .NET 10 SDK and a local Dalamud API 15 installation.

```powershell
dotnet build -c Release
```

Set `DALAMUD_HOME` to your Dalamud API 15 assembly directory if the SDK cannot locate it. The build produces a `latest.zip` release package under `bin/Release/SaddlebagManager`.
