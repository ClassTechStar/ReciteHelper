using ReciteHelper.SharedKernel;

namespace ReciteHelper.Core.Entities;

public class Semantics : Entity
{
    public int Id { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Summary { get; set; }

    // Identity is the surrogate Id. Summary-based equality silently dropped
    // knowledge points with identical (often empty) summaries during clustering.
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
            return true;
        return obj is Semantics other && other.Id == this.Id;
    }

    public override int GetHashCode()
    {
        return Id;
    }
}
