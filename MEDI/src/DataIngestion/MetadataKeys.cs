namespace CommunityToolkit.DataIngestion;

/// <summary>
/// Well-known metadata keys written by the ingestion chunk processors in this package.
/// </summary>
public static class MetadataKeys
{
    /// <summary>Comma-separated list of people entities extracted from the chunk.</summary>
    public const string EntitiesPeople = "entities_people";

    /// <summary>Comma-separated list of organization entities extracted from the chunk.</summary>
    public const string EntitiesOrganizations = "entities_organizations";

    /// <summary>Comma-separated list of technology entities extracted from the chunk.</summary>
    public const string EntitiesTechnologies = "entities_technologies";

    /// <summary>Comma-separated list of version strings extracted from the chunk.</summary>
    public const string EntitiesVersions = "entities_versions";

    /// <summary>Primary topic label assigned by topic classification.</summary>
    public const string TopicPrimary = "topic_primary";

    /// <summary>Comma-separated secondary topic labels assigned by topic classification.</summary>
    public const string TopicSecondary = "topic_secondary";

    /// <summary>The type of chunk (original, hypothetical_query, branch_summary, root_summary).</summary>
    public const string ChunkType = "chunk_type";

    /// <summary>The identifier of the parent chunk (for hypothetical query chunks).</summary>
    public const string ParentChunkId = "parent_chunk_id";

    /// <summary>Pipe-delimited hypothetical questions generated for a chunk.</summary>
    public const string HypotheticalQuestions = "hypothetical_questions";

    /// <summary>Tree hierarchy level (0 = leaf, 1 = branch, 2 = root).</summary>
    public const string Level = "level";

    /// <summary>The identifier of the parent node in the tree index hierarchy.</summary>
    public const string ParentId = "parent_id";

    /// <summary>Chunk type value for original (leaf) content chunks.</summary>
    public const string ChunkTypeOriginal = "original";

    /// <summary>Chunk type value for hypothetical query expansion chunks.</summary>
    public const string ChunkTypeHypotheticalQuery = "hypothetical_query";

    /// <summary>Chunk type value for document-level (branch) summary chunks.</summary>
    public const string ChunkTypeBranchSummary = "branch_summary";

    /// <summary>Chunk type value for corpus-level (root) summary chunks.</summary>
    public const string ChunkTypeRootSummary = "root_summary";
}
