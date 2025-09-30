namespace Celbridge.Screenplay.Models;

public enum CharacterType
{
    Player,
    PlayerVariant,
    NPC
}

public record Character(string CharacterId, string Name, string Tag, CharacterType CharacterType);
