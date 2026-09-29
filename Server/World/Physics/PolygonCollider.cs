using System.Collections.Generic;
using Shared.Math;

namespace Server.World.Physics
{
    public class PolygonCollider : ICollisionProvider
    {
        private readonly List<Vector3> _vertices;

        public PolygonCollider(List<Vector3> vertices)
        {
            _vertices = vertices;
        }

        // Standard Ray-casting / Point-in-polygon algorithm
        public bool ContainsPoint(Vector3 point)
        {
            bool result = false;
            int j = _vertices.Count - 1;
            for (int i = 0; i < _vertices.Count; i++)
            {
                if ((_vertices[i].Y < point.Y && _vertices[j].Y >= point.Y || _vertices[j].Y < point.Y && _vertices[i].Y >= point.Y) &&
                    (_vertices[i].X <= point.X || _vertices[j].X <= point.X))
                {
                    if (_vertices[i].X + (point.Y - _vertices[i].Y) / (_vertices[j].Y - _vertices[i].Y) * (_vertices[j].X - _vertices[i].X) < point.X)
                    {
                        result = !result;
                    }
                }
                j = i;
            }
            return result;
        }
    }
}
