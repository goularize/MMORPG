using Shared.Math;

namespace Server.World.Physics
{
    public class CircleCollider : ICollisionProvider
    {
        private readonly Vector3 _center;
        private readonly float _radius;

        public CircleCollider(Vector3 center, float radius)
        {
            _center = center;
            _radius = radius;
        }

        public bool ContainsPoint(Vector3 point)
        {
            float dx = point.X - _center.X;
            float dy = point.Y - _center.Y;
            return (dx * dx + dy * dy) <= (_radius * _radius);
        }
    }
}
