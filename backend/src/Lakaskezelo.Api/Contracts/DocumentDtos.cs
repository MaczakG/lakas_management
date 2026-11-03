namespace Lakaskezelo.Api.Contracts;

public record DocumentDto(
    Guid Id, string Name, string FileName, string ContentType, long SizeBytes, Guid? DocumentTypeId, string? DocumentTypeName,
    string? Note, int CurrentVersion,
    Guid? PropertyId, string? PropertyName, Guid? TenantId, string? TenantName, Guid? OwnerId, string? OwnerName,
    DateTime UploadedAt, string? UploadedByName);

public record DocumentVersionDto(int Number, string FileName, string ContentType, long SizeBytes, DateTime UploadedAt, string? UploadedByName);

public record DocumentEventDto(string Type, int? VersionNumber, string? UserName, DateTime At);

public record DocumentDetailDto(DocumentDto Document, List<DocumentVersionDto> Versions, List<DocumentEventDto> Events);

public record UpdateDocumentRequest(string Name, Guid? DocumentTypeId, string? Note, Guid? PropertyId, Guid? TenantId, Guid? OwnerId);

public record DocumentTypeDto(Guid Id, string Name, int SortOrder, int DocumentCount);

public record UpsertDocumentTypeRequest(string Name);
