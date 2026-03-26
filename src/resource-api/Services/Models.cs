namespace ResourceApi.Services;

public sealed class ResourceState
{
    public List<ImageAsset> Images { get; set; } = [];

    public List<PuzzleDefinition> Puzzles { get; set; } = [];

    public List<PackDefinition> Packs { get; set; } = [];

    public List<PlayerProgress> ProgressEntries { get; set; } = [];

    public List<string> Tags { get; set; } = [];
}

public sealed class ImageAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string SourceType { get; set; } = "url";

    public List<string> Tags { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PuzzleDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Answer { get; set; } = string.Empty;

    public string? Hint { get; set; }

    public string Difficulty { get; set; } = "medium";

    public List<string> ImageIds { get; set; } = [];

    public List<string> AcceptableVariants { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PackDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Visibility { get; set; } = "public";

    public string Status { get; set; } = "draft";

    public int SortOrder { get; set; }

    public List<string> PuzzleIds { get; set; } = [];

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PlayerProgress
{
    public string UserId { get; set; } = string.Empty;

    public int Score { get; set; }

    public Dictionary<string, int> AttemptsByPuzzleId { get; set; } = new();

    public Dictionary<string, DateTime> SolvedAtByPuzzleId { get; set; } = new();
}

public sealed class NextPuzzleResult
{
    public string PackId { get; set; } = string.Empty;

    public bool PackCompleted { get; set; }

    public PuzzleDefinition? Puzzle { get; set; }
}

public sealed class MutationResult<T>
{
    public T? Value { get; set; }

    public string? Error { get; set; }
}

