namespace SC.ScriptForge.Models;

public sealed record ScriptReviewSnapshot(Guid Id, string Path, string Code, string ContentHash, ScriptMetadata Metadata, string MetadataHash);
