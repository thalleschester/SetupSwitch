using System.Drawing;

namespace SetupSwitch.App;

/// <summary>Encaixa uma tela na borda de outra, como no organizador do Windows.</summary>
public static class LayoutSnapper
{
    /// <summary>As telas precisam encostar por pelo menos este trecho para o mouse passar.</summary>
    private const int MinimumContact = 100;

    /// <summary>Distância em pixels da tela em que as bordas se alinham sozinhas.</summary>
    private const int AlignTolerance = 150;

    public static Point Snap(Rectangle moving, IReadOnlyList<Rectangle> others)
    {
        var best = moving.Location;
        var bestDistance = double.MaxValue;

        foreach (var other in others)
        {
            foreach (var candidate in Candidates(moving, other))
            {
                var placed = new Rectangle(candidate, moving.Size);
                if (others.Any(o => o.IntersectsWith(placed)))
                {
                    continue;
                }

                var (dx, dy) = ((double)candidate.X - moving.X, (double)candidate.Y - moving.Y);
                var distance = (dx * dx) + (dy * dy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static IEnumerable<Point> Candidates(Rectangle moving, Rectangle other)
    {
        var alignedY = Align(moving.Y, moving.Height, other.Y, other.Height);
        var alignedX = Align(moving.X, moving.Width, other.X, other.Width);

        yield return new Point(other.Left - moving.Width, alignedY);
        yield return new Point(other.Right, alignedY);
        yield return new Point(alignedX, other.Top - moving.Height);
        yield return new Point(alignedX, other.Bottom);
    }

    private static int Align(int position, int size, int otherPosition, int otherSize)
    {
        var contact = Math.Min(MinimumContact, Math.Min(size, otherSize));
        var clamped = Math.Clamp(position, otherPosition - size + contact, otherPosition + otherSize - contact);

        if (Math.Abs(clamped - otherPosition) <= AlignTolerance)
        {
            return otherPosition;
        }

        var bottomAligned = otherPosition + otherSize - size;
        return Math.Abs(clamped - bottomAligned) <= AlignTolerance ? bottomAligned : clamped;
    }
}
