using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Interfaces;

public interface IOverlayPresenter
{
    void Show(SnapTarget target, Rectangle bounds);

    void Hide();
}
