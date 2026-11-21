using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace SaddlebagManager;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/saddlebagmanager";
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly Transfer transfer;
    private readonly SaddlebagFooter footer;
    private bool windowOpen;
    private bool includePremium;
    private bool materialsOnly;
    private bool? confirmDeposit;

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IFramework framework,
        IGameGui gameGui, IClientState clientState, IDataManager data)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        transfer = new Transfer(gameGui, clientState, data);
        footer = new SaddlebagFooter(transfer);

        commands.AddHandler(Command, new CommandInfo((_, _) => OpenWindow())
        {
            HelpMessage = "Open Saddlebag Manager.",
        });
        framework.Update += Update;
        pluginInterface.UiBuilder.Draw += Draw;
        pluginInterface.UiBuilder.OpenMainUi += OpenWindow;
    }

    private void OpenWindow() => windowOpen = true;

    private void OpenConfirmation(bool deposit)
    {
        confirmDeposit = deposit;
        OpenWindow();
    }

    private void Update(IFramework _) => transfer.Update();

    private void Draw()
    {
        footer.Draw(includePremium, ref materialsOnly);
        if (!windowOpen)
            return;

            ImGui.SetNextWindowSize(new Vector2(420, 195), ImGuiCond.FirstUseEver);
        var visible = ImGui.Begin("Saddlebag Manager", ref windowOpen, ImGuiWindowFlags.NoCollapse);
        try
        {
            if (!windowOpen)
                confirmDeposit = null;
            if (!visible)
                return;

            var ready = transfer.SaddlebagReady;
            if (!ready)
            {
                ImGui.TextWrapped("Open your Chocobo Saddlebag to start a transfer.");
                confirmDeposit = null;
            }

            if (transfer.IsRunning)
            {
                if (ImGui.Button("Stop transfer"))
                    transfer.RequestStop();
            }
            else
            {
                ImGui.BeginDisabled(!ready);
                ImGui.Checkbox("Include premium saddlebag", ref includePremium);
                ImGui.Checkbox("Materials only", ref materialsOnly);
                if (confirmDeposit is bool deposit)
                {
                    ImGui.TextUnformatted(deposit
                        ? "Move items from inventory to the saddlebag?"
                        : "Move items from the saddlebag to inventory?");
                    if (ImGui.Button("Start transfer"))
                    {
                        transfer.Start(deposit, includePremium, materialsOnly);
                        confirmDeposit = null;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel"))
                        confirmDeposit = null;
                }
                else
                {
                    if (ImGui.Button("Deposit All"))
                        OpenConfirmation(true);
                    ImGui.SameLine();
                    if (ImGui.Button("Withdraw All"))
                        OpenConfirmation(false);
                }
                ImGui.EndDisabled();
            }

            if (transfer.Status.Length > 0)
            {
                ImGui.Separator();
                ImGui.TextWrapped(transfer.Status);
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    public void Dispose()
    {
        pluginInterface.UiBuilder.Draw -= Draw;
        pluginInterface.UiBuilder.OpenMainUi -= OpenWindow;
        framework.Update -= Update;
        commands.RemoveHandler(Command);
    }
}
