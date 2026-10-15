namespace TimeMemoria.Types;

public class QuestData : ICloneable
{
  public string Title { get; set; } = "";
  public string EnglishTitle { get; set; } = "";
  public List<QuestData> Categories { get; set; } = [];
  public List<Quest> Quests { get; set; } = [];
  public float NumComplete { get; set; }
  public float Total { get; set; }
  public bool Hide { get; set; }
  public uint SortKey { get; set; }

  public object Clone()
  {
    QuestData copy = new()
    {
      Title = Title,
      EnglishTitle = EnglishTitle,
      NumComplete = NumComplete,
      Total = Total,
      Hide = Hide,
      SortKey = SortKey,
      Categories = new List<QuestData>(Categories.Count),
      Quests = new List<Quest>(Quests.Count)
    };

    foreach (QuestData cat in Categories)
    {
      copy.Categories.Add((QuestData)cat.Clone());
    }

    foreach (Quest q in Quests)
    {
      copy.Quests.Add((Quest)q.Clone());
    }

    return copy;
  }
}

/// <summary>One item a delivery leve asks for, and how many per turn-in.</summary>
public record LeveItem(uint ItemId, int Count, string Name)
{
  public override string ToString() => $"{Count}x {Name}";
}

public class Quest : ICloneable
{
  public required string Title { get; set; }
  public required List<uint> Ids { get; set; }
  public required string Area { get; set; }
  public string? Start { get; set; }
  public string? Gc { get; set; }
  public int Level { get; set; }
  public bool Hide { get; set; }
  public uint SortKey { get; set; }
  public bool IsLeve { get; set; }

  /// <summary>Leves only: who can take it — "Carpenter", or "Disciples of War
  /// or Magic" for battlecraft and Grand Company leves. Null for quests.</summary>
  public string? Job { get; set; }

  /// <summary>Leves only: how many times the items can be handed in for one
  /// allowance. 1 for everything except Charity and Sincerity leves (3, or 6
  /// for the Heavensward "(L)" ones).</summary>
  public int TurnIns { get; set; } = 1;

  /// <summary>Delivery leves only: what one turn-in needs. Each prints as a
  /// "3x Item Name" line, the shape Teamcraft and Allagan Tools import. Empty
  /// for everything else.</summary>
  public List<LeveItem> RequiredItems { get; set; } = [];

  /// <summary>ExVersion row id. 0 is A Realm Reborn.</summary>
  public uint ExpansionId { get; set; }

  /// <summary>Journal section, localised — "Main Scenario", "Levequests".</summary>
  public string Section { get; set; } = "";

  /// <summary>The same section in English, for matching against settings.</summary>
  public string EnglishSection { get; set; } = "";

  /// <summary>Quest.Festival row id. 0 for a quest with no seasonal gate.</summary>
  public uint FestivalId { get; set; }

  public object Clone()
  {
    return new Quest
    {
      Title = Title,
      Ids = [.. Ids],
      Area = Area,
      Start = Start,
      Gc = Gc,
      Level = Level,
      Hide = Hide,
      SortKey = SortKey,
      IsLeve = IsLeve,
      Job = Job,
      TurnIns = TurnIns,
      RequiredItems = [.. RequiredItems],
      ExpansionId = ExpansionId,
      Section = Section,
      EnglishSection = EnglishSection,
      FestivalId = FestivalId
    };
  }
}
