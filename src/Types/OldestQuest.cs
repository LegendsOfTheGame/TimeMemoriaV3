namespace TimeMemoria.Types;

/// <summary>
/// One of the oldest quests a character still has outstanding, with the context
/// needed to say where it sits.
///
/// The category is captured while the tree is being walked rather than looked up
/// afterwards, because the breadcrumb only exists at the moment the quest is
/// reached — it is the accumulated path of the nodes descended through, and a
/// quest object carries no back-reference to its parent. Deriving it later would
/// mean a second walk to find a quest already in hand.
/// </summary>
/// <param name="Quest">The quest itself. Shared with the tree — see the caller.</param>
/// <param name="Expansion">Display name of the expansion it belongs to.</param>
/// <param name="Category">Journal path, joined with an em dash.</param>
public sealed record OldestQuest(Quest Quest, string Expansion, string Category);
