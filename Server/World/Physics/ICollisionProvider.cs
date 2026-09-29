using Shared.Math;

namespace Server.World.Physics
{
    public interface ICollisionProvider
    {
        bool ContainsPoint(Vector3 point);
    }
}
