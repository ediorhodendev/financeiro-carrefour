namespace Financial.Domain.Customers;
public sealed class Customer
{
    private Customer() { }
    public Customer(string name, string subjectId, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150) throw new ArgumentException("Invalid name.", nameof(name));
        if (string.IsNullOrWhiteSpace(subjectId) || subjectId.Length > 200 || subjectId != subjectId.Trim()) throw new ArgumentException("Invalid subject.", nameof(subjectId));
        Id = Guid.NewGuid(); Name = name.Trim(); SubjectId = subjectId; CreatedAt = createdAt.ToUniversalTime();
    }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string SubjectId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
