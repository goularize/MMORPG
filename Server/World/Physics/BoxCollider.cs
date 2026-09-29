using Shared.Math;

namespace Server.World.Physics
{
    public class BoxCollider : ICollisionProvider
    {
        private readonly Vector3 _center;
        private readonly Vector3 _size; // Total size (width, height)

        public BoxCollider(Vector3 center, Vector3 size)
        {
            _center = center;
            _size = size;
        }

        public bool ContainsPoint(Vector3 point)
        {
            float halfWidth = _size.X / 2f;
            float halfHeight = _size.Y / 2f;

            return point.X >= _center.X - halfWidth &&
                   point.X <= _center.X + halfWidth &&
                   point.Y >= _center.Y - halfHeight &&
                   point.Y <= _center.Y + halfHeight;
        }
    }
}
