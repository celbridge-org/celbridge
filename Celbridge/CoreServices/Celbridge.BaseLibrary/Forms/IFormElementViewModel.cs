using Celbridge.Entities;

namespace Celbridge.Forms;

public interface IFormElementViewModel
{
    void Bind(PropertyBinding binding);

    void Unbind();
}
