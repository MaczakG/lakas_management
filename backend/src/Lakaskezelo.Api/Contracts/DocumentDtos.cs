namespace Lakaskezelo.Api.Contracts;

public record DocumentDto(
    Guid Id, string Name, string FileName, string ContentType, long SizeBytes, string Category, string? Note,
    Guid? PropertyId, string? PropertyName, Guid? TenantId, string? TenantName, Guid? OwnerId, string? OwnerName,
    DateTime UploadedAt, string? UploadedByName);

public record UpdateDocumentRequest(string Name, string Category, string? Note, Guid? PropertyId, Guid? TenantId, Guid? OwnerId);
