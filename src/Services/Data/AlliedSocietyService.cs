using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using BeastTribeRow = Lumina.Excel.Sheets.BeastTribe;
using RankRow = Lumina.Excel.Sheets.BeastReputationRank;
using QuestRow = Lumina.Excel.Sheets.Quest;

namespace TimeMemoria.Services;

/// <summary>
/// One allied society's standing. Read live from the client — rank, points into
/// that rank, and what the rank requires.
/// </summary>
public class SocietyStanding
{
  /// <summary>Row in the BeastTribe sheet, which is also the client's index.</summary>
  public required uint Index { get; init; }

  public required string Name { get; init; }

  /// <summary>0 for a society never started.</summary>
  public required int Rank { get; init; }

  /// <summary>"Neutral", "Friendly" and so on. Empty when not started.</summary>
  public required string RankName { get; init; }

  public required int Points { get; init; }

  /// <summary>Points the current rank requires. Zero when not started or at the cap.</summary>
  public required int Needed { get; init; }

  public bool IsStarted => Rank > 0;

  /// <summary>
  /// Points full for this rank. Further dailies award nothing until an allied
  /// society main quest promotes you, which is the one state worth surfacing:
  /// it is when continuing to grind is pointless.
  /// </summary>
  public bool IsCapped => Needed > 0 && Points >= Needed;
}

public interface IAlliedSocietyService
{
  /// <summary>Every society, started or not, in sheet order.</summary>
  List<SocietyStanding> GetStandings();

  /// <summary>Daily allowances left to spend. Deducted when a quest is accepted.</summary>
  int Allowances { get; }

  /// <summary>
  /// Accepted allied society quests. Non-zero is the only circumstance under
  /// which <see cref="Allowances"/> can be wrong: the count does not roll over
  /// at reset while any are held, and unfreezes when the last is cleared.
  /// </summary>
  int HeldQuests { get; }
}

/// <summary>
/// Allied society standing, read from the client rather than tracked.
///
/// Everything here was established by probing the live game before any of it was
/// written — see the allied society notes. The short version: rank and points
/// are exact, quotas come from the BeastReputationRank sheet, and none of it
/// needs a hand-maintained table.
/// </summary>
public unsafe class AlliedSocietyService(ILogger _logger, IDataManager _dataManager) : IAlliedSocietyService
{
  /// <summary>
  /// Ids of every repeatable allied society quest, built once. Asking
  /// IsQuestAccepted of the whole Quest sheet each time would be a five thousand
  /// row scan for a number read every few seconds.
  /// </summary>
  private uint[]? _societyQuestIds;

  public List<SocietyStanding> GetStandings()
  {
    List<SocietyStanding> standings = [];

    PlayerState* player = PlayerState.Instance();
    if (player is null) return standings;

    foreach (BeastTribeRow tribe in _dataManager.GetExcelSheet<BeastTribeRow>())
    {
      string name = SocietyName(tribe);
      if (name.Length == 0) continue;

      byte index = (byte)tribe.RowId;

      int rank = player->GetBeastTribeRank(index);
      int points = player->GetBeastTribeCurrentReputation(index);
      int needed = player->GetBeastTribeNeededReputation(index);

      standings.Add(new SocietyStanding
      {
        Index = tribe.RowId,
        Name = name,
        Rank = rank,
        RankName = RankName(rank),
        Points = points,
        Needed = needed
      });
    }

    return standings;
  }

  public int Allowances
  {
    get
    {
      QuestManager* quests = QuestManager.Instance();
      return quests is null ? 0 : (int)quests->GetBeastTribeAllowance();
    }
  }

  public int HeldQuests
  {
    get
    {
      QuestManager* manager = QuestManager.Instance();
      if (manager is null) return 0;

      int held = 0;

      foreach (uint id in SocietyQuestIds())
        if (manager->IsQuestAccepted(id))
          held++;

      return held;
    }
  }

  /// <summary>
  /// NameRelation is the journal's category name — "Ixali Relations" — which is
  /// right for grouping quests and wrong as a label for the society itself. The
  /// suffix comes off when it is there, which also happens to leave nearly every
  /// name matching what the ledger already calls them.
  ///
  /// Conditional rather than unconditional so a client in another language keeps
  /// whatever its sheet says instead of losing characters off the end.
  /// </summary>
  private static string SocietyName(BeastTribeRow tribe)
  {
    const string suffix = " Relations";

    string name = tribe.NameRelation.ToString();
    return name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;
  }

  /// <summary>
  /// The rank's name from the sheet rather than a table of our own.
  ///
  /// Rank 8 carries two: "Allied" for the A Realm Reborn societies and
  /// "Bloodsworn" for the later ones, in Name and AlliedNames respectively.
  /// Only the first is used here — telling the two groups apart is a
  /// presentation problem and no character has reached it to check against.
  /// </summary>
  private string RankName(int rank)
  {
    if (rank <= 0) return string.Empty;

    RankRow? row = _dataManager.GetExcelSheet<RankRow>().GetRowOrDefault((uint)rank);
    return row?.Name.ToString() ?? string.Empty;
  }

  private uint[] SocietyQuestIds()
  {
    if (_societyQuestIds is not null) return _societyQuestIds;

    List<uint> ids = [];

    foreach (QuestRow quest in _dataManager.GetExcelSheet<QuestRow>())
      if (quest.BeastTribe.RowId != 0 && quest.IsRepeatable)
        ids.Add(quest.RowId);

    _societyQuestIds = [.. ids];
    _logger.Debug($"[Society] {_societyQuestIds.Length} repeatable allied society quests indexed.");

    return _societyQuestIds;
  }
}
