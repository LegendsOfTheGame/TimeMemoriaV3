namespace TimeMemoria.Windows.Native;

/// <summary>
/// The work this plugin is built on.
///
/// The addresses are shown but not clickable — a list row is a selectable thing
/// by nature, and a row that highlights on hover while doing nothing is worse
/// than plain text. The classic window has real links, so this points there.
/// </summary>
public class CreditsPanelNode : TextLinesPanelNode
{
  protected override List<string> BuildLines() =>
  [
    "Built on",
    "   isaiahcat — QuestTracker, the plugin this descends from",
    "   keifufu — rebuilt its quest data from the game's own tables",
    "   maxiin — kept QuestTracker current through Dawntrail",
    "Data the game does not provide",
    "   Critical-Impact — LuminaSupplemental, the festival names",
    "   Garland Tools — which patch introduced which quest",
    "Framework",
    "   goatcorp — Dalamud, and FFXIVClientStructs",
    "   MidoriKami — KamiToolKit, which draws this window",
    "This plugin",
    "   Licensed AGPL-3.0. The source, including the data files, is public —",
    "   nothing needed to rebuild it is held back.",
    "   github.com/LegendsOfTheGame/TimeMemoriaV3",
    "   For clickable links, open the classic window: /tm classic",
    "FINAL FANTASY XIV © SQUARE ENIX CO., LTD.",
    "   This plugin is unaffiliated with and unendorsed by Square Enix."
  ];
}
