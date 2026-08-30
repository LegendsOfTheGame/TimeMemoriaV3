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
      ExpansionId = ExpansionId,
      Section = Section,
      EnglishSection = EnglishSection,
      FestivalId = FestivalId
    };
  }
}
