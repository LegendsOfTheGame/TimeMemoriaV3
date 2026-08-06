namespace TimeMemoria.Windows.Native;

/// <summary>
/// The same answers the ImGui Help tab gives — behaviour that looks like a bug
/// and is not, plus what the plugin will never do.
/// </summary>
public class HelpPanelNode : TextLinesPanelNode
{
  protected override List<string> BuildLines() =>
  [
    "Why is a whole expansion greyed out?",
    "   You have not reached it yet, so its name and counts are hidden.",
    "   Settings > Spoiler Mode shows them anyway. Free Trial Mode is",
    "   separate and cannot be overridden — there is nothing there to reveal.",
    "Why does a quest say \"Pre-Plugin\" instead of a date?",
    "   It was already complete when the plugin was first installed, so there",
    "   is no record of when you did it. Anything since is dated properly.",
    "Why is my playtime blank or out of date?",
    "   The game only reveals total playtime in the reply to /playtime, and",
    "   this plugin will not run commands for you. Type it yourself and the",
    "   figure is captured. The age beside it is when that happened.",
    "Why is session pacing empty?",
    "   It measures quests completed since you logged in. Until you finish",
    "   one this session there is nothing to average.",
    "Where do the percentages come from?",
    "   Quests your character can never take — the starting city and class",
    "   routes you did not pick, the Grand Company you did not join — are left",
    "   out. Two characters can legitimately show different totals.",
    "An event is running in game but not listed. Or the reverse.",
    "   Active Events reads the client, so anything switched on shows up even",
    "   if no article announced it. End dates come from the Lodestone feed,",
    "   so an event the feed missed appears without one.",
    "Where does my exported data go?",
    "   Onto your clipboard, and nowhere else. The only network request this",
    "   plugin makes is fetching the public Lodestone news feed.",
    "Commands",
    "   /timememoria    the window chosen in Settings  (/tm for short)",
    "   /tm classic     the classic window, whichever is chosen",
    "   /tm native      this window, whichever is chosen",
    "   /tmmini         playtime, pacing and jobs, in a small window",
    "   /tm reset       rebuild the quest tree",
    "   The plugin registers these; it never sends commands itself.",
    "What this plugin will never do",
    "   No damage meters, combat logs or duty results.",
    "   No automation, and no commands issued on your behalf.",
    "   No toasts or overlays interrupting your play.",
    "   No reading of other characters' data.",
    "   It is a notebook, not a scoreboard. If a feature request needs any of",
    "   the above, it will be declined.",
    "Something wrong, or missing?",
    "   Miscounts and missing quests are worth reporting — quest data changes",
    "   with every patch and this cannot all be tested by hand.",
    "   github.com/LegendsOfTheGame/TimeMemoriaV3/issues",
    "   For clickable links, open the classic window: /tm classic"
  ];

}
