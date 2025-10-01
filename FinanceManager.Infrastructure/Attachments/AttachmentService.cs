using System.Security.Cryptography;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Attachments;

public sealed class AttachmentService : IAttachmentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AttachmentService> _logger;
    private const int MaxTake = 200;

    public AttachmentService(AppDbContext db, ILogger<AttachmentService> logger)
    {
        _db = db; _logger = logger;
    }

    public async Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var size = (long)bytes.Length;
        string sha = ComputeSha256(bytes);

        var entity = new Attachment(ownerUserId, kind, entityId, fileName, contentType, size, sha, categoryId, bytes, null);
        _db.Attachments.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Attachment uploaded {AttachmentId} kind={Kind} entity={EntityId} size={Size}", entity.Id, kind, entityId, size);
        return Map(entity);
    }

    public async Task<AttachmentDto> CreateUrlAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string url, string? fileName, Guid? categoryId, CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? new Uri(url).Segments.LastOrDefault() ?? url : fileName;
        var entity = new Attachment(ownerUserId, kind, entityId, name, "text/uri-list", 0, null, categoryId, null, url);
        _db.Attachments.Add(entity);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Attachment URL created {AttachmentId} kind={Kind} entity={EntityId} url={Url}", entity.Id, kind, entityId, url);
        return Map(entity);
    }

    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxTake);
        return await _db.Attachments.AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId && a.EntityKind == kind && a.EntityId == entityId)
            .OrderByDescending(a => a.UploadedUtc)
            .Skip(skip)
            .Take(take)
            .Select(a => new AttachmentDto(a.Id, (short)a.EntityKind, a.EntityId, a.FileName, a.ContentType, a.SizeBytes, a.CategoryId, a.UploadedUtc, a.Url != null))
            .ToListAsync(ct);
    }

    public async Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct)
    {
        var a = await _db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId && a.OwnerUserId == ownerUserId, ct);
        if (a == null) { return null; }
        // If this is a reference-only attachment, resolve master
        if (a.Content == null && string.IsNullOrWhiteSpace(a.Url) && a.ReferenceAttachmentId.HasValue)
        {
            var master = await _db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == a.ReferenceAttachmentId.Value && x.OwnerUserId == ownerUserId, ct);
            if (master == null || master.Content == null) { return null; }
            return (new MemoryStream(master.Content, writable: false), master.FileName, master.ContentType);
        }
        if (a.Content == null) { return null; }
        return (new MemoryStream(a.Content, writable: false), a.FileName, a.ContentType);
    }

    public async Task<bool> DeleteAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct)
    {
        var a = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.OwnerUserId == ownerUserId, ct);
        if (a == null) { return false; }

        // If the selected attachment is only a reference, delete the master instead (and thereby all references)
        var targetId = a.ReferenceAttachmentId ?? a.Id;
        Attachment? target;
        if (a.ReferenceAttachmentId.HasValue)
        {
            target = await _db.Attachments.FirstOrDefaultAsync(x => x.Id == targetId && x.OwnerUserId == ownerUserId, ct);
            if (target == null)
            {
                // Master is missing; fall back to deleting the reference itself
                _db.Attachments.Remove(a);
                await _db.SaveChangesAsync(ct);
                return true;
            }
        }
        else
        {
            target = a;
        }

        // Delete only the master; DB FK (OnDelete Cascade) removes references automatically
        _db.Attachments.Remove(target);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateCategoryAsync(Guid ownerUserId, Guid attachmentId, Guid? categoryId, CancellationToken ct)
    {
        var a = await _db.Attachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.OwnerUserId == ownerUserId, ct);
        if (a == null) { return false; }
        a.SetCategory(categoryId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UpdateCoreAsync(Guid ownerUserId, Guid attachmentId, string? fileName, Guid? categoryId, CancellationToken ct)
    {
        var a = await _db.Attachments.FirstOrDefaultAsync(x => x.Id == attachmentId && x.OwnerUserId == ownerUserId, ct);
        if (a == null) { return false; }
        if (!string.IsNullOrWhiteSpace(fileName) && !string.Equals(fileName, a.FileName, StringComparison.Ordinal))
        {
            a.Rename(fileName);
            // Propagate name change to referencing attachments to keep consistent display names
            var refs = await _db.Attachments.Where(x => x.ReferenceAttachmentId == a.Id && x.OwnerUserId == ownerUserId).ToListAsync(ct);
            foreach (var r in refs)
            {
                r.Rename(a.FileName);
            }
        }
        a.SetCategory(categoryId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReassignAsync(AttachmentEntityKind fromKind, Guid fromId, AttachmentEntityKind toKind, Guid toId, Guid ownerUserId, CancellationToken ct)
    {
        await _db.Attachments
            .Where(a => a.OwnerUserId == ownerUserId && a.EntityKind == fromKind && a.EntityId == fromId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.EntityKind, toKind).SetProperty(a => a.EntityId, toId), ct);
    }

    // NEW: create reference attachment pointing to a master attachment (bank posting main)
    public async Task<AttachmentDto> CreateReferenceAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid masterAttachmentId, CancellationToken ct)
    {
        var master = await _db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == masterAttachmentId && a.OwnerUserId == ownerUserId, ct);
        if (master == null) { throw new ArgumentException("Master attachment not found"); }
        var copy = new Attachment(ownerUserId, kind, entityId, master.FileName, master.ContentType, 0L, master.Sha256, master.CategoryId, null, null, master.Id);
        var entry = _db.Attachments.Add(copy);
        await _db.SaveChangesAsync(ct);
        return Map(copy);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static AttachmentDto Map(Attachment a)
        => new AttachmentDto(a.Id, (short)a.EntityKind, a.EntityId, a.FileName, a.ContentType, a.SizeBytes, a.CategoryId, a.UploadedUtc, a.Url != null);
}

public sealed class AttachmentCategoryService : IAttachmentCategoryService
{
    private readonly AppDbContext _db;

    public AttachmentCategoryService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<AttachmentCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
        => await _db.AttachmentCategories.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Select(c => new AttachmentCategoryDto(c.Id, c.Name, c.IsSystem))
            .ToListAsync(ct);

    public async Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct)
    {
        var exists = await _db.AttachmentCategories.AnyAsync(c => c.OwnerUserId == ownerUserId && c.Name == name, ct);
        if (exists) { throw new ArgumentException("Category name already exists"); }
        var cat = new AttachmentCategory(ownerUserId, name);
        _db.AttachmentCategories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return new AttachmentCategoryDto(cat.Id, cat.Name, cat.IsSystem);
    }

    public async Task<bool> DeleteAsync(Guid ownerUserId, Guid id, CancellationToken ct)
    {
        var anyUse = await _db.Attachments.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.CategoryId == id, ct);
        if (anyUse) { throw new InvalidOperationException("Category is in use"); }
        var cat = await _db.AttachmentCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (cat == null) { return false; }
        _db.AttachmentCategories.Remove(cat);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
