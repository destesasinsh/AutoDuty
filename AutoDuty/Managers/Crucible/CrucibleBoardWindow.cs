using ECommons.DalamudServices;

namespace AutoDuty.Managers
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using Dalamud.Bindings.ImGui;
    using Dalamud.Interface.Colors;
    using Dalamud.Interface.Textures;
    using Dalamud.Interface.Textures.TextureWraps;
    using Dalamud.Interface.Utility;
    using Dalamud.Interface.Utility.Raii;
    using Dalamud.Interface.Windowing;
    using ECommons.ImGuiMethods;

    public class CrucibleBoardWindow : Window
    {
        private const float CellSize          = 44f;
        private const int   MapMargin         = 1;
        private const float PathThickness     = 4f;
        private const float StopRadius        = 17f;
        private const float StopRingThickness = 3f;
        private const float IconInset         = 1f;
        private const float HereRingGap       = 5f;
        private const float HereRingThickness = 2f;
        private const int   AutoSegments      = 0;

        private const int LabelWidth = 12;

        public CrucibleBoardWindow() : base("Crucible Board###AutoDutyCrucibleBoard", ImGuiWindowFlags.AlwaysAutoResize)
        {
        }

        private static Vector4 Colour(CrucibleStopKind kind) => kind switch
        {
            CrucibleStopKind.Start    => ImGuiColors.ParsedBlue,
            CrucibleStopKind.Enemy    => ImGuiColors.DalamudGrey,
            CrucibleStopKind.Elite    => ImGuiColors.DalamudViolet,
            CrucibleStopKind.Boss     => ImGuiColors.DalamudRed,
            CrucibleStopKind.Shop     => ImGuiColors.HealerGreen,
            CrucibleStopKind.Campsite => ImGuiColors.DalamudOrange,
            CrucibleStopKind.Treasure => ImGuiColors.DalamudYellow,
            _                         => ImGuiColors.DalamudWhite,
        };

        public override void Draw()
        {
            CrucibleBoard? board = CrucibleBoard.Current();
            if (board == null)
            {
                this.IsOpen = false;
                return;
            }

            CrucibleBoardStop? here = board.StopPlayerIsOn();

            ImGuiEx.Text($"{board.Name}  (board {board.BoardNumber})");
            ImGuiEx.Text(here != null ? $"You are on: {here.Label}" : "You are off the board (fight arena?)");

            if(here != null)
            {
                IEnumerable<CrucibleBoardStop> stops                  = [];
                if(board.HasCampBeforeNextFight(here, ref stops))
                {
                    ImGuiEx.Text($"There is a camp before the next fight");
                    ImGui.Indent();
                    ImGui.Text("Path: " + string.Join("|", stops.Select(cbs => cbs.Label)));
                    ImGui.Unindent();
                }
                else
                    ImGuiEx.Text($"There is no camp before the next fight");
            }

            ImGui.Separator();
            DrawMap(board, here);
            ImGui.SameLine();
            DrawList(board, here);
        }

        private static void DrawMap(CrucibleBoard board, CrucibleBoardStop? here)
        {
            float scale = ImGuiHelpers.GlobalScale;
            int   minX  = board.Stops.Min(x => x.GridX);
            int   maxX  = board.Stops.Max(x => x.GridX);
            int   minY  = board.Stops.Min(x => x.GridY);
            int   maxY  = board.Stops.Max(x => x.GridY);

            Vector2 cells  = new(maxX - minX + 2 * MapMargin, maxY - minY + 2 * MapMargin);
            Vector2 size   = cells * CellSize * scale;
            Vector2 origin = ImGui.GetCursorScreenPos();
            ImGui.Dummy(size);

            ImDrawListPtr draw = ImGui.GetWindowDrawList();

            Vector2 ScreenPos(CrucibleBoardStop stop) => origin + new Vector2(stop.GridX - minX + MapMargin, stop.GridY - minY + MapMargin) * CellSize * scale;

            foreach (CrucibleBoardStop stop in board.Stops)
                foreach (CrucibleBoardStop next in board.NextStops(stop))
                    draw.AddLine(ScreenPos(stop), ScreenPos(next), ImGui.GetColorU32(ImGuiColors.DalamudGrey3), PathThickness * scale);

            foreach (CrucibleBoardStop stop in board.Stops)
            {
                Vector2 pos = ScreenPos(stop);

                draw.AddCircleFilled(pos, StopRadius * scale, ImGui.GetColorU32(ImGuiCol.WindowBg));
                draw.AddCircle(pos, StopRadius * scale, ImGui.GetColorU32(Colour(stop.Kind)), AutoSegments, StopRingThickness * scale);

                if (stop.Icon != 0)
                {
                    IDalamudTextureWrap icon = Svc.Texture.GetFromGameIcon(new GameIconLookup(stop.Icon)).GetWrapOrEmpty();
                    Vector2             half = new Vector2(StopRadius - IconInset) * scale;
                    draw.AddImage(icon.Handle, pos - half, pos + half);
                }

                if (stop == here)
                    draw.AddCircle(pos, (StopRadius + HereRingGap) * scale, ImGui.GetColorU32(ImGuiColors.DalamudWhite), AutoSegments, HereRingThickness * scale);
            }
        }

        private static void DrawList(CrucibleBoard board, CrucibleBoardStop? here)
        {
            using var group = ImRaii.Group();

            foreach (CrucibleBoardStop stop in board.Stops.OrderByDescending(x => x.GridY).ThenBy(x => x.GridX))
            {
                string marker = stop == here ? ">" : " ";
                string next   = string.Join(", ", board.NextStops(stop).Select(x => x.Label));
                string line   = $"{marker} {stop.Label.PadRight(LabelWidth)}";
                if (next.Length > 0)
                    line += $"  -> {next}";

                ImGuiEx.Text(Colour(stop.Kind), line);
            }
        }
    }
}
