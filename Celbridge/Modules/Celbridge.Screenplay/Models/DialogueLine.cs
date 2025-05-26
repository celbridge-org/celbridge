namespace Celbridge.Screenplay.Models;

public record DialogueLine(
    string LineType,
    string Category,
    string Namespace,
    string DialogueKey,
    string CharacterId,
    string SpeakingTo,
    string SourceText,
    string ContextNotes,
    string Direction,
    string GameArea,
    string TimeConstraint,
    string SoundProcessing,
    string Platform,
    string LinePriority,
    string ProductionStatus);
