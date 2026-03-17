using Pop.Core.Models;

namespace Pop.Core.Interfaces;

public interface ISnapDecider
{
    SnapDecision Decide(DragSession session, AppSettings settings);
}
