using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Bounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;

namespace SaddlebagManager;

internal sealed unsafe class SaddlebagFooter(Transfer transfer, Action<bool> openConfirmation)
{
    private const uint FooterAnchorNodeId = 83;
    private const float DepositWidth = 84f;
    private const float WithdrawWidth = 92f;
    private const float ButtonHeight = 21f;
    private const float ButtonGap = 5f;
    private const float RowWidth = DepositWidth + ButtonGap + WithdrawWidth;

    private const ImGuiWindowFlags WindowFlags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoBringToFrontOnFocus;

    public void Draw()
    {
        var addon = transfer.GetVisibleSaddlebag();
        if (addon == null || addon->RootNode == null || !addon->RootNode->IsVisible())
            return;

        var scale = addon->Scale;
        if (!float.IsFinite(scale) || scale <= 0)
            return;

        Bounds window = default;
        addon->GetRootBounds(&window);
        var rowSize = new Vector2(RowWidth, ButtonHeight) * scale;

        // Leave space at the right for Pandora's Entrust Duplicates overlay.
        var right = window.Pos2.X - (136f + ButtonGap) * scale;
        var top = window.Pos2.Y - 39f * scale;
        var anchor = addon->GetNodeById(FooterAnchorNodeId);
        if (anchor != null)
        {
            Bounds anchorBounds = default;
            anchor->GetBounds(&anchorBounds);
            var anchorTop = anchorBounds.Pos1.Y;
            if (anchorTop >= window.Pos2.Y - 48f * scale &&
                anchorTop <= window.Pos2.Y - ButtonHeight * scale &&
                anchorBounds.Pos2.X >= window.CenterX &&
                anchorBounds.Pos2.X < window.Pos2.X)
            {
                top = anchorTop;
                right = MathF.Min(right, anchorBounds.Pos2.X + 12f - ButtonGap * scale);
            }
        }

        var position = new Vector2(right - rowSize.X, top);
        if (position.X < window.Pos1.X || position.Y < window.Pos1.Y || top + rowSize.Y > window.Pos2.Y)
            return;

        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(position);
        ImGui.SetNextWindowSize(new Vector2(MathF.Ceiling(rowSize.X), MathF.Ceiling(rowSize.Y)));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ButtonGap * scale, 0));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.34f, 0.36f, 0.39f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.44f, 0.47f, 0.51f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.27f, 0.3f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.96f, 0.97f, 0.98f, 1f));
        try
        {
            if (!ImGui.Begin("###SaddlebagManagerFooter", WindowFlags))
                return;

            ImGui.SetWindowFontScale(1f);
            ImGui.SetWindowFontScale(13f * scale / ImGui.GetFontSize());
            ImGui.BeginDisabled(transfer.IsRunning);
            if (ImGui.Button("Deposit All", new Vector2(DepositWidth * scale, rowSize.Y)))
                openConfirmation(true);
            ImGui.SameLine();
            if (ImGui.Button("Withdraw All", new Vector2(WithdrawWidth * scale, rowSize.Y)))
                openConfirmation(false);
            ImGui.EndDisabled();
        }
        finally
        {
            ImGui.End();
            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(6);
        }
    }
}
