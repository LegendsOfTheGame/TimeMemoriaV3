namespace TimeMemoria.Windows.Native;

/// <summary>
/// The same answers the ImGui Help tab gives — behaviour that looks like a bug
/// and is not, plus what the plugin will never do.
/// </summary>
public class HelpPanelNode : TextLinesPanelNode
{
  /// <summary>
  /// (Question, answer paragraphs) pairs. Kept as full sentences rather than
  /// pre-broken lines — <see cref="WrapParagraph"/> measures each one against
  /// the panel's actual width when the panel is shown.
  /// </summary>
  private static readonly (string Question, string[] Paragraphs)[] Entries =
  [
    ("Why is a whole expansion greyed out?",
    [
      "You have not reached this expansion yet. The plugin hides its name and quest count. Go to Settings > Spoiler Mode to show them.",
      "Free Trial Mode is different. You cannot turn off this limit. A Free Trial account has no hidden data to show."
    ]),
    ("Why does a quest say \"Pre-Plugin\" instead of a date?",
    [
      "You completed this quest before you installed the plugin. The plugin has no record of the date. The plugin records the date for every quest you complete after that."
    ]),
    ("Why is my playtime blank or out of date?",
    [
      "The game shows your total playtime only in the reply to the /playtime command. The plugin does not run this command for you. Type /playtime yourself. The plugin then reads the reply and stores the figure. The time beside the figure shows when the plugin last read it."
    ]),
    ("Why is session pacing empty?",
    [
      "Session pacing counts quests you complete after you log in. Complete one quest this session. Then the plugin can calculate an average."
    ]),
    ("Where do the percentages come from?",
    [
      "The plugin removes quests your character cannot take from the total. Examples: quests for a starting city you did not pick, quests for a class route you did not pick, and quests for a Grand Company you did not join. Because of this, two characters can correctly show different totals."
    ]),
    ("Why do collectables say \"open Achievements once\"?",
    [
      "The game does not keep a running count. The count exists only inside an achievement record. The game client downloads an achievement record only when you view that achievement. So the plugin waits until you open the Achievements window. The plugin cannot ask the game server directly.",
      "A number with a plus sign is a minimum value, not your exact count. This happens on a tier you already completed: the plugin shows that tier's requirement instead of your real total. To see your exact count, open a later tier you have not completed yet."
    ]),
    ("The Wiki button beside the quest search",
    [
      "This button sends your search text to the FFXIV wiki's search function. If the text matches a page title exactly, the wiki opens that page directly.",
      "This also works for shorthand terms such as A8S, TEA, and DRS. These terms are not quest names, so the quest search cannot find them. The wiki redirects each term to its correct page.",
      "If the search box is empty, the button opens the wiki's Main Scenario index. This index lists the main story in order."
    ]),
    ("Why does Overview show a smaller total than the Quests tree?",
    [
      "The Overview tab applies your exclusion settings. The Overview tab shows an excluded category in grey and removes it from the total. The Quests tree does not apply exclusion settings. You use the Quests tree to open a quest. An excluded category stays visible in the tree, so you can still reach its quests."
    ]),
    ("An event is running in game but not listed. Or the reverse.",
    [
      "The Active Events list reads data from the game client. An event appears in the list as soon as the game turns it on, even before a news article announces it. The plugin gets event end dates from the Lodestone news feed. If the feed does not report an event, the event stays in the list without an end date."
    ]),
    ("Where does my exported data go?",
    [
      "Exported data goes to your clipboard only. The plugin sends it nowhere else. The plugin makes one network request: it downloads the public Lodestone news feed."
    ]),
    ("Commands",
    [
      "/timememoria    the window chosen in Settings  (/tm for short)\n/tm classic     the classic window, whichever is chosen\n/tm native      this window, whichever is chosen\n/tmmini         playtime, pacing and jobs, in a small window\n/tm reset       rebuild the quest tree",
      "The plugin registers these commands. The plugin never sends a command itself."
    ]),
    ("What this plugin will never do",
    [
      "No damage meters, combat logs or duty results.\nNo automation, and no commands issued on your behalf.\nNo toasts or overlays interrupting your play.\nNo reading of other characters' data.",
      "This plugin only tracks quest progress. This plugin does not rank your performance. The maintainer declines any feature request that needs an action listed above."
    ]),
    ("Something wrong, or missing?",
    [
      "Report miscounts and missing quests. Quest data changes with every game patch. The maintainer cannot test all of it by hand.",
      "github.com/LegendsOfTheGame/TimeMemoriaV3/issues\nFor clickable links, open the classic window: /tm classic"
    ])
  ];

  protected override List<string> BuildLines()
  {
    List<string> lines = [];

    foreach ((string question, string[] paragraphs) in Entries)
    {
      lines.Add(question);

      foreach (string paragraph in paragraphs)
      {
        // A command list or bullet list is already one item per line and
        // must not be re-flowed into prose — only prose paragraphs go
        // through the wrapper.
        if (paragraph.Contains('\n'))
          lines.AddRange(paragraph.Split('\n').Select((line) => $"   {line}"));
        else
          lines.AddRange(WrapParagraph(paragraph));
      }
    }

    return lines;
  }

}
