using Celbridge.Entities;

namespace Celbridge.Core.Components;

public class EmptyEditor : ComponentEditorBase
{
    public override string ComponentConfigPath => "Celbridge.Core.Assets.Components.Empty.json";

    public override Result<ComponentSummary> GetComponentSummary()
    {
        var getComment = GetProperty("/comment");
        if (getComment.IsFailure)
        {
            return Result<ComponentSummary>.Fail(getComment.Error);
        }
        var comment = getComment.Value;

        var summary = new ComponentSummary(comment, comment);

        return Result<ComponentSummary>.Ok(summary);
    }
}
