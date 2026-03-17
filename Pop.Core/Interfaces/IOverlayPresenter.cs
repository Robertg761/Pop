using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Interfaces;

public interface IOverlayPresenter
{
    void Update(SnapTarget target, Rectangle bounds);

    void Hide();
}
