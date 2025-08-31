namespace FinanceManager.Domain;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; protected set; } = DateTime.UtcNow;
    public DateTime? ModifiedUtc { get; protected set; }

    protected void Touch() => ModifiedUtc = DateTime.UtcNow;
}

public interface IAggregateRoot { }
