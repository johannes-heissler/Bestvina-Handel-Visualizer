using System;
using System.Collections.Generic;
using System.Linq;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;


public abstract partial class Point : IEquatable<Point>, ITransformable<Point>
{
    public virtual GeodesicSurface Surface => ModelSurface.baseGeometrySurfaces[GeometryType.Flat];
    public virtual Vector3 Position => Positions.First();
    public abstract IEnumerable<Vector3> Positions { get; }
    public virtual Vector3 PassThrough(int fromPositionIndex, int toPositionIndex, Vector3 direction) => direction;

    public abstract Point ApplyHomeomorphism(Homeomorphism homeomorphism);

    public virtual bool Equals(Point other) => 
        other != null && 
        Positions.Any(
            position => 
                other.Positions.Any(
                    otherPosition => otherPosition.ApproximatelyEquals(position)
                ));
    


    public virtual float DistanceSquared(Point other) => Surface.DistanceSquared(this, other);
    
    public static implicit operator Point(Vector3 v) => new BasicPoint(v);
    public static implicit operator Point(Vector2 v) => new BasicPoint(v);
    public static implicit operator Vector3(Point p) => p.Position;
    

}

public partial class BasicPoint : Point
{

    public override Vector3 Position { get; }
    public override IEnumerable<Vector3> Positions => new[] { Position };

    public BasicPoint(Vector3 position)
    {
        Position = position;
    }

    public override Point ApplyHomeomorphism(Homeomorphism homeomorphism)
    {
        var result = homeomorphism.F(Position);
        if (!result.HasValue)
            throw new Exception("Homeomorphism is not defined on this point");
        return new BasicPoint(result.Value)
        {
            Color = Color
        };
    }

    public override string ToString() => Position.z.ApproximateEquals(0)
        ? $"({Position.x:0.00}, {Position.y:0.00})"
        : $"({Position.x:0.00}, {Position.y:0.00}, {Position.z:0.00})";

    public static implicit operator BasicPoint(Vector3 v) => new(v);
    public static implicit operator BasicPoint(Vector2 v) => new(v);
}