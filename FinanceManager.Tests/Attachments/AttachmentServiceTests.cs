using System.Text;
using FinanceManager.Domain.Attachments;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Attachments;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceManager.Tests.Attachments;

public sealed class AttachmentServiceTests
{
    private static (AttachmentService svc, AppDbContext db, SqliteConnection conn, Guid ownerId) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var owner = new FinanceManager.Domain.Users.User("owner", "hash", true);
        db.Users.Add(owner);
        db.SaveChanges();
        var svc = new AttachmentService(db, NullLogger<AttachmentService>.Instance);
        return (svc, db, conn, owner.Id);
    }

    [Fact]
    public async Task UploadAsync_StoresBlobAndSha()
    {
        var (svc, db, conn, owner) = Create();
        var entityId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("hello world");
        await using var ms = new MemoryStream(bytes);

        var dto = await svc.UploadAsync(owner, AttachmentEntityKind.StatementDraft, entityId, ms, "hello.txt", "text/plain", null, CancellationToken.None);

        dto.Should().NotBeNull();
        dto.IsUrl.Should().BeFalse();
        dto.FileName.Should().Be("hello.txt");
        dto.ContentType.Should().Be("text/plain");
        dto.SizeBytes.Should().Be(bytes.Length);

        var stored = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.Id);
        stored.Should().NotBeNull();
        stored!.OwnerUserId.Should().Be(owner);
        stored.EntityKind.Should().Be(AttachmentEntityKind.StatementDraft);
        stored.EntityId.Should().Be(entityId);
        stored.Content.Should().NotBeNull();
        stored.Url.Should().BeNull();
        stored.Content!.Length.Should().Be(bytes.Length);

        conn.Dispose();
    }

    [Fact]
    public async Task CreateUrlAsync_StoresUrl()
    {
        var (svc, db, conn, owner) = Create();
        var dto = await svc.CreateUrlAsync(owner, AttachmentEntityKind.Contact, Guid.NewGuid(), "https://example.com/a.pdf", null, null, CancellationToken.None);

        dto.IsUrl.Should().BeTrue();
        var stored = await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == dto.Id);
        stored.Url.Should().Be("https://example.com/a.pdf");
        stored.Content.Should().BeNull();

        conn.Dispose();
    }

    [Fact]
    public async Task ListAsync_FiltersAndSorts()
    {
        var (svc, db, conn, owner) = Create();
        var entityId = Guid.NewGuid();
        async Task<Guid> Upload(string name)
        {
            await using var s = new MemoryStream(Encoding.UTF8.GetBytes(name));
            var dto = await svc.UploadAsync(owner, AttachmentEntityKind.StatementDraft, entityId, s, name, "text/plain", null, CancellationToken.None);
            return dto.Id;
        }
        var id1 = await Upload("a1.txt");
        var id2 = await Upload("a2.txt");
        var id3 = await Upload("a3.txt");

        var list = await svc.ListAsync(owner, AttachmentEntityKind.StatementDraft, entityId, 0, 10, CancellationToken.None);
        list.Select(x => x.Id).Should().ContainInOrder(new[] { id3, id2, id1 });

        // Different entity filtered out
        await using var s2 = new MemoryStream(Encoding.UTF8.GetBytes("x"));
        await svc.UploadAsync(owner, AttachmentEntityKind.StatementDraft, Guid.NewGuid(), s2, "x.txt", "text/plain", null, CancellationToken.None);
        var list2 = await svc.ListAsync(owner, AttachmentEntityKind.StatementDraft, entityId, 0, 10, CancellationToken.None);
        list2.Should().HaveCount(3);

        conn.Dispose();
    }

    [Fact]
    public async Task Download_UpdateCategory_Delete_Reassign_Work()
    {
        var (svc, db, conn, owner) = Create();
        var entityId = Guid.NewGuid();
        var otherEntity = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("content");
        await using var ms = new MemoryStream(content);
        var dto = await svc.UploadAsync(owner, AttachmentEntityKind.StatementDraft, entityId, ms, "c.txt", "text/plain", null, CancellationToken.None);

        var dl = await svc.DownloadAsync(owner, dto.Id, CancellationToken.None);
        dl.Should().NotBeNull();
        using (var reader = new StreamReader(dl!.Value.Content, Encoding.UTF8))
        {
            var txt = await reader.ReadToEndAsync();
            txt.Should().Be("content");
        }

        // category
        var cat = new AttachmentCategory(owner, "Docs");
        db.AttachmentCategories.Add(cat);
        await db.SaveChangesAsync();
        (await svc.UpdateCategoryAsync(owner, dto.Id, cat.Id, CancellationToken.None)).Should().BeTrue();
        (await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == dto.Id)).CategoryId.Should().Be(cat.Id);

        // reassign
        await svc.ReassignAsync(AttachmentEntityKind.StatementDraft, entityId, AttachmentEntityKind.StatementEntry, otherEntity, owner, CancellationToken.None);
        var moved = await db.Attachments.AsNoTracking().FirstAsync(a => a.Id == dto.Id);
        moved.EntityKind.Should().Be(AttachmentEntityKind.StatementEntry);
        moved.EntityId.Should().Be(otherEntity);

        // delete
        (await svc.DeleteAsync(owner, dto.Id, CancellationToken.None)).Should().BeTrue();
        (await db.Attachments.AsNoTracking().AnyAsync(a => a.Id == dto.Id)).Should().BeFalse();

        conn.Dispose();
    }
}
